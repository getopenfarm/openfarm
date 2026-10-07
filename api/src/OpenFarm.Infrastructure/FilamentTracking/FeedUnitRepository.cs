using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.FilamentTracking;

public sealed class FeedUnitRepository(OpenFarmDbContext dbContext) : IFeedUnitRepository
{
    public async Task<T> ExecuteAtomicallyAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken, Guid? lockId = null)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize configuration, loads and removal for a printer feed path. Guid hashing is
            // deterministic; collisions only serialize unrelated units, never compromise correctness.
            if (lockId is { } id)
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({id.GetHashCode()})", cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException)
        {
            throw new SpoolConcurrencyConflictException(Guid.Empty);
        }
    }

    public Task<Guid?> GetPrinterIdAsync(Guid feedUnitId, CancellationToken cancellationToken) =>
        dbContext.FeedUnits.AsNoTracking().Where(u => u.Id == feedUnitId)
            .Select(u => (Guid?)u.PrinterId).FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(FeedUnit feedUnit, CancellationToken cancellationToken)
    {
        dbContext.FeedUnits.Add(feedUnit);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<FeedUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.FeedUnits.Include(u => u.FeedSlots).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyList<FeedUnit>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.FeedUnits.AsNoTracking().Include(u => u.FeedSlots).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FeedUnit>> GetByPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
        await dbContext.FeedUnits
            .Include(u => u.FeedSlots)
            .Where(u => u.PrinterId == printerId)
            .OrderBy(u => u.Kind).ThenBy(u => u.UnitIndex)
            .ToListAsync(cancellationToken);

    public Task<FeedUnit?> FindByPrinterKindUnitIndexAsync(Guid printerId, string kind, int unitIndex, CancellationToken cancellationToken) =>
        dbContext.FeedUnits
            .Include(u => u.FeedSlots)
            .FirstOrDefaultAsync(u => u.PrinterId == printerId && u.Kind == kind && u.UnitIndex == unitIndex, cancellationToken);

    public Task<FeedSlot?> GetFeedSlotByIdAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
        dbContext.FeedSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == feedSlotId, cancellationToken);

    public Task<FeedUnit?> GetByFeedSlotIdAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
        dbContext.FeedUnits
            .Include(u => u.FeedSlots)
            .FirstOrDefaultAsync(u => u.FeedSlots.Any(s => s.Id == feedSlotId), cancellationToken);

    public void TrackNewFeedSlot(FeedSlot feedSlot) => dbContext.FeedSlots.Add(feedSlot);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
