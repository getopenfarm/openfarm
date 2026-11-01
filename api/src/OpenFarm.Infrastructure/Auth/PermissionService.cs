using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record UserAccess(bool IsAdministrator, string[] Groups, string[] Permissions);

public sealed class PermissionService(OpenFarmDbContext db)
{
    public async Task<UserAccess> ResolveAsync(Guid userId, CancellationToken ct)
    {
        var groups = await (from membership in db.UserRoles
                            join role in db.Roles on membership.RoleId equals role.Id
                            where membership.UserId == userId
                            select new { role.Id, role.Name, role.IsProtected }).ToListAsync(ct);
        var admin = groups.Any(g => g.IsProtected && g.Name == PermissionCatalog.Administrators);
        if (admin) return new(true, groups.Select(g => g.Name!).ToArray(), PermissionCatalog.All.Select(p => p.Key).ToArray());
        var ids = groups.Select(g => g.Id).ToArray();
        var inherited = await db.RoleClaims.Where(c => ids.Contains(c.RoleId) && c.ClaimType == PermissionCatalog.GrantClaim).Select(c => c.ClaimValue!).ToListAsync(ct);
        var extra = await db.UserClaims.Where(c => c.UserId == userId && c.ClaimType == PermissionCatalog.GrantClaim).Select(c => c.ClaimValue!).ToListAsync(ct);
        return new(false, groups.Select(g => g.Name!).ToArray(), inherited.Concat(extra).Where(PermissionCatalog.IsGrantable).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
