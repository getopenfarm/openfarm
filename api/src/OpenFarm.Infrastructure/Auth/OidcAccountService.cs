using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record OidcLinkTicket(Guid UserId, Guid SessionId, string Nonce, string? SecurityStamp, DateTimeOffset ExpiresAtUtc);

/// <summary>Explicit linking and opt-in JIT creation. Existing identities are resolved only by issuer and subject.</summary>
public sealed class OidcAccountService(OpenFarmDbContext db, UserManager<ApplicationUser> users,
    BrowserSessionService sessions, OidcSettings settings, IDataProtectionProvider protection, TimeProvider time,
    ExternalProvisioningService provisioning, AuthenticationConfiguration configuration,
    ILogger<OidcAccountService> logger)
{
    private const string LinkTokenName = "PendingLink";
    private readonly IDataProtector _protector = protection.CreateProtector("OpenFarm.OidcLink.v1");

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
        var ticket = ReadTicket(protectedTicket) ?? throw new OidcLinkException(OidcLinkFailure.Expired, "The linking request expired. Start again from My profile.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await provisioning.LockAsync(ct);
        if (!await configuration.LockCurrentAsync(ct)) throw new OidcLinkException(OidcLinkFailure.ConfigurationChanged, "Single sign-on settings changed. Start linking again.");
        await LockUserAsync(ticket.UserId, ct);
        await LockSessionAsync(ticket.SessionId, ct);
        var user = await users.FindByIdAsync(ticket.UserId.ToString());
        if (user is null || !user.IsEnabled || user.SecurityStamp != ticket.SecurityStamp
            || !await MatchesBrowserAsync(ticket.UserId, ticket.SessionId, credential, ct))
            throw new OidcLinkException(OidcLinkFailure.SessionChanged, "The original browser session is no longer active.");
        var hash = BrowserSessionService.Hash(ticket.Nonce);
        var consumed = await db.UserTokens.Where(t => t.UserId == user.Id && t.LoginProvider == settings.ProviderKey
            && t.Name == LinkTokenName && t.Value == hash).ExecuteDeleteAsync(ct);
        if (consumed != 1) throw new OidcLinkException(OidcLinkFailure.RequestReplaced, "The linking request has already been used or replaced.");
        if (await db.UserLogins.AnyAsync(l => l.UserId == user.Id && l.LoginProvider == settings.ProviderKey, ct))
            throw new OidcLinkException(OidcLinkFailure.AlreadyLinked, "This account is already linked to the sign-in provider.");
        var result = await users.AddLoginAsync(user, new UserLoginInfo(settings.ProviderKey, subject, settings.DisplayName));
        if (!result.Succeeded)
            throw new OidcLinkException(result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.LoginAlreadyAssociated))
                ? OidcLinkFailure.IdentityConflict : OidcLinkFailure.Failed, "The provider identity could not be linked.");
        Audit(user.Id, "OidcIdentityLinked");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<LoginResult?> SignInAsync(string subject, string device, CancellationToken ct, ExternalProfile? profile = null,
        string? providerSessionId = null, DateTimeOffset? authorizationStartedAt = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await provisioning.LockAsync(ct);
        if (!settings.Enabled || !await configuration.LockCurrentAsync(ct)) return Reject("provider_disabled_or_settings_changed");
        var user = profile is null ? await users.FindByLoginAsync(settings.ProviderKey, subject)
            : await provisioning.ResolveJitAsync(subject, profile, ct); // logs its own refusal reason
        if (user is null) return null;
        await LockUserAsync(user.Id, ct);
        await db.Entry(user).ReloadAsync(ct);
        if (db.Entry(user).State == EntityState.Detached || !user.IsEnabled) return Reject("account_disabled_or_removed");
        if (profile is not null && !await provisioning.SyncGroupsAsync(user, profile, ct)) return null; // logs its own refusal reason; nothing is committed
        var now = time.GetUtcNow();
        var authorizationTime = authorizationStartedAt ?? now;
        if (await db.OidcLogoutReceipts.AnyAsync(r => r.Provider == settings.SessionIdentity && r.ExpiresAtUtc > now
            && ((providerSessionId != null && r.SessionId == providerSessionId && (r.Subject == null || r.Subject == subject))
                || (r.SessionId == null && r.Subject == subject && r.ReceivedAtUtc >= authorizationTime)), ct)) return Reject("provider_logout_receipt");
        var result = await sessions.CreateAsync(user.Id, device, ct, external: true, oidcSubject: subject, oidcSessionId: providerSessionId);
        Audit(user.Id, "OidcSignedIn");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private LoginResult? Reject(string reason)
    {
        OidcSignInLog.Rejected(logger, reason);
        return null;
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
