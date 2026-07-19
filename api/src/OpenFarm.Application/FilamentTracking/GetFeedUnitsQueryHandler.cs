using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Per-printer read of a printer's Feed Units, grouped with their Feed Slots — derives display
/// labels, detected material, and a superseded External Spool's hide-by-default filtering
/// (research.md §5/§8/§9/§11/§12).
/// </summary>
public sealed class GetFeedUnitsQueryHandler(
    IPrinterRepository printerRepository,
    IFeedUnitRepository feedUnitRepository,
    ISpoolRepository spoolRepository) : IQueryHandler<GetFeedUnitsQuery, IReadOnlyList<FeedUnitDetail>>
{
    public async Task<IReadOnlyList<FeedUnitDetail>> HandleAsync(GetFeedUnitsQuery query, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(query.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(query.PrinterId);

        var feedUnits = await feedUnitRepository.GetByPrinterIdAsync(query.PrinterId, cancellationToken);
        var usingSpools = await spoolRepository.ListAsync(SpoolState.Using, null, cancellationToken);
        var spoolBySlotId = usingSpools
            .Where(s => s.FeedSlotId is not null)
            .ToDictionary(s => s.FeedSlotId!.Value);

        var singleFilamentInlet = !PrinterModelCatalog.TryResolve(printer.Brand, printer.ModelCode, out var model) || model!.FilamentInletCount == 1;

        var details = feedUnits
            .Select(unit => BuildDetail(unit, feedUnits, printer, spoolBySlotId, singleFilamentInlet))
            .ToList();

        return details
            .Where(d => !ShouldHide(d, spoolBySlotId, feedUnits.First(u => u.Id == d.Id)))
            .ToList();
    }

    private static FeedUnitDetail BuildDetail(
        FeedUnit unit,
        IReadOnlyList<FeedUnit> allUnits,
        Printer printer,
        Dictionary<Guid, Spool> spoolBySlotId,
        bool singleFilamentInlet)
    {
        var slots = unit.FeedSlots
            .OrderBy(s => s.SlotPosition)
            .Select(slot => BuildSlotDetail(unit, slot, printer, spoolBySlotId))
            .ToList();

        var supersededByFeedUnitId = FeedUnitDisplay.ResolveSupersededByFeedUnitId(unit, allUnits, singleFilamentInlet);

        return new FeedUnitDetail(
            unit.Id,
            unit.Kind,
            unit.UnitIndex,
            FeedUnitDisplay.UnitLabel(unit, printer.Brand),
            unit.IsRevealed,
            unit.ReplacesFeedUnitId,
            supersededByFeedUnitId,
            slots);
    }

    private static FeedSlotDetail BuildSlotDetail(FeedUnit unit, FeedSlot slot, Printer printer, Dictionary<Guid, Spool> spoolBySlotId)
    {
        var currentSpool = spoolBySlotId.TryGetValue(slot.Id, out var spool)
            ? new CurrentSpoolSummary(spool.Id, spool.FilamentVariantId, spool.RemainingWeightGrams)
            : null;

        var detectedTray = FeedUnitDisplay.ResolveDetectedTray(unit, slot, printer);

        return new FeedSlotDetail(
            slot.Id,
            slot.SlotPosition,
            FeedUnitDisplay.SlotLabel(unit, slot, printer.Brand),
            slot.DeactivatedAtUtc,
            currentSpool,
            detectedTray is { Empty: false } ? detectedTray.MaterialType : null,
            detectedTray is { Empty: false } ? detectedTray.ColorHex : null,
            detectedTray is { Empty: false } ? detectedTray.SubBrand : null);
    }

    /// <summary>research.md §12 — omitted only when superseded, unrevealed, and nothing is currently loaded on it.</summary>
    private static bool ShouldHide(FeedUnitDetail detail, Dictionary<Guid, Spool> spoolBySlotId, FeedUnit unit)
    {
        if (detail.Kind != FeedUnitKindCatalog.ExternalSpool)
        {
            return false;
        }

        var hasLoadedSpool = unit.FeedSlots.Any(s => spoolBySlotId.ContainsKey(s.Id));
        return detail.SupersededByFeedUnitId is not null && !detail.IsRevealed && !hasLoadedSpool;
    }
}
