using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record UserAccess(bool IsAdministrator, string[] Groups, string[] Permissions);

public sealed class PermissionService(OpenFarmDbContext db, ProvisioningSettings provisioning)
{
    public bool IsProvisioningTarget(string? name) => provisioning.JitEnabled
        && (string.Equals(name, provisioning.DefaultGroup, StringComparison.OrdinalIgnoreCase)
            || provisioning.GroupMappings.Any(m => string.Equals(name, m.Group, StringComparison.OrdinalIgnoreCase)));

    public Task<int> MemberCountAsync(Guid roleId, CancellationToken ct) =>
        db.UserRoles.CountAsync(m => m.RoleId == roleId, ct);

    /// <summary>
    /// The one definition of "an administrator remains": every path that can remove an administrator (account edits and
    /// removal, the OIDC group sync) writes its change and then asks this, inside the administration lock.
    /// </summary>
    public Task<bool> HasEnabledAdministratorAsync(CancellationToken ct) =>
        (from user in db.Users
         join membership in db.UserRoles on user.Id equals membership.UserId
         join role in db.Roles on membership.RoleId equals role.Id
         where user.IsEnabled && role.IsProtected && role.Name == PermissionCatalog.Administrators
         select user.Id).AnyAsync(ct);

    public async Task<UserAccess> ResolveAsync(Guid userId, CancellationToken ct)
    {
        var groups = await (from membership in db.UserRoles
                            join role in db.Roles on membership.RoleId equals role.Id
                            where membership.UserId == userId
                            select new { role.Id, role.Name, role.IsProtected }).ToListAsync(ct);
        var admin = groups.Any(g => g.IsProtected && g.Name == PermissionCatalog.Administrators);
        var names = groups.Select(g => g.Name!).Distinct(StringComparer.Ordinal).ToArray();
        if (admin) return new(true, names, PermissionCatalog.All.Select(p => p.Key).ToArray());
        var ids = groups.Select(g => g.Id).Distinct().ToArray();
        var inherited = await db.RoleClaims.Where(c => ids.Contains(c.RoleId) && c.ClaimType == PermissionCatalog.GrantClaim).Select(c => c.ClaimValue!).ToListAsync(ct);
        var extra = await db.UserClaims.Where(c => c.UserId == userId && c.ClaimType == PermissionCatalog.GrantClaim).Select(c => c.ClaimValue!).ToListAsync(ct);
        return new(false, names, inherited.Concat(extra).Where(PermissionCatalog.IsGrantable).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
