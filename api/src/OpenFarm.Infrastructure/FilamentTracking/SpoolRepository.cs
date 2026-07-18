using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.FilamentTracking;

public sealed class SpoolRepository(OpenFarmDbContext dbContext) : ISpoolRepository
{
    public async Task AddAsync(Spool spool, CancellationToken cancellationToken)
    {
        dbContext.Spools.Add(spool);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Spool?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Spools.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Spool>> ListAsync(SpoolState? state, Guid? filamentVariantId, CancellationToken cancellationToken)
    {
        var query = dbContext.Spools.AsNoTracking().AsQueryable();

        if (state is { } s)
        {
            query = query.Where(spool => spool.State == s);
        }

        if (filamentVariantId is { } variantId)
        {
            query = query.Where(spool => spool.FilamentVariantId == variantId);
        }

        return await query.OrderBy(spool => spool.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Spool>> FindLoadedInFeedUnitAsync(Guid feedUnitId, CancellationToken cancellationToken)
    {
        var feedSlotIds = await dbContext.FeedSlots
            .Where(slot => slot.FeedUnitId == feedUnitId)
            .Select(slot => slot.Id)
            .ToListAsync(cancellationToken);

        return await dbContext.Spools
            .Where(spool => spool.State == SpoolState.Using && spool.FeedSlotId != null && feedSlotIds.Contains(spool.FeedSlotId.Value))
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
