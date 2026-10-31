using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed class BrowserSessionService(OpenFarmDbContext db, JwtTokenService jwt, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("OpenFarm.RefreshRetry.v1");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string NewCredential() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public async Task<LoginResult> CreateAsync(Guid userId, string device, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
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

    public async Task<LoginResult?> RefreshAsync(string credential, CancellationToken ct)
    {
        if (credential.Length != 64) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hash = Hash(credential);
        // Lock the session as well as its credential so rotation and revocation serialize.
        var row = await db.SessionCredentials.FromSqlInterpolated($"SELECT * FROM \"SessionCredentials\" WHERE \"Hash\" = {hash} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var session = await db.BrowserSessions.FromSqlInterpolated($"SELECT * FROM \"BrowserSessions\" WHERE \"Id\" = {row.SessionId} FOR UPDATE").SingleAsync(ct);
        var now = DateTimeOffset.UtcNow;
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
            Audit(session.UserId, session.Id, "SessionCredentialReuse");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        var next = NewCredential();
        row.UsedAtUtc = now;
        row.ProtectedSuccessor = _protector.Protect(next);
        db.SessionCredentials.Add(new SessionCredential { Hash = Hash(next), SessionId = session.Id });
        session.LastSeenAtUtc = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Issue(session, next);
    }

    private LoginResult Issue(BrowserSession session, string credential)
    {
        var token = jwt.IssueToken(session.UserId, session.Id);
        return new(token.Token, token.ExpiresAtUtc, session.UserId, credential, session.ExpiresAtUtc);
    }

    public async Task<bool> IsActiveAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        await db.BrowserSessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > DateTimeOffset.UtcNow, ct)
        && await db.Users.AnyAsync(u => u.Id == userId && u.IsEnabled, ct);

    public Task RevokeAsync(Guid userId, Guid? sessionId, Guid actorId, CancellationToken ct) => RevokeAsync(userId, sessionId, actorId, null, ct);

    /// <summary>Revokes one session, or all of them except <paramref name="exceptSessionId"/>.</summary>
    public async Task RevokeAsync(Guid userId, Guid? sessionId, Guid actorId, Guid? exceptSessionId, CancellationToken ct)
    {
        var query = db.BrowserSessions.Where(s => s.UserId == userId && s.RevokedAtUtc == null);
        if (sessionId is not null) query = query.Where(s => s.Id == sessionId);
        if (exceptSessionId is not null) query = query.Where(s => s.Id != exceptSessionId);
        await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, DateTimeOffset.UtcNow), ct);
        Audit(actorId, sessionId ?? userId, sessionId is null ? "AllSessionsRevoked" : "SessionRevoked");
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeCredentialAsync(string credential, CancellationToken ct)
    {
        var row = await db.SessionCredentials.AsNoTracking().SingleOrDefaultAsync(c => c.Hash == Hash(credential), ct);
        if (row is null) return;
        var session = await db.BrowserSessions.FindAsync([row.SessionId], ct);
        if (session is not null) await RevokeAsync(session.UserId, session.Id, session.UserId, ct);
    }

    private void Audit(Guid actorId, Guid id, string action) => db.ActivityLogEntries.Add(
        ActivityLogEntry.Create("BrowserSession", id.ToString(), action, actorId, ActorType.Operator));
}
