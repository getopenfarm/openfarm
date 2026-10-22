using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.FilamentTracking;

public sealed class SpoolRepository(OpenFarmDbContext dbContext) : ISpoolRepository, ISpoolReadModel
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

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private const int MaxSearchTerms = 8;
    private const int MaxSearchTermLength = 64;

    public async Task<SpoolRowPage> ListRowsAsync(GetSpoolsQuery query, CancellationToken cancellationToken)
    {
        // Inner joins to the catalog (an orphaned spool has no row to show); left joins for vendor and
        // the loaded slot, which may legitimately be absent.
        var rows =
            from spool in dbContext.Spools.AsNoTracking()
            join variant in dbContext.FilamentVariants on spool.FilamentVariantId equals variant.Id
            join filament in dbContext.Filaments on variant.FilamentId equals filament.Id
            join vendor in dbContext.Vendors on filament.VendorId equals vendor.Id into vendors
            from vendor in vendors.DefaultIfEmpty()
            join slot in dbContext.FeedSlots on spool.FeedSlotId equals (Guid?)slot.Id into slots
            from slot in slots.DefaultIfEmpty()
            join unit in dbContext.FeedUnits on slot.FeedUnitId equals (Guid?)unit.Id into units
            from unit in units.DefaultIfEmpty()
            join printer in dbContext.Printers on unit.PrinterId equals (Guid?)printer.Id into printers
            from printer in printers.DefaultIfEmpty()
            select new { spool, variant, filament, vendor, slot, unit, printer };

        if (query.SpoolId is { } spoolId)
        {
            rows = rows.Where(x => x.spool.Id == spoolId);
        }

        if (query.State is { } state)
        {
            rows = rows.Where(x => x.spool.State == state);
        }

        if (query.FilamentVariantId is { } variantId)
        {
            rows = rows.Where(x => x.spool.FilamentVariantId == variantId);
        }

        if (query.FilamentId is { } filamentId)
        {
            rows = rows.Where(x => x.filament.Id == filamentId);
        }

        if (query.VendorId is { } vendorId)
        {
            rows = rows.Where(x => x.filament.VendorId == vendorId);
        }

        foreach (var term in SearchTerms(query.Search))
        {
            var pattern = $"%{term}%";
            rows = rows.Where(x =>
                EF.Functions.ILike(x.spool.Label ?? "", pattern, "\\")
                || EF.Functions.ILike(x.vendor.Name ?? "", pattern, "\\")
                || EF.Functions.ILike(x.filament.Material, pattern, "\\")
                || EF.Functions.ILike(x.filament.MaterialType, pattern, "\\")
                || EF.Functions.ILike(x.variant.ColorName, pattern, "\\"));
        }

        var paged = query.Page is not null || query.PageSize is not null;
        var pageSize = Math.Clamp(query.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        var total = paged ? await rows.CountAsync(cancellationToken) : 0;

        var ordered = rows
            .OrderBy(x => x.spool.CreatedAtUtc)
            .ThenBy(x => x.spool.Id);
        var window = paged ? ordered.Skip((Math.Max(query.Page ?? 1, 1) - 1) * pageSize).Take(pageSize) : ordered;

        var projected = await window
            .Select(x => new
            {
                x.spool.Id,
                x.spool.Label,
                x.spool.FilamentVariantId,
                x.variant.ColorName,
                x.variant.ColorHex,
                x.filament.Material,
                VendorName = x.vendor.Name ?? string.Empty,
                x.spool.SpoolWeightGrams,
                x.spool.RemainingWeightGrams,
                x.spool.State,
                x.spool.FeedSlotId,
                x.spool.HasUsageOverrun,
                UnitKind = x.unit.Kind,
                UnitIndex = (int?)x.unit.UnitIndex,
                UnitName = x.unit.Name,
                SlotPosition = (int?)x.slot.SlotPosition,
                PrinterBrand = x.printer.Brand,
                PrinterName = x.printer.Name,
            })
            .ToListAsync(cancellationToken);

        var spoolRows = projected
            .Select(r => new SpoolRow(
                r.Id,
                r.Label,
                r.FilamentVariantId,
                r.ColorName,
                r.ColorHex,
                r.Material,
                r.VendorName,
                r.SpoolWeightGrams,
                r.RemainingWeightGrams,
                r.State,
                r.FeedSlotId,
                r.HasUsageOverrun,
                r is { UnitKind: not null, UnitIndex: { } unitIndex, SlotPosition: { } slotPosition }
                    ? new SpoolSlotPlacement(r.UnitKind, unitIndex, r.UnitName, slotPosition, r.PrinterBrand ?? string.Empty, r.PrinterName ?? string.Empty)
                    : null))
            .ToList();

        return new SpoolRowPage(spoolRows, paged ? total : spoolRows.Count);
    }

    public async Task<SpoolHistoryPage?> ListHistoryAsync(GetSpoolHistoryQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Spools.AsNoTracking().AnyAsync(s => s.Id == query.SpoolId, cancellationToken))
        {
            return null;
        }

        var pageSize = Math.Clamp(query.PageSize, 1, GetSpoolHistoryQuery.MaxPageSize);
        var page = Math.Max(query.Page, 1);

        var records = dbContext.SpoolUsageRecords.AsNoTracking().Where(r => r.SpoolId == query.SpoolId);
        var total = await records.CountAsync(cancellationToken);

        var rows = await (
                from record in records
                join print in dbContext.Prints.AsNoTracking() on record.JobId equals (Guid?)print.Id into prints
                from print in prints.DefaultIfEmpty()
                orderby record.RecordedAtUtc descending, record.Id
                select new
                {
                    record.Id,
                    record.RecordedAtUtc,
                    record.GramsConsumed,
                    record.RemainingAfterGrams,
                    record.Source,
                    record.Reason,
                    PrintId = (Guid?)print.Id,
                    PrintName = print.Name,
                    PrintStatus = (PrintJobStatus?)print.Status,
                })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new SpoolHistoryPage(
            rows.Select(r => new SpoolHistoryEntry(
                r.Id, r.RecordedAtUtc, r.GramsConsumed, r.RemainingAfterGrams, r.Source, r.Reason, r.PrintId, r.PrintName, r.PrintStatus)).ToList(),
            total);
    }

    /// <summary>Trimmed, bounded, LIKE-escaped terms; a bounded term count keeps a pasted paragraph from becoming a huge predicate.</summary>
    private static IEnumerable<string> SearchTerms(string? search) =>
        (search ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxSearchTerms)
            .Select(term => (term.Length > MaxSearchTermLength ? term[..MaxSearchTermLength] : term)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal));

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

    public Task<Spool?> FindUsingFeedSlotAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
        dbContext.Spools.FirstOrDefaultAsync(s => s.State == SpoolState.Using && s.FeedSlotId == feedSlotId, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            var spoolId = ex.Entries.Select(e => e.Entity).OfType<Spool>().FirstOrDefault()?.Id ?? Guid.Empty;
            throw new SpoolConcurrencyConflictException(spoolId);
        }
    }
}
