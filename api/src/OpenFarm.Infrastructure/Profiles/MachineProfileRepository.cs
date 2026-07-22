using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Profiles;

public sealed class MachineProfileRepository(OpenFarmDbContext dbContext) : IMachineProfileRepository
{
    public async Task AddAsync(MachineProfile profile, CancellationToken cancellationToken)
    {
        dbContext.MachineProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<MachineProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MachineProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.MachineProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MachineProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        await dbContext.MachineProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name)
            .ToListAsync(cancellationToken);

    public Task<MachineProfile?> FindByExternalIdAsync(string vendorName, string externalId, CancellationToken cancellationToken) =>
        dbContext.MachineProfiles.FirstOrDefaultAsync(
            p => p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == externalId, cancellationToken);

    public Task<MachineProfile?> FindSystemByNameAsync(string vendorName, string name, CancellationToken cancellationToken) =>
        dbContext.MachineProfiles.FirstOrDefaultAsync(
            p => p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == null && p.Name == name, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
