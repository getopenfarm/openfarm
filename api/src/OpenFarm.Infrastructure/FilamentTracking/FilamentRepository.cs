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
        dbContext.Filaments.Include(f => f.Variants).Include(f => f.ProfileAssignments).AsSplitQuery().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Filament>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Filaments.AsNoTracking().Include(f => f.Variants).Include(f => f.ProfileAssignments).AsSplitQuery().OrderBy(f => f.Material).ToListAsync(cancellationToken);

    public Task<FilamentVariant?> FindVariantByIdAsync(Guid filamentVariantId, CancellationToken cancellationToken) =>
        dbContext.FilamentVariants.FirstOrDefaultAsync(v => v.Id == filamentVariantId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        // DetectChanges would attach new Guid-keyed children as Modified before we can mark them Added.
        var autoDetect = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var filament in dbContext.ChangeTracker.Entries<Filament>().Select(e => e.Entity).ToList())
            {
                foreach (var variant in filament.Variants)
                    if (dbContext.Entry(variant).State == EntityState.Detached) dbContext.Add(variant);
                foreach (var assignment in filament.ProfileAssignments)
                    if (dbContext.Entry(assignment).State == EntityState.Detached) dbContext.Add(assignment);
            }
        }
        finally { dbContext.ChangeTracker.AutoDetectChangesEnabled = autoDetect; }
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
