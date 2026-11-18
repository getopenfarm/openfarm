using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record OidcLinkTicket(Guid UserId, Guid SessionId, string Nonce, string? SecurityStamp, DateTimeOffset ExpiresAtUtc);

/// <summary>Explicit linking and opt-in JIT creation. Existing identities are resolved only by issuer and subject.</summary>
public sealed class OidcAccountService(OpenFarmDbContext db, UserManager<ApplicationUser> users,
    BrowserSessionService sessions, OidcSettings settings, IDataProtectionProvider protection, TimeProvider time,
    ExternalProvisioningService provisioning, AuthenticationConfiguration configuration, ServerConfigurationOverrides serverPolicy)
{
    private const string LinkTokenName = "PendingLink";
    private readonly IDataProtector _protector = protection.CreateProtector("OpenFarm.OidcLink.v1");

    private readonly IDataProtector _testProtector = protection.CreateProtector("OpenFarm.OidcTest.v1");

    public async Task<string?> BeginTestAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await provisioning.LockAsync(ct);
        if (!settings.Enabled || !await configuration.LockCurrentAsync(ct)) return null;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !await users.IsInRoleAsync(user, PermissionCatalog.Administrators)
            || !await sessions.IsActiveAsync(userId, sessionId, ct)) return null;
        var ticket = new OidcLinkTicket(userId, sessionId, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            user.SecurityStamp, time.GetUtcNow().AddMinutes(5));
        Check(await users.SetAuthenticationTokenAsync(user, settings.ProviderKey, "PendingTest", BrowserSessionService.Hash(ticket.Nonce)));
        await tx.CommitAsync(ct);
        return _testProtector.Protect(JsonSerializer.Serialize(ticket));
    }

    public async Task<bool> CompleteTestAsync(string value, string credential, string subject, string? challengedOrigin, CancellationToken ct)
    {
        OidcLinkTicket? ticket;
        try { ticket = JsonSerializer.Deserialize<OidcLinkTicket>(_testProtector.Unprotect(value)); }
        catch (Exception e) when (e is CryptographicException or JsonException) { return false; }
        if (ticket is null || ticket.ExpiresAtUtc <= time.GetUtcNow()) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await provisioning.LockAsync(ct);
        if (!await configuration.LockCurrentAsync(ct)) return false;
        await LockUserAsync(ticket.UserId, ct);
        await LockSessionAsync(ticket.SessionId, ct);
        var user = await users.FindByIdAsync(ticket.UserId.ToString());
        if (user is null || user.SecurityStamp != ticket.SecurityStamp || !user.IsEnabled
            || !await users.IsInRoleAsync(user, PermissionCatalog.Administrators)
            || !await MatchesBrowserAsync(ticket.UserId, ticket.SessionId, credential, ct)) return false;
        var hash = BrowserSessionService.Hash(ticket.Nonce);
        if (await db.UserTokens.Where(t => t.UserId == user.Id && t.LoginProvider == settings.ProviderKey
            && t.Name == "PendingTest" && t.Value == hash).ExecuteDeleteAsync(ct) != 1) return false;
        // A provider test must prove access to the administrator's explicitly linked identity.
        if (!await db.UserLogins.AnyAsync(l => l.UserId == user.Id && l.LoginProvider == settings.ProviderKey && l.ProviderKey == subject, ct))
        { await tx.CommitAsync(ct); return false; }
        var origin = serverPolicy.Resolve(await db.ServerSettings.AsNoTracking().SingleAsync(ct)).PublicUrl;
        if (origin != challengedOrigin) return false;
        var saved = await db.AuthenticationSettings.SingleAsync(ct);
        saved.TestedProviderIdentity = settings.TestIdentity(origin);
        saved.TestedAtUtc = time.GetUtcNow();
        Audit(user.Id, "OidcProviderTestPassed");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<string?> BeginLinkAsync(Guid userId, Guid sessionId, string password, string credential, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await provisioning.LockAsync(ct);
        if (!await configuration.LockCurrentAsync(ct)) return null;
        await LockUserAsync(userId, ct);
        await LockSessionAsync(sessionId, ct);
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsEnabled || await users.IsLockedOutAsync(user)
            || !await MatchesBrowserAsync(userId, sessionId, credential, ct)) return null;
        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            await transaction.CommitAsync(ct);
            return null;
        }
        await users.ResetAccessFailedCountAsync(user);
        if (await db.UserLogins.AnyAsync(l => l.UserId == userId && l.LoginProvider == settings.ProviderKey, ct))
            throw new ArgumentException("This account is already linked to the sign-in provider.");
        var ticket = new OidcLinkTicket(userId, sessionId, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            user.SecurityStamp, time.GetUtcNow().AddMinutes(5));
        Check(await users.SetAuthenticationTokenAsync(user, settings.ProviderKey, LinkTokenName, BrowserSessionService.Hash(ticket.Nonce)));
        await transaction.CommitAsync(ct);
        return _protector.Protect(JsonSerializer.Serialize(ticket));
    }

    public OidcLinkTicket? ReadTicket(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 4096) return null;
        try
        {
            var ticket = JsonSerializer.Deserialize<OidcLinkTicket>(_protector.Unprotect(value));
            return ticket?.ExpiresAtUtc > time.GetUtcNow() ? ticket : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException) { return null; }
    }

    public async Task<bool> MatchesBrowserAsync(Guid userId, Guid sessionId, string credential, CancellationToken ct)
    {
        if (credential.Length != 64 || !await sessions.IsActiveAsync(userId, sessionId, ct)) return false;
        var hash = BrowserSessionService.Hash(credential);
        return await db.SessionCredentials.AnyAsync(c => c.Hash == hash && c.SessionId == sessionId, ct);
    }

    public async Task CompleteLinkAsync(string protectedTicket, string credential, string subject, CancellationToken ct)
    {
        var ticket = ReadTicket(protectedTicket) ?? throw new ArgumentException("The linking request expired. Start again from My profile.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await provisioning.LockAsync(ct);
        if (!await configuration.LockCurrentAsync(ct)) throw new ArgumentException("Single sign-on settings changed. Start linking again.");
        await LockUserAsync(ticket.UserId, ct);
        await LockSessionAsync(ticket.SessionId, ct);
        var user = await users.FindByIdAsync(ticket.UserId.ToString());
        if (user is null || !user.IsEnabled || user.SecurityStamp != ticket.SecurityStamp
            || !await MatchesBrowserAsync(ticket.UserId, ticket.SessionId, credential, ct))
            throw new ArgumentException("The original browser session is no longer active.");
        var hash = BrowserSessionService.Hash(ticket.Nonce);
        var consumed = await db.UserTokens.Where(t => t.UserId == user.Id && t.LoginProvider == settings.ProviderKey
            && t.Name == LinkTokenName && t.Value == hash).ExecuteDeleteAsync(ct);
        if (consumed != 1) throw new ArgumentException("The linking request has already been used or replaced.");
        if (await db.UserLogins.AnyAsync(l => l.UserId == user.Id && l.LoginProvider == settings.ProviderKey, ct))
            throw new ArgumentException("This account is already linked to the sign-in provider.");
        Check(await users.AddLoginAsync(user, new UserLoginInfo(settings.ProviderKey, subject, settings.DisplayName)));
        Audit(user.Id, "OidcIdentityLinked");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<LoginResult?> SignInAsync(string subject, string device, CancellationToken ct, ExternalProfile? profile = null,
        string? providerSessionId = null, DateTimeOffset? authorizationStartedAt = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await provisioning.LockAsync(ct);
        if (!settings.Enabled || !await configuration.LockCurrentAsync(ct)) return null;
        var user = profile is null ? await users.FindByLoginAsync(settings.ProviderKey, subject)
            : await provisioning.ResolveJitAsync(subject, profile, ct);
        if (user is null) return null;
        await LockUserAsync(user.Id, ct);
        await db.Entry(user).ReloadAsync(ct);
        if (db.Entry(user).State == EntityState.Detached || !user.IsEnabled) return null;
        var now = time.GetUtcNow();
        var authorizationTime = authorizationStartedAt ?? now;
        if (await db.OidcLogoutReceipts.AnyAsync(r => r.Provider == settings.SessionIdentity && r.ExpiresAtUtc > now
            && ((providerSessionId != null && r.SessionId == providerSessionId && (r.Subject == null || r.Subject == subject))
                || (r.SessionId == null && r.Subject == subject && r.ReceivedAtUtc >= authorizationTime)), ct)) return null;
        var result = await sessions.CreateAsync(user.Id, device, ct, external: true, oidcSubject: subject, oidcSessionId: providerSessionId);
        Audit(user.Id, "OidcSignedIn");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private Task<int> LockUserAsync(Guid userId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE", ct);

    private Task<int> LockSessionAsync(Guid sessionId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"BrowserSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE", ct);

    private void Audit(Guid userId, string action) => db.ActivityLogEntries.Add(
        ActivityLogEntry.Create("User", userId.ToString(), action, userId, ActorType.Operator));

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new ArgumentException("The identity could not be linked. It may already belong to another account.");
    }
}
