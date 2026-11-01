using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record GroupSummary(Guid Id, string Name);
public sealed record AccountResponse(Guid Id, string Username, string Email, string? Name, bool IsEnabled,
    DateTimeOffset CreatedAtUtc, GroupSummary[] Groups, string[] ExtraGrants, string[] EffectivePermissions, bool IsAdministrator, int SessionCount);
public sealed record GroupResponse(Guid Id, string Name, string Description, bool IsProtected, string[] Grants, int MemberCount);
public sealed record AccountInput(string Username, string Email, string? Name, Guid[] GroupIds, string[] ExtraGrants, bool IsEnabled, string? Password);
public sealed record GroupInput(string Name, string Description, string[] Grants);
public sealed record PasswordInput(string CurrentPassword, string NewPassword);
public sealed record ResetPasswordInput(string Password);
public sealed record ProfileInput(string? Name, string Email, string Username);

public sealed class AccountAdministrationService(OpenFarmDbContext db, UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles, PermissionService permissions, BrowserSessionService sessions)
{
    public async Task<AccountResponse[]> ListUsersAsync(CancellationToken ct)
    {
        var all = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct);
        var result = new List<AccountResponse>();
        foreach (var user in all)
        {
            var access = await permissions.ResolveAsync(user.Id, ct);
            var groups = await (from membership in db.UserRoles
                                join role in db.Roles on membership.RoleId equals role.Id
                                where membership.UserId == user.Id
                                select new GroupSummary(role.Id, role.Name!)).ToArrayAsync(ct);
            var grants = await db.UserClaims.Where(c => c.UserId == user.Id && c.ClaimType == PermissionCatalog.GrantClaim).Select(c => c.ClaimValue!).ToArrayAsync(ct);
            grants = PermissionCatalog.Sort(grants.Where(PermissionCatalog.IsGrantable));
            var count = await db.BrowserSessions.CountAsync(s => s.UserId == user.Id && s.RevokedAtUtc == null && s.ExpiresAtUtc > DateTimeOffset.UtcNow, ct);
            result.Add(new(user.Id, user.UserName!, user.Email!, user.DisplayName, user.IsEnabled, user.CreatedAtUtc,
                groups, grants, access.Permissions, access.IsAdministrator, count));
        }
        return result.ToArray();
    }

    public async Task<GroupResponse[]> ListGroupsAsync(CancellationToken ct)
    {
        var all = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var result = new List<GroupResponse>();
        foreach (var role in all)
            result.Add(new(role.Id, role.Name!, role.Description, role.IsProtected,
                PermissionCatalog.Sort((await db.RoleClaims.Where(c => c.RoleId == role.Id && c.ClaimType == PermissionCatalog.GrantClaim).Select(c => c.ClaimValue!).ToArrayAsync(ct)).Where(PermissionCatalog.IsGrantable)),
                await db.UserRoles.CountAsync(m => m.RoleId == role.Id, ct)));
        return result.ToArray();
    }

    // A database lock serializes all administrator/membership edits across server processes.
    private async Task LockAdministrationAsync(CancellationToken ct) =>
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71426499)", ct);

    private static void ValidateGrants(IEnumerable<string> grants)
    {
        if (grants.Any(g => !PermissionCatalog.IsGrantable(g))) throw new ArgumentException("Unknown or administrator-only permission.");
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new ArgumentException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public async Task<Guid> SaveUserAsync(Guid? id, AccountInput input, Guid actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Email)) throw new ArgumentException("Username and email are required.");
        if (input.Username.Length > 256 || input.Email.Length > 256) throw new ArgumentException("Username and email must be 256 characters or fewer.");
        if (input.GroupIds is null || input.ExtraGrants is null) throw new ArgumentException("Groups and extra permissions are required.");
        if (!System.Net.Mail.MailAddress.TryCreate(input.Email, out _)) throw new ArgumentException("Enter a valid email address.");
        ValidateGrants(input.ExtraGrants);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAdministrationAsync(ct);
        var groupIds = input.GroupIds.Distinct().ToArray();
        var selected = await db.Roles.Where(r => groupIds.Contains(r.Id)).ToArrayAsync(ct);
        if (selected.Length != groupIds.Length) throw new ArgumentException("One or more groups no longer exist.");
        var user = id is null ? new ApplicationUser() : await users.FindByIdAsync(id.Value.ToString()) ?? throw new KeyNotFoundException("User not found.");
        if (id is null && string.IsNullOrWhiteSpace(input.Password)) throw new ArgumentException("A temporary password is required.");
        if (id is not null && input.Password is not null) throw new ArgumentException("Use Reset password to change another user's password.");
        user.UserName = input.Username.Trim(); user.Email = input.Email.Trim(); user.DisplayName = input.Name?.Trim(); user.IsEnabled = input.IsEnabled;
        Check(id is null ? await users.CreateAsync(user, input.Password!) : await users.UpdateAsync(user));
        await db.UserRoles.Where(m => m.UserId == user.Id).ExecuteDeleteAsync(ct);
        db.UserRoles.AddRange(groupIds.Select(g => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = g }));
        await db.UserClaims.Where(c => c.UserId == user.Id && c.ClaimType == PermissionCatalog.GrantClaim).ExecuteDeleteAsync(ct);
        db.UserClaims.AddRange(input.ExtraGrants.Distinct(StringComparer.Ordinal).Select(g => new IdentityUserClaim<Guid> { UserId = user.Id, ClaimType = PermissionCatalog.GrantClaim, ClaimValue = g }));
        Audit(actor, "User", user.Id, id is null ? "UserCreated" : "UserUpdated");
        await db.SaveChangesAsync(ct);
        await EnsureAdministratorAsync(ct);
        if (!user.IsEnabled) await sessions.RevokeAsync(user.Id, null, actor, ct);
        await transaction.CommitAsync(ct);
        return user.Id;
    }

    private async Task EnsureAdministratorAsync(CancellationToken ct)
    {
        var exists = await (from user in db.Users
                            join membership in db.UserRoles on user.Id equals membership.UserId
                            join role in db.Roles on membership.RoleId equals role.Id
                            where user.IsEnabled && role.IsProtected && role.Name == PermissionCatalog.Administrators
                            select user.Id).AnyAsync(ct);
        if (!exists) throw new ArgumentException("At least one enabled administrator must remain.");
    }

    public async Task<Guid> SaveGroupAsync(Guid? id, GroupInput input, Guid actor, CancellationToken ct)
    {
        if (input.Grants is null) throw new ArgumentException("Permissions are required.");
        ValidateGrants(input.Grants);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 256) throw new ArgumentException("Group name is required and must be 256 characters or fewer.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAdministrationAsync(ct);
        var role = id is null ? new ApplicationRole() : await roles.FindByIdAsync(id.Value.ToString()) ?? throw new KeyNotFoundException("Group not found.");
        if (role.IsProtected) throw new ArgumentException("The Administrators group is protected.");
        role.Name = input.Name.Trim(); role.Description = input.Description?.Trim() ?? string.Empty;
        Check(id is null ? await roles.CreateAsync(role) : await roles.UpdateAsync(role));
        await db.RoleClaims.Where(c => c.RoleId == role.Id && c.ClaimType == PermissionCatalog.GrantClaim).ExecuteDeleteAsync(ct);
        db.RoleClaims.AddRange(input.Grants.Distinct(StringComparer.Ordinal).Select(g => new IdentityRoleClaim<Guid> { RoleId = role.Id, ClaimType = PermissionCatalog.GrantClaim, ClaimValue = g }));
        Audit(actor, "Group", role.Id, id is null ? "GroupCreated" : "GroupUpdated");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return role.Id;
    }

    public async Task RemoveGroupAsync(Guid id, Guid actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAdministrationAsync(ct);
        var group = await roles.FindByIdAsync(id.ToString()) ?? throw new KeyNotFoundException("Group not found.");
        if (group.IsProtected || await db.UserRoles.AnyAsync(m => m.RoleId == id, ct)) throw new ArgumentException("Only an empty, unprotected group can be removed.");
        Check(await roles.DeleteAsync(group));
        Audit(actor, "Group", id, "GroupRemoved");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task RemoveUserAsync(Guid id, Guid actor, CancellationToken ct)
    {
        if (id == actor) throw new ArgumentException("Cannot delete your own account");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAdministrationAsync(ct);
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new KeyNotFoundException("User not found.");
        // Sessions, memberships and grants cascade with the user row.
        Check(await users.DeleteAsync(user));
        await EnsureAdministratorAsync(ct);
        Audit(actor, "User", id, "UserRemoved");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task UpdateProfileAsync(Guid userId, ProfileInput input, CancellationToken ct)
    {
        var email = input.Email?.Trim() ?? string.Empty;
        var username = input.Username?.Trim() ?? string.Empty;
        if (email.Length == 0) throw new ArgumentException("Email is required.");
        if (username.Length == 0) throw new ArgumentException("Username is required.");
        if (email.Length > 256 || username.Length > 256) throw new ArgumentException("Username and email must be 256 characters or fewer.");
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _)) throw new ArgumentException("Enter a valid email address.");
        // Same lock as SaveUserAsync, so Identity's duplicate check is not raced into a unique-index 500.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAdministrationAsync(ct);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new KeyNotFoundException("User not found.");
        var name = input.Name?.Trim();
        user.DisplayName = string.IsNullOrEmpty(name) ? null : name;
        user.Email = email; user.UserName = username;
        var result = await users.UpdateAsync(user);
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")) throw new ArgumentException("Email or username is already taken.");
        Check(result);
        Audit(userId, "User", userId, "ProfileUpdated");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>Changes the caller's password and signs out every other session; <paramref name="currentSessionId"/> stays signed in.</summary>
    public async Task ChangePasswordAsync(Guid userId, PasswordInput input, Guid? currentSessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input.CurrentPassword)) throw new ArgumentException("Current password is required.");
        if (string.IsNullOrWhiteSpace(input.NewPassword)) throw new ArgumentException("A new password is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new KeyNotFoundException("User not found.");
        var result = await users.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);
        if (result.Errors.Any(e => e.Code == "PasswordMismatch")) throw new ArgumentException("Current password is incorrect.");
        Check(result);
        await sessions.RevokeAsync(userId, null, userId, currentSessionId, ct);
        Audit(userId, "User", userId, "PasswordChanged");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ResetPasswordAsync(Guid userId, string password, Guid actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("A temporary password is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new KeyNotFoundException("User not found.");
        // Hash through Identity's validators; no plaintext password enters the activity log.
        foreach (var validator in users.PasswordValidators) Check(await validator.ValidateAsync(users, user, password));
        user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid().ToString();
        Check(await users.UpdateAsync(user));
        await sessions.RevokeAsync(userId, null, actor, ct);
        Audit(actor, "User", userId, "PasswordReset");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private void Audit(Guid actor, string type, Guid id, string action) => db.ActivityLogEntries.Add(
        ActivityLogEntry.Create(type, id.ToString(), action, actor, ActorType.Operator));
}
