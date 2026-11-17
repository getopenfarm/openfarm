using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record UserAccess(bool IsAdministrator, string[] Groups, string[] Permissions);

public sealed class PermissionService(OpenFarmDbContext db, ProvisioningSettings provisioning, OidcSettings oidc)
{
    public bool IsProvisioningTarget(string? name) => (provisioning.JitEnabled || provisioning.ScimEnabled)
        && (string.Equals(name, provisioning.DefaultGroup, StringComparison.OrdinalIgnoreCase)
            || provisioning.GroupMappings.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)));

    public async Task<int> MemberCountAsync(Guid roleId, string? roleName, CancellationToken ct)
    {
        var local = db.UserRoles.Where(m => m.RoleId == roleId).Select(m => m.UserId);
        if (!oidc.Enabled || !provisioning.ScimEnabled) return await local.CountAsync(ct);
        var externalNames = provisioning.GroupMappings.Where(p => string.Equals(p.Value, roleName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Key.ToUpperInvariant()).ToArray();
        var managed = from member in db.ProvisioningGroupMembers join externalGroup in db.ProvisioningGroups on member.GroupId equals externalGroup.Id
                      where externalGroup.Provider == oidc.ProviderKey && externalNames.Contains(externalGroup.NormalizedDisplayName) select member.UserId;
        return await local.Union(managed).CountAsync(ct);
    }

    public async Task<GroupSummary[]> ManagedGroupsAsync(Guid userId, CancellationToken ct)
    {
        if (!oidc.Enabled || !provisioning.ScimEnabled || provisioning.GroupMappings.Count == 0) return [];
        var externalNames = await (from member in db.ProvisioningGroupMembers
                                   join externalGroup in db.ProvisioningGroups on member.GroupId equals externalGroup.Id
                                   where member.UserId == userId && externalGroup.Provider == oidc.ProviderKey
                                   select externalGroup.DisplayName).ToArrayAsync(ct);
        var normalized = externalNames.Where(provisioning.GroupMappings.ContainsKey)
            .Select(n => provisioning.GroupMappings[n].ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        return await db.Roles.Where(r => normalized.Contains(r.NormalizedName!) && !r.IsProtected && r.Name != PermissionCatalog.Administrators)
            .Select(r => new GroupSummary(r.Id, r.Name!)).ToArrayAsync(ct);
    }

    public async Task<UserAccess> ResolveAsync(Guid userId, CancellationToken ct)
    {
        var groups = await (from membership in db.UserRoles
                            join role in db.Roles on membership.RoleId equals role.Id
                            where membership.UserId == userId
                            select new { role.Id, role.Name, role.IsProtected }).ToListAsync(ct);
        var admin = groups.Any(g => g.IsProtected && g.Name == PermissionCatalog.Administrators);
        var managed = await ManagedGroupsAsync(userId, ct);
        var names = groups.Select(g => g.Name!).Concat(managed.Select(g => g.Name)).Distinct(StringComparer.Ordinal).ToArray();
        if (admin) return new(true, names, PermissionCatalog.All.Select(p => p.Key).ToArray());
        var ids = groups.Select(g => g.Id).Concat(managed.Select(g => g.Id)).Distinct().ToArray();
        var inherited = await db.RoleClaims.Where(c => ids.Contains(c.RoleId) && c.ClaimType == PermissionCatalog.GrantClaim).Select(c => c.ClaimValue!).ToListAsync(ct);
        var extra = await db.UserClaims.Where(c => c.UserId == userId && c.ClaimType == PermissionCatalog.GrantClaim).Select(c => c.ClaimValue!).ToListAsync(ct);
        return new(false, names, inherited.Concat(extra).Where(PermissionCatalog.IsGrantable).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
