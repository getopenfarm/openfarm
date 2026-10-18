using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

public sealed class PrintRepository(OpenFarmDbContext dbContext) : IPrintRepository
{
    public Task<Print?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Prints.Include(p => p.Filaments).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Print> Prints, int TotalCount)> ListNeedingFilamentFollowUpAsync(
        int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Prints.Where(p =>
            p.FilamentFollowUpDismissedAtUtc == null
            && (p.Status == PrintJobStatus.Completed || p.Status == PrintJobStatus.Failed || p.Status == PrintJobStatus.Cancelled)
            && p.Filaments.Any(f => f.SkippedReason != null));
        var total = await query.CountAsync(cancellationToken);
        var prints = await query.OrderByDescending(p => p.EndedAtUtc).ThenBy(p => p.Id)
            .Include(p => p.Filaments).Take(limit).ToListAsync(cancellationToken);
        return (prints, total);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ProductionRunConcurrencyConflictException(ex.Entries.Select(e => e.Entity).OfType<Print>().FirstOrDefault()?.Id ?? Guid.Empty);
        }
    }
}
