using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.FilamentTracking;

public sealed class FilamentRepository(OpenFarmDbContext dbContext) : IFilamentRepository
{
    public async Task AddAsync(Filament filament, CancellationToken cancellationToken)
    {
        dbContext.Filaments.Add(filament);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Filament?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Filaments.Include(f => f.Variants).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Filament>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Filaments.AsNoTracking().Include(f => f.Variants).OrderBy(f => f.Material).ToListAsync(cancellationToken);

    public Task<FilamentVariant?> FindVariantByIdAsync(Guid filamentVariantId, CancellationToken cancellationToken) =>
        dbContext.FilamentVariants.FirstOrDefaultAsync(v => v.Id == filamentVariantId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
