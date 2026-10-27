using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

public sealed class PrintRepository(OpenFarmDbContext dbContext) : IPrintRepository
{
    private static readonly PrintJobStatus[] ClosedStatuses =
        [PrintJobStatus.Completed, PrintJobStatus.Failed, PrintJobStatus.Cancelled];

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

    public async Task<IReadOnlyDictionary<Guid, string?>> GetCurrentPrintNamesAsync(
        IReadOnlyCollection<Printer> printers, CancellationToken cancellationToken)
    {
        var ids = printers.Select(p => p.Id).ToList();
        var awaitingClear = printers.Where(p => p.Status == PrinterStatus.AwaitingClear).Select(p => p.Id).ToList();
        var current = await dbContext.Prints.AsNoTracking()
            .Where(p => p.PrinterId != null && ids.Contains(p.PrinterId.Value)
                && (!ClosedStatuses.Contains(p.Status) || awaitingClear.Contains(p.PrinterId.Value)))
            .GroupBy(p => p.PrinterId!.Value)
            .Select(g => g.OrderBy(p => ClosedStatuses.Contains(p.Status)).ThenByDescending(p => p.EndedAtUtc)
                .Select(p => new { PrinterId = g.Key, p.Source, p.Name }).First())
            .ToDictionaryAsync(p => p.PrinterId, cancellationToken);
        return printers.ToDictionary(p => p.Id, p =>
            current.GetValueOrDefault(p.Id) is { Source: PrintSource.Dispatched } print ? print.Name : p.CurrentJobName);
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
