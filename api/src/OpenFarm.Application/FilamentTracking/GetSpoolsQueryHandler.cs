using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>Denormalized joined read (contracts/filament-tracking-api.md) — every field computed at read time, never cached columns (plan.md's Constraints, SC-002).</summary>
public sealed class GetSpoolsQueryHandler(
    ISpoolRepository spoolRepository,
    IFilamentRepository filamentRepository,
    IVendorRepository vendorRepository,
    IFeedUnitRepository feedUnitRepository,
    IPrinterRepository printerRepository) : IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>>
{
    public async Task<IReadOnlyList<SpoolSummary>> HandleAsync(GetSpoolsQuery query, CancellationToken cancellationToken)
    {
        var spools = await spoolRepository.ListAsync(query.State, query.FilamentVariantId, cancellationToken);

        var filaments = await filamentRepository.ListAsync(cancellationToken);
        var vendors = (await vendorRepository.ListAsync(cancellationToken)).ToDictionary(v => v.Id);
        var variantLookup = filaments
            .SelectMany(f => f.Variants.Select(v => (Filament: f, Variant: v)))
            .ToDictionary(x => x.Variant.Id);

        var feedUnits = await feedUnitRepository.ListAsync(cancellationToken);
        var printers = (await printerRepository.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var slotLookup = feedUnits
            .SelectMany(u => u.FeedSlots.Select(s => (Unit: u, Slot: s)))
            .ToDictionary(x => x.Slot.Id);

        var results = new List<SpoolSummary>();
        foreach (var spool in spools)
        {
            if (!variantLookup.TryGetValue(spool.FilamentVariantId, out var variantEntry))
            {
                continue;
            }

            var (filament, variant) = variantEntry;
            if (query.FilamentId is { } filamentId && filament.Id != filamentId)
            {
                continue;
            }

            if (query.VendorId is { } vendorId && filament.VendorId != vendorId)
            {
                continue;
            }

            var vendorName = vendors.TryGetValue(filament.VendorId, out var vendor) ? vendor.Name : string.Empty;

            string? feedSlotLabel = null;
            if (spool.FeedSlotId is { } feedSlotId && slotLookup.TryGetValue(feedSlotId, out var slotEntry))
            {
                var brand = printers.TryGetValue(slotEntry.Unit.PrinterId, out var printer) ? printer.Brand : string.Empty;
                feedSlotLabel = FeedUnitDisplay.SlotLabel(slotEntry.Unit, slotEntry.Slot, brand);
            }

            results.Add(new SpoolSummary(
                spool.Id,
                spool.Label,
                spool.FilamentVariantId,
                variant.ColorName,
                variant.ColorHex,
                filament.Material,
                vendorName,
                spool.SpoolWeightGrams,
                spool.RemainingWeightGrams,
                spool.State,
                spool.FeedSlotId,
                feedSlotLabel,
                spool.HasUsageOverrun));
        }

        return results;
    }
}
