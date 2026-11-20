using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

/// <param name="ClaimNames">The names, never the values, of the claims in the ID token: it lets the log say what a provider sent.</param>
/// <param name="GroupsClaimSent">The ID token has the Groups claim, even as an empty list. Parsed claims cannot tell an empty list
/// from a missing claim (an empty JSON array yields no claim), and the two have different fixes.</param>
public sealed record ExternalProfile(string? Username, string? Email, string? DisplayName, bool EmailVerified, IReadOnlyList<string>? Groups = null,
    IReadOnlyList<string>? ClaimNames = null, bool GroupsClaimSent = false);
public sealed class ProvisioningConflictException(string message) : ArgumentException(message);

public sealed class ExternalProvisioningService(OpenFarmDbContext db, UserManager<ApplicationUser> users, PermissionService permissions,
    OidcSettings oidc, ProvisioningSettings settings, TimeProvider time, ILogger<ExternalProvisioningService> logger)
{
    /// <summary>
    /// The default group goes to every sign-in, so it must not be a protected group. A mapped group may be any group:
    /// the provider then grants it only to users it lists, and an administrator accepted that in Settings → Authentication.
    /// </summary>
    public static bool CanBeDefaultGroup(ApplicationRole role) => !role.IsProtected;

    private ApplicationUser? Reject(string reason)
    {
        OidcSignInLog.Rejected(logger, reason);
        return null;
    }

    // Shared with account administration, so Identity's username/email uniqueness checks cannot race.
    public Task<int> LockAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71426499)", ct);

    public async Task ValidateAsync(CancellationToken ct)
    {
        if (!settings.JitEnabled) return;
        _ = await FindDefaultRoleAsync(ct);
        foreach (var mapping in settings.GroupMappings) _ = await FindMappedRoleAsync(mapping.Group, ct);
    }

    private async Task<ApplicationRole> FindDefaultRoleAsync(CancellationToken ct)
    {
        var role = await FindRoleAsync(settings.DefaultGroup, ct);
        if (role is null || !CanBeDefaultGroup(role)) throw new ArgumentException("The default group must be an existing, unprotected group.");
        return role;
    }

    private async Task<ApplicationRole> FindMappedRoleAsync(string name, CancellationToken ct) =>
        await FindRoleAsync(name, ct) ?? throw new ArgumentException("Each mapped group must be an existing group.");

    private Task<ApplicationRole?> FindRoleAsync(string name, CancellationToken ct)
    {
        var normalized = name.ToUpperInvariant();
        return db.Roles.SingleOrDefaultAsync(r => r.NormalizedName == normalized, ct);
    }

    /// <summary>Caller owns the transaction. A disabled account is never re-enabled.</summary>
    public async Task<ApplicationUser?> ResolveJitAsync(string subject, ExternalProfile profile, CancellationToken ct)
    {
        await LockAsync(ct);
        var linked = await users.FindByLoginAsync(oidc.ProviderKey, subject);
        if (linked is not null) return linked;
        if (!settings.JitEnabled) return Reject("not_linked_and_jit_disabled");
        if (settings.RequireVerifiedEmail && !profile.EmailVerified) return Reject("email_unverified");
        var username = profile.Username?.Trim();
        if (string.IsNullOrEmpty(username)) return Reject("username_claim_missing");
        if (string.IsNullOrWhiteSpace(profile.Email) || !System.Net.Mail.MailAddress.TryCreate(profile.Email, out _))
            return Reject("email_claim_missing_or_invalid");
        if (username.Length > 256 || profile.Email.Length > 256 || profile.DisplayName?.Length > 256) return Reject("claim_too_long");
        return await CreateAsync(subject, username, profile.Email.Trim(), profile.DisplayName, ct);
    }

    /// <summary>
    /// Re-applies the group mapping at every sign-in. The sync owns the groups the mapping and the default group name now,
    /// plus every group it granted at the previous sign-in, so removing or retargeting a mapping revokes what it granted.
    /// Groups assigned by hand are never touched. No match (or no claim) yields the default group. Returns false, with
    /// nothing to commit, when the change would remove the last enabled administrator. Caller owns the transaction and the
    /// administration lock, and commits only when this returns true.
    /// </summary>
    public async Task<bool> SyncGroupsAsync(ApplicationUser user, ExternalProfile profile, CancellationToken ct)
    {
        var mappings = settings.GroupMappings;
        var claimed = profile.Groups;
        if (!settings.JitEnabled || user.ProvisioningProvider != oidc.ProviderKey
            || (mappings.Length == 0 && user.ProvisionedGroupIds.Length == 0)) return true;
        if (mappings.Length > 0 && claimed is not { Count: > 0 })
        {
            if (profile.GroupsClaimSent) OidcSignInLog.GroupsClaimEmpty(logger, settings.GroupsClaim);
            else OidcSignInLog.GroupsClaimMissing(logger, settings.GroupsClaim, profile.ClaimNames, oidc.Scopes);
        }
        var defaultId = (await FindDefaultRoleAsync(ct)).Id;
        var mapped = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in mappings.Select(m => m.Group).Distinct(StringComparer.OrdinalIgnoreCase))
            mapped[name] = (await FindMappedRoleAsync(name, ct)).Id; // refuses a group that was removed
        var granted = mappings.Where(m => claimed?.Contains(m.ExternalGroup, StringComparer.Ordinal) == true)
            .Select(m => mapped[m.Group]).Distinct().ToArray();
        Guid[] desired = granted.Length > 0 ? granted : [defaultId];
        // With no mapping left, only what an earlier sign-in granted is revoked: groups are managed locally again.
        var owned = user.ProvisionedGroupIds.ToHashSet();
        if (mappings.Length > 0) owned.UnionWith(mapped.Values.Append(defaultId));
        var provisioned = mappings.Length > 0 ? desired.Order().ToArray() : [];
        var memberships = await db.UserRoles.Where(m => m.UserId == user.Id).ToListAsync(ct);
        var add = desired.Except(memberships.Select(m => m.RoleId)).ToArray();
        var remove = memberships.Where(m => owned.Contains(m.RoleId) && !desired.Contains(m.RoleId)).ToList();
        var provisionedChanged = !provisioned.SequenceEqual(user.ProvisionedGroupIds);
        if (add.Length == 0 && remove.Count == 0 && !provisionedChanged) return true;
        user.ProvisionedGroupIds = provisioned;
        db.UserRoles.RemoveRange(remove);
        db.UserRoles.AddRange(add.Select(id => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = id }));
        if (add.Length > 0 || remove.Count > 0)
        {
            var removed = remove.Select(m => m.RoleId).ToArray();
            var names = await db.Roles.Where(r => add.Contains(r.Id) || removed.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name!, ct);
            db.ActivityLogEntries.Add(ActivityLogEntry.Create("User", user.Id.ToString(), "OidcGroupsSynced", null, ActorType.System,
                new ActivityLogDetails(ReportedState: System.Text.Json.JsonSerializer.Serialize(new
                {
                    Added = add.Select(id => names[id]).Order().ToArray(),
                    Removed = removed.Select(id => names[id]).Order().ToArray(),
                }))));
        }
        await db.SaveChangesAsync(ct);
        // Same rule as account administration: write, then require an administrator to remain. The caller rolls back on false.
        if (remove.Count > 0 && !await permissions.HasEnabledAdministratorAsync(ct))
        {
            OidcSignInLog.Rejected(logger, "last_administrator_would_be_removed");
            return false;
        }
        return true;
    }

    private async Task<ApplicationUser> CreateAsync(string subject, string username, string email, string? displayName, CancellationToken ct)
    {
        if (await db.UserLogins.AnyAsync(l => l.LoginProvider == oidc.ProviderKey && l.ProviderKey == subject, ct)
            || await db.Users.AnyAsync(u => u.ProvisioningProvider == oidc.ProviderKey && u.ProvisioningExternalId == subject, ct))
            throw new ProvisioningConflictException("This external identity already belongs to an account.");
        var role = await FindDefaultRoleAsync(ct);
        var user = new ApplicationUser
        {
            UserName = username,
            Email = email,
            DisplayName = displayName,
            IsEnabled = true,
            ProvisioningProvider = oidc.ProviderKey,
            ProvisioningExternalId = subject,
            CreatedAtUtc = time.GetUtcNow(),
        };
        Check(await users.CreateAsync(user)); // No password or local login credential is created.
        Check(await users.AddLoginAsync(user, new UserLoginInfo(oidc.ProviderKey, subject, oidc.DisplayName)));
        Check(await users.AddToRoleAsync(user, role.Name!));
        db.ActivityLogEntries.Add(ActivityLogEntry.Create("User", user.Id.ToString(), "JitAccountCreated", null, ActorType.System));
        await db.SaveChangesAsync(ct);
        return user;
    }

    public static void Check(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName" or "LoginAlreadyAssociated"))
            throw new ProvisioningConflictException("Username, email or identity is already in use.");
        if (!result.Succeeded) throw new ArgumentException("Username, email or identity is invalid or already in use.");
    }
}
