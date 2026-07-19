using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Organizations;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class OrganizationRepository(OpenFarmDbContext dbContext) : IOrganizationRepository
{
    public async Task<Organization> GetAsync(CancellationToken cancellationToken) =>
        await dbContext.Organizations.FirstAsync(o => o.Id == Organization.WellKnownId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
