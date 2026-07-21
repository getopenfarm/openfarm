using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Profiles;

public sealed class ProcessProfileRepository(OpenFarmDbContext dbContext) : IProcessProfileRepository
{
    public async Task AddAsync(ProcessProfile profile, CancellationToken cancellationToken)
    {
        dbContext.ProcessProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ProcessProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProcessProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProcessProfile>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.ProcessProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProcessProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        await dbContext.ProcessProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
