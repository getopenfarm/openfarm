using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Profiles;

public sealed class OrganizationFilamentDefaultRepository(OpenFarmDbContext dbContext) : IOrganizationFilamentDefaultRepository
{
    public async Task AddAsync(OrganizationFilamentDefault entry, CancellationToken cancellationToken)
    {
        dbContext.OrganizationFilamentDefaults.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<OrganizationFilamentDefault?> FindByMaterialTypeAsync(string materialType, CancellationToken cancellationToken) =>
        dbContext.OrganizationFilamentDefaults.FirstOrDefaultAsync(d => d.MaterialType == materialType, cancellationToken);

    public async Task<IReadOnlyList<OrganizationFilamentDefault>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.OrganizationFilamentDefaults.AsNoTracking().OrderBy(d => d.MaterialType).ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
