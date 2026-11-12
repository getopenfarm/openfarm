using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Auth.OAuth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

/// <summary>The grant behind a connected app's access token, resolved on every request.</summary>
public sealed record AppGrant(string[] Scopes, string ClientName);

/// <summary>
/// Browser sessions and connected apps (a session with <see cref="BrowserSession.ClientId"/> set). Every method
/// states which kind it acts on: a browser session never acts as an app, and an app never as a browser session.
/// </summary>
public sealed class BrowserSessionService(OpenFarmDbContext db, JwtTokenService jwt, IDataProtectionProvider protection,
    OAuthSettings oauth, TimeProvider time)
{
    public const string AppCredentialPrefix = "ofr_";
    public static readonly TimeSpan AppLifetime = TimeSpan.FromDays(30);
    private static readonly SearchValues<char> LowerHex = SearchValues.Create("0123456789abcdef");

    private readonly IDataProtector _protector = protection.CreateProtector("OpenFarm.RefreshRetry.v1");
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string NewCredential() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string NewAppCredential() => AppCredentialPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static bool IsAppCredential(string value) => value.Length == 68 && value.StartsWith(AppCredentialPrefix, StringComparison.Ordinal)
        && value.AsSpan(4).IndexOfAnyExcept(LowerHex) < 0;

    public async Task<LoginResult> CreateAsync(Guid userId, string device, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var session = new BrowserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
            ExpiresAtUtc = now.AddDays(14),
            Device = device[..Math.Min(256, device.Length)],
        };
        var credential = NewCredential();
        db.BrowserSessions.Add(session);
        db.SessionCredentials.Add(new SessionCredential { Hash = Hash(credential), SessionId = session.Id });
        await db.SaveChangesAsync(ct);
        return Issue(session, credential);
    }

    /// <summary>
    /// A connected app awaiting its code exchange: no credential yet, hidden from every list. Its id comes from the
    /// consent request, so a second approval of the same request hits the primary key and fails.
    /// </summary>
    /// <param name="redirectUri">The redirect the user consented to: what "Returns to" shows from then on.</param>
    /// <param name="revoked">For a denial: the row only uses up the request.</param>
    public async Task CreatePendingAppAsync(Guid userId, OAuthClient client, string[] scopes, string redirectUri, Guid id, CancellationToken ct,
        bool revoked = false)
    {
        var now = time.GetUtcNow();
        var session = new BrowserSession
        {
            Id = id,
            UserId = userId,
            ClientId = client.Id,
            OAuthConfigurationIdentity = oauth.ConfigurationIdentity,
            Scopes = scopes,
            RedirectUri = redirectUri,
            RevokedAtUtc = revoked ? now : null,
            Device = client.Name,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
            ExpiresAtUtc = now.Add(AppLifetime),
        };
        db.BrowserSessions.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" or "23503" })
        {
            db.Entry(session).State = EntityState.Detached;
            throw new OAuthException("invalid_request");
        }
    }

    /// <summary>Rotates a browser credential (<paramref name="clientId"/> null) or an app's <c>ofr_</c> credential.</summary>
    public Task<LoginResult?> RefreshAsync(string credential, CancellationToken ct) => RefreshAsync(credential, null, ct);

    public async Task<LoginResult?> RefreshAsync(string credential, string? clientId, CancellationToken ct)
    {
        if (clientId is null ? credential.Length != 64 : !IsAppCredential(credential)) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hash = Hash(credential);
        // Lock the session as well as its credential so rotation and revocation serialize.
        var row = await db.SessionCredentials.FromSqlInterpolated($"SELECT * FROM \"SessionCredentials\" WHERE \"Hash\" = {hash} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var session = await db.BrowserSessions.FromSqlInterpolated($"SELECT * FROM \"BrowserSessions\" WHERE \"Id\" = {row.SessionId} FOR UPDATE").SingleAsync(ct);
        var now = time.GetUtcNow();
        if (session.ClientId != clientId) return null;
        if (clientId is not null && (!oauth.Enabled || session.OAuthConfigurationIdentity != oauth.ConfigurationIdentity)) return null;
        if (session.RevokedAtUtc is not null || session.ExpiresAtUtc <= now || !await db.Users.AnyAsync(u => u.Id == session.UserId && u.IsEnabled, ct)) return null;
        if (row.UsedAtUtc is not null)
        {
            if (now - row.UsedAtUtc.Value <= TimeSpan.FromSeconds(10) && row.ProtectedSuccessor is not null)
            {
                // Another tab or a lost response: return exactly the same successor, not another rotation.
                var retry = Issue(session, _protector.Unprotect(row.ProtectedSuccessor));
                await transaction.CommitAsync(ct);
                return retry;
            }
            session.RevokedAtUtc = now;
            if (session.ClientId is null) Audit(session.UserId, session.Id, "SessionCredentialReuse");
            else db.ActivityLogEntries.Add(ActivityLogEntry.Create(ConnectedAppEntity, session.Id.ToString(), "ConnectedAppCredentialReuse", session.UserId, ActorType.Operator));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        var next = session.ClientId is null ? NewCredential() : NewAppCredential();
        row.UsedAtUtc = now;
        row.ProtectedSuccessor = _protector.Protect(next);
        db.SessionCredentials.Add(new SessionCredential { Hash = Hash(next), SessionId = session.Id });
        session.LastSeenAtUtc = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Issue(session, next);
    }

    public LoginResult Issue(BrowserSession session, string credential)
    {
        var token = session.ClientId is null
            ? jwt.IssueToken(session.UserId, session.Id)
            : jwt.IssueAppToken(session.UserId, session.Id, session.ClientId,
                oauth.Issuer ?? throw new InvalidOperationException("OAuth is disabled."), oauth.Resource!);
        return new(token.Token, token.ExpiresAtUtc, session.UserId, credential, session.ExpiresAtUtc);
    }

    /// <summary>Browser sessions only: an app's session id never authenticates the REST API or the hub.</summary>
    public async Task<bool> IsActiveAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return await db.BrowserSessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId && s.ClientId == null && s.RevokedAtUtc == null && s.ExpiresAtUtc > now, ct)
            && await db.Users.AnyAsync(u => u.Id == userId && u.IsEnabled, ct);
    }

    /// <summary>Apps only: the grant behind an access token, while the app, its client and its user are all live.</summary>
    public async Task<AppGrant?> ResolveAppAsync(Guid userId, Guid sessionId, string clientId, CancellationToken ct)
    {
        if (!oauth.Enabled) return null;
        var now = time.GetUtcNow();
        var app = await db.BrowserSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId && s.ClientId == clientId && s.OAuthConfigurationIdentity == oauth.ConfigurationIdentity && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .Select(s => new { s.Scopes, s.Device, s.LastSeenAtUtc }).SingleOrDefaultAsync(ct);
        if (app?.Scopes is null || !await db.Users.AnyAsync(u => u.Id == userId && u.IsEnabled, ct)) return null;
        var minuteAgo = now.AddMinutes(-1);
        // At most one write a minute; the condition cannot revive a revoked app.
        if (app.LastSeenAtUtc < minuteAgo)
            await db.BrowserSessions.Where(s => s.Id == sessionId && s.RevokedAtUtc == null && s.LastSeenAtUtc < minuteAgo)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAtUtc, now), ct);
        return new(app.Scopes, app.Device);
    }

    public Task RevokeAsync(Guid userId, Guid? sessionId, Guid actorId, CancellationToken ct) => RevokeAsync(userId, sessionId, actorId, null, false, ct);

    public Task RevokeAsync(Guid userId, Guid? sessionId, Guid actorId, Guid? exceptSessionId, CancellationToken ct) =>
        RevokeAsync(userId, sessionId, actorId, exceptSessionId, false, ct);

    /// <summary>
    /// Revokes one session or app, or all of them except <paramref name="exceptSessionId"/>. With
    /// <paramref name="browsersOnly"/> connected apps stay connected (your own "Revoke all sessions").
    /// </summary>
    public async Task RevokeAsync(Guid userId, Guid? sessionId, Guid actorId, Guid? exceptSessionId, bool browsersOnly, CancellationToken ct)
    {
        var query = db.BrowserSessions.Where(s => s.UserId == userId && s.RevokedAtUtc == null);
        if (sessionId is not null) query = query.Where(s => s.Id == sessionId);
        if (exceptSessionId is not null) query = query.Where(s => s.Id != exceptSessionId);
        if (browsersOnly) query = query.Where(s => s.ClientId == null);
        // Pending apps (never exchanged) were never shown, so they are revoked without an audit row.
        var apps = await query.Where(s => s.ClientId != null && db.SessionCredentials.Any(c => c.SessionId == s.Id)).AsNoTracking().ToListAsync(ct);
        var now = time.GetUtcNow();
        await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
        if (sessionId is null || apps.Count == 0) Audit(actorId, sessionId ?? userId, sessionId is null ? "AllSessionsRevoked" : "SessionRevoked");
        foreach (var app in apps) await AuditAppAsync(actorId, app, "ConnectedAppRevoked", actorId == userId ? "user" : "administrator", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Revokes one app for a protocol reason (<c>replaced</c>, <c>code-reuse</c>, <c>app</c>), credited to its owner.</summary>
    public async Task RevokeAppAsync(Guid sessionId, string reason, CancellationToken ct)
    {
        var app = await db.BrowserSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId && s.ClientId != null && s.RevokedAtUtc == null, ct);
        if (app is null) return;
        var now = time.GetUtcNow();
        if (await db.BrowserSessions.Where(s => s.Id == sessionId && s.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct) == 0) return;
        await AuditAppAsync(app.UserId, app, "ConnectedAppRevoked", reason, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sign out: browser sessions only. A connected app's refresh credential sent as the cookie does nothing.</summary>
    public async Task RevokeCredentialAsync(string credential, CancellationToken ct)
    {
        var row = await db.SessionCredentials.AsNoTracking().SingleOrDefaultAsync(c => c.Hash == Hash(credential), ct);
        if (row is null) return;
        var session = await db.BrowserSessions.FindAsync([row.SessionId], ct);
        if (session is { ClientId: null }) await RevokeAsync(session.UserId, session.Id, session.UserId, ct);
    }

    public const string ConnectedAppEntity = "ConnectedApp";

    /// <summary>Non-secret app metadata only: never a credential, code or token.</summary>
    public async Task AuditAppAsync(Guid actorId, BrowserSession app, string eventType, string? reason, CancellationToken ct)
    {
        var redirect = app.RedirectUri
            ?? await db.OAuthClients.AsNoTracking().Where(c => c.Id == app.ClientId).Select(c => c.RedirectUris[0]).SingleOrDefaultAsync(ct);
        var state = System.Text.Json.JsonSerializer.Serialize(new
        {
            app.ClientId,
            ClientName = app.Device,
            ReturnsTo = redirect is null ? null : OAuthRules.ReturnsTo(redirect).Label,
            app.Scopes,
            app.ExpiresAtUtc,
            Reason = reason,
        });
        db.ActivityLogEntries.Add(ActivityLogEntry.Create(ConnectedAppEntity, app.Id.ToString(), eventType, actorId, ActorType.Operator,
            new ActivityLogDetails(ReportedState: state)));
    }

    private void Audit(Guid actorId, Guid id, string action) => db.ActivityLogEntries.Add(
        ActivityLogEntry.Create("BrowserSession", id.ToString(), action, actorId, ActorType.Operator));
}
