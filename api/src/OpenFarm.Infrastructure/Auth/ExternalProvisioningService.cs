using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record ExternalProfile(string? Username, string? Email, string? DisplayName, bool EmailVerified);
public sealed class ProvisioningConflictException(string message) : ArgumentException(message);

public sealed class ExternalProvisioningService(OpenFarmDbContext db, UserManager<ApplicationUser> users,
    OidcSettings oidc, ProvisioningSettings settings, TimeProvider time)
{
    // Shared with account administration, so Identity's username/email uniqueness checks cannot race.
    public Task<int> LockAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71426499)", ct);

    public async Task ValidateAsync(CancellationToken ct)
    {
        if (!settings.JitEnabled && !settings.ScimEnabled) return;
        foreach (var name in settings.GroupMappings.Values.Append(settings.DefaultGroup).Distinct(StringComparer.OrdinalIgnoreCase))
            _ = await FindAllowedRoleAsync(name, ct);
    }

    public async Task<ApplicationRole> FindAllowedRoleAsync(string name, CancellationToken ct)
    {
        var normalized = name.ToUpperInvariant();
        var role = await db.Roles.SingleOrDefaultAsync(r => r.NormalizedName == normalized, ct);
        if (role is null || role.IsProtected || string.Equals(role.Name, PermissionCatalog.Administrators, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Provisioning can use only existing, unprotected OpenFarm groups.");
        return role;
    }

    /// <summary>Caller owns the transaction. A disabled or deleted subject is never recreated.</summary>
    public async Task<ApplicationUser?> ResolveJitAsync(string subject, ExternalProfile profile, CancellationToken ct)
    {
        await LockAsync(ct);
        var linked = await users.FindByLoginAsync(oidc.ProviderKey, subject);
        if (linked is not null) return linked;
        if (!settings.JitEnabled || (settings.RequireVerifiedEmail && !profile.EmailVerified)) return null;
        var username = profile.Username?.Trim();
        if (string.IsNullOrEmpty(username) || username.Length > 256 || string.IsNullOrWhiteSpace(profile.Email)
            || profile.Email.Length > 256 || !System.Net.Mail.MailAddress.TryCreate(profile.Email, out _)
            || profile.DisplayName?.Length > 256) return null;
        return await CreateAsync(subject, username, profile.Email.Trim(), profile.DisplayName, true, "JitAccountCreated", ct);
    }

    public async Task<ApplicationUser> CreateAsync(string subject, string username, string email, string? displayName,
        bool active, string action, CancellationToken ct)
    {
        if (await db.UserLogins.AnyAsync(l => l.LoginProvider == oidc.ProviderKey && l.ProviderKey == subject, ct)
            || await db.Users.AnyAsync(u => u.ProvisioningProvider == oidc.ProviderKey && u.ProvisioningExternalId == subject, ct))
            throw new ProvisioningConflictException("This external identity already belongs to an account.");
        var role = await FindAllowedRoleAsync(settings.DefaultGroup, ct);
        var user = new ApplicationUser
        {
            UserName = username,
            Email = email,
            DisplayName = displayName,
            IsEnabled = active,
            ProvisioningProvider = oidc.ProviderKey,
            ProvisioningExternalId = subject,
            ProvisioningModifiedAtUtc = time.GetUtcNow(),
            ProvisioningRevision = Guid.NewGuid(),
            CreatedAtUtc = time.GetUtcNow(),
        };
        Check(await users.CreateAsync(user)); // No password or local login credential is created.
        Check(await users.AddLoginAsync(user, new UserLoginInfo(oidc.ProviderKey, subject, oidc.DisplayName)));
        Check(await users.AddToRoleAsync(user, role.Name!));
        db.ActivityLogEntries.Add(ActivityLogEntry.Create("User", user.Id.ToString(), action, null, ActorType.System));
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<ApplicationUser?> RestoreScimAsync(string subject, string username, string email, string? displayName,
        bool active, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.ProvisioningProvider == oidc.ProviderKey
            && u.ProvisioningExternalId == subject && u.IsScimDeleted, ct);
        if (user is null) return null;
        if (oidc.RecoveryUserIds.Contains(user.Id)) throw new ArgumentException("Recovery accounts cannot be provisioned.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {user.Id} FOR UPDATE", ct);
        var role = await FindAllowedRoleAsync(settings.DefaultGroup, ct);
        user.UserName = username; user.Email = email; user.DisplayName = displayName;
        user.IsScimDeleted = false; user.IsEnabled = active; user.PasswordHash = null;
        user.SecurityStamp = Guid.NewGuid().ToString();
        Check(await users.UpdateAsync(user));
        Check(await users.AddToRoleAsync(user, role.Name!));
        db.ActivityLogEntries.Add(ActivityLogEntry.Create("User", user.Id.ToString(), "ScimUserRestored", null, ActorType.System));
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
