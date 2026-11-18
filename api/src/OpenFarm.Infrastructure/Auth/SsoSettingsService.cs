using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record SsoRecoveryAccount(Guid Id, string Username);
public sealed record SsoGroupMapping(string ExternalGroup, string LocalGroup);
public sealed record SsoSettingsResponse(Guid Revision, bool Enabled, bool Required, string DisplayName, string? Authority,
    string? ClientId, bool ClientCredentialConfigured, int SessionHours, Guid[] RecoveryUserIds, bool JitEnabled,
    bool RequireVerifiedEmail, bool ScimEnabled, bool ScimCredentialConfigured, int ScimRequestsPerMinute,
    string DefaultGroup, SsoGroupMapping[] GroupMappings, SsoRecoveryAccount[] RecoveryAccounts,
    GroupSummary[] Groups, string? CallbackUrl, string? LogoutUrl, string? ScimUrl, bool RecoveryMode);
public sealed record UpdateSsoSettingsRequest(Guid Revision, SsoConfigurationValues Settings);

public sealed class SsoSettingsService(OpenFarmDbContext db, AuthenticationConfiguration configuration,
    ServerConfiguration server, ServerConfigurationOverrides policy, TimeProvider time)
{
    public async Task<SsoSettingsResponse> ReadAsync(CancellationToken ct)
    {
        var saved = await db.SsoConfigurations.AsNoTracking().SingleAsync(ct);
        return await ResponseAsync(saved.Revision, configuration.Unprotect(saved), ct);
    }

    public async Task<SsoSettingsResponse> UpdateAsync(Guid actor, Guid sessionId, UpdateSsoSettingsRequest input, CancellationToken ct)
    {
        if (input.Settings is null) throw new ArgumentException("Single sign-on settings are required.");
        if (configuration.RecoveryMode) throw new ArgumentException("Turn off deployment recovery mode before changing single sign-on settings.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Same administration lock as user/group changes, followed by the configuration row.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71426499)", ct);
        var saved = await db.SsoConfigurations.FromSqlInterpolated(
            $"SELECT * FROM \"SsoConfigurations\" WHERE \"Id\" = {SsoConfiguration.SingletonId} FOR UPDATE").SingleAsync(ct);
        if (saved.Revision != input.Revision) throw new ServerSettingsConflictException();
        var previous = configuration.Unprotect(saved);
        var value = input.Settings;
        if (value.GroupMappings is null || value.RecoveryUserIds is null || value.GroupMappings.Count > 100
            || value.DisplayName is null || value.DefaultGroup is null || value.Authority?.Length > 2048
            || value.ClientId?.Length > 256 || value.ClientSecret?.Length > 4096
            || value.ScimRequestsPerMinute is < 1 or > 10000)
            throw new ArgumentException("Enter valid single sign-on settings.");
        if ((previous.Authority != value.Authority || previous.ClientId != value.ClientId)
            && previous.ClientSecret is not null && string.IsNullOrWhiteSpace(value.ClientSecret))
            throw new ArgumentException("Enter the provider's client credential when changing its issuer or client ID.");
        try { value = value with { GroupMappings = new(value.GroupMappings, StringComparer.OrdinalIgnoreCase) }; }
        catch (ArgumentException) { throw new ArgumentException("External group names must be unique."); }
        value = value with { ClientSecret = value.ClientSecret ?? previous.ClientSecret, ScimToken = value.ScimToken ?? previous.ScimToken };
        OidcSettings oidc;
        try
        {
            oidc = new(value.Configuration());
            _ = new ProvisioningSettings(value.Configuration(), oidc, value.GroupMappings);
            oidc.ValidatePublicUrl(policy.Resolve(await db.ServerSettings.AsNoTracking().SingleAsync(ct)).PublicUrl);
        }
        catch (InvalidOperationException) { throw new ArgumentException("Check the issuer URL, client credentials, session duration, recovery accounts, and provisioning settings. Enabled SSO needs an HTTPS public URL and issuer; HTTP is allowed only on localhost."); }
        var recovery = await RecoveryAccountsAsync(ct);
        if (value.RecoveryUserIds.Any(id => !recovery.Any(a => a.Id == id)))
            throw new ArgumentException("Recovery accounts must be enabled local administrators with a password.");
        if (value.Required && !value.RecoveryUserIds.Contains(actor)
            && !await db.BrowserSessions.AnyAsync(s => s.Id == sessionId && s.UserId == actor
                && s.OidcConfigurationIdentity == oidc.SessionIdentity && s.RevokedAtUtc == null, ct))
            throw new ArgumentException("Sign in through this provider or select your current administrator as a recovery account before requiring SSO.");
        if (value.JitEnabled || value.ScimEnabled)
        {
            var allowedGroups = await db.Roles.Where(r => !r.IsProtected && r.Name != PermissionCatalog.Administrators)
                .Select(r => r.Name!).ToArrayAsync(ct);
            if (value.GroupMappings.Values.Append(value.DefaultGroup).Any(name => !allowedGroups.Contains(name, StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("Provisioning groups must be existing, unprotected OpenFarm groups.");
        }
        if (JsonSerializer.Serialize(previous) != JsonSerializer.Serialize(value))
        {
            var providerChanged = previous.Enabled != value.Enabled || previous.Authority != value.Authority
                || previous.ClientId != value.ClientId || previous.ClientSecret != value.ClientSecret || previous.SessionHours != value.SessionHours;
            var now = time.GetUtcNow();
            await db.BrowserSessions.Where(s => s.ClientId == null && s.RevokedAtUtc == null
                && ((providerChanged && s.OidcConfigurationIdentity != null)
                    || (value.Required && s.OidcConfigurationIdentity == null && !value.RecoveryUserIds.Contains(s.UserId))))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
            // Pending linking proofs must not survive a policy change or provider switch.
            await db.UserTokens.Where(t => t.Name == "PendingLink").ExecuteDeleteAsync(ct);
            saved.ProtectedConfiguration = configuration.Protect(value);
            saved.Revision = Guid.NewGuid();
            // Deliberately exclude credentials and the encrypted payload from audit details.
            db.ActivityLogEntries.Add(ActivityLogEntry.Create("SsoConfiguration", saved.Id.ToString(), "SsoSettingsChanged", actor,
                ActorType.Operator, new ActivityLogDetails(ReportedState: JsonSerializer.Serialize(new
                {
                    value.Enabled,
                    value.Required,
                    value.Authority,
                    value.ClientId,
                    value.JitEnabled,
                    value.ScimEnabled,
                    ClientCredentialChanged = previous.ClientSecret != value.ClientSecret,
                    ScimCredentialChanged = previous.ScimToken != value.ScimToken
                }))));
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await ResponseAsync(saved.Revision, value, ct);
    }

    private async Task<SsoRecoveryAccount[]> RecoveryAccountsAsync(CancellationToken ct) =>
        await (from u in db.Users.AsNoTracking()
               join membership in db.UserRoles on u.Id equals membership.UserId
               join role in db.Roles on membership.RoleId equals role.Id
               where u.IsEnabled && u.PasswordHash != null && u.ProvisioningProvider == null && role.Name == PermissionCatalog.Administrators
               orderby u.UserName
               select new SsoRecoveryAccount(u.Id, u.UserName!)).ToArrayAsync(ct);

    private async Task<SsoSettingsResponse> ResponseAsync(Guid revision, SsoConfigurationValues value, CancellationToken ct)
    {
        var origin = server.Current.PublicUrl;
        return new(revision, value.Enabled, value.Required, value.DisplayName, value.Authority, value.ClientId,
            !string.IsNullOrEmpty(value.ClientSecret), value.SessionHours, value.RecoveryUserIds, value.JitEnabled,
            value.RequireVerifiedEmail, value.ScimEnabled, !string.IsNullOrEmpty(value.ScimToken), value.ScimRequestsPerMinute,
            value.DefaultGroup, value.GroupMappings.Select(m => new SsoGroupMapping(m.Key, m.Value)).ToArray(),
            await RecoveryAccountsAsync(ct), await db.Roles.Where(r => !r.IsProtected && r.Name != PermissionCatalog.Administrators)
                .OrderBy(r => r.Name).Select(r => new GroupSummary(r.Id, r.Name!)).ToArrayAsync(ct),
            origin is null ? null : origin + "/api/v1/auth/sso/callback",
            origin is null ? null : origin + "/api/v1/auth/sso/backchannel-logout",
            origin is null ? null : origin + "/scim/v2", configuration.RecoveryMode);
    }
}
