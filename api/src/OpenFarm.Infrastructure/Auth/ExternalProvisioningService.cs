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
        if (!settings.JitEnabled) return;
        _ = await FindAllowedRoleAsync(settings.DefaultGroup, ct);
    }

    private async Task<ApplicationRole> FindAllowedRoleAsync(string name, CancellationToken ct)
    {
        var normalized = name.ToUpperInvariant();
        var role = await db.Roles.SingleOrDefaultAsync(r => r.NormalizedName == normalized, ct);
        if (role is null || role.IsProtected || string.Equals(role.Name, PermissionCatalog.Administrators, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Provisioning can use only existing, unprotected OpenFarm groups.");
        return role;
    }

    /// <summary>Caller owns the transaction. A disabled account is never re-enabled.</summary>
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
        return await CreateAsync(subject, username, profile.Email.Trim(), profile.DisplayName, ct);
    }

    private async Task<ApplicationUser> CreateAsync(string subject, string username, string email, string? displayName, CancellationToken ct)
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
