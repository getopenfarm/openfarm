using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>Computed read-time-only fields (data-model.md's "Derived display fields") shared by <see cref="GetSpoolsQueryHandler"/>, <see cref="GetFeedUnitsQueryHandler"/>, and <see cref="LoadSpoolOntoFeedSlotCommandHandler"/>.</summary>
internal static class FeedUnitDisplay
{
    public static string UnitLabel(FeedUnit unit, string printerBrand) =>
        UnitLabel(unit.Kind, unit.UnitIndex, unit.Name, printerBrand);

    /// <summary>Column-level overload, so read queries can label a slot from projected columns without loading the entities.</summary>
    public static string UnitLabel(string kind, int unitIndex, string? name, string printerBrand) => kind switch
    {
        FeedUnitKindCatalog.MultiMaterialUnit when IsToolChanger(printerBrand) => "Toolheads",
        FeedUnitKindCatalog.MultiMaterialUnit => $"{FeedUnitKindCatalog.ResolveMultiMaterialUnitTerm(printerBrand)} {unitIndex + 1}",
        FeedUnitKindCatalog.ExternalSpool => "External Spool",
        FeedUnitKindCatalog.AutomaticFilamentReloader => name ?? "Automatic Filament Reloader",
        _ => kind,
    };

    public static string SlotLabel(FeedUnit unit, FeedSlot slot, string printerBrand) =>
        SlotLabel(unit.Kind, unit.UnitIndex, unit.Name, slot.SlotPosition, printerBrand);

    public static string SlotLabel(string kind, int unitIndex, string? name, int slotPosition, string printerBrand) => kind switch
    {
        FeedUnitKindCatalog.ExternalSpool => UnitLabel(kind, unitIndex, name, printerBrand),
        FeedUnitKindCatalog.MultiMaterialUnit when IsToolChanger(printerBrand) => $"T{slotPosition + 1}",
        _ => $"{UnitLabel(kind, unitIndex, name, printerBrand)} · Slot {slotPosition + 1}",
    };

    // A Snapmaker's connector-derived unit is its toolheads, one slot each, named T1–T4 as on the printer.
    private static bool IsToolChanger(string printerBrand) =>
        Enum.TryParse<PrinterBrand>(printerBrand, ignoreCase: true, out var brand) && brand == PrinterBrand.Snapmaker;

    /// <summary>FR-021, gated purely on live per-slot data presence — no <c>Kind</c> restriction (research.md §8).</summary>
    public static AmsTray? ResolveDetectedTray(FeedUnit unit, FeedSlot slot, Printer printer) => unit.Kind switch
    {
        FeedUnitKindCatalog.MultiMaterialUnit => printer.AmsUnits
            .FirstOrDefault(a => a.UnitIndex == unit.UnitIndex)?.Trays
            .FirstOrDefault(t => t.SlotIndex == slot.SlotPosition),
        FeedUnitKindCatalog.ExternalSpool => printer.ExternalSpoolTray,
        FeedUnitKindCatalog.AutomaticFilamentReloader when unit.ArchivedAtUtc is null
            && unit.ReplacesFeedUnitId is not null && unit.FeedingSlotId == slot.Id => printer.ExternalSpoolTray,
        _ => null,
    };

    /// <summary>research.md §9/§11 priority order: an explicit <see cref="FeedUnit.ReplacesFeedUnitId"/> declaration first, then (single-filament-inlet printers only) an active Multi-Material Unit's presence.</summary>
    public static Guid? ResolveSupersededByFeedUnitId(FeedUnit unit, IReadOnlyList<FeedUnit> allUnits, bool singleFilamentInlet)
    {
        if (unit.Kind != FeedUnitKindCatalog.ExternalSpool)
        {
            return null;
        }

        var declaringAfr = allUnits.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader && u.ArchivedAtUtc is null && u.ReplacesFeedUnitId == unit.Id);
        if (declaringAfr is not null)
        {
            return declaringAfr.Id;
        }

        if (!singleFilamentInlet)
        {
            return null;
        }

        return allUnits.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit && u.FeedSlots.Any(s => s.DeactivatedAtUtc is null))?.Id;
    }
}
