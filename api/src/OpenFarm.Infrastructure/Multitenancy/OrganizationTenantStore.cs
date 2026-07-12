using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.Organizations;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Multitenancy;

public sealed class OrganizationTenantStore(OpenFarmDbContext dbContext) : IMultiTenantStore<AppTenantInfo>
{
    public const string StaticIdentifier = "default";

    public async Task<AppTenantInfo?> GetAsync(string id)
    {
        var organization = await dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id.ToString() == id);

        return ToTenantInfo(organization);
    }

    public async Task<AppTenantInfo?> GetByIdentifierAsync(string identifier)
    {
        if (identifier != StaticIdentifier)
        {
            return null;
        }

        var organization = await dbContext.Organizations.AsNoTracking().FirstOrDefaultAsync();
        return ToTenantInfo(organization);
    }

    public async Task<IEnumerable<AppTenantInfo>> GetAllAsync()
    {
        var organizations = await dbContext.Organizations.AsNoTracking().ToListAsync();
        return organizations.Select(o => ToTenantInfo(o)!);
    }

    public Task<IEnumerable<AppTenantInfo>> GetAllAsync(int take, int skip) =>
        throw new NotSupportedException("Pagination is not supported for the single-organization store.");

    public Task<bool> AddAsync(AppTenantInfo tenantInfo) =>
        throw new NotSupportedException("Organizations are created only by FirstBootSeeder.");

    public Task<bool> UpdateAsync(AppTenantInfo tenantInfo) =>
        throw new NotSupportedException("Organizations are immutable in this phase.");

    public Task<bool> RemoveAsync(string identifier) =>
        throw new NotSupportedException("Organizations cannot be removed.");

    private static AppTenantInfo? ToTenantInfo(Organization? organization) =>
        organization is null
            ? null
            : new AppTenantInfo
            {
                Id = organization.Id.ToString(),
                Identifier = StaticIdentifier,
                Name = organization.Name,
            };
}
