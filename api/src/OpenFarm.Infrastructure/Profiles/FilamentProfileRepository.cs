using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Profiles;

public sealed class FilamentProfileRepository(OpenFarmDbContext dbContext) : IFilamentProfileRepository
{
    public async Task AddAsync(FilamentProfile profile, CancellationToken cancellationToken)
    {
        dbContext.FilamentProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<FilamentProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.FilamentProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.FilamentProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FilamentProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        await dbContext.FilamentProfiles
            .AsNoTracking()
            .Where(p => p.SourceFormat == sourceFormat && p.Name == name)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
