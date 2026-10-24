using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>Computed read-time-only fields (data-model.md's "Derived display fields") shared by <see cref="GetSpoolsQueryHandler"/>, <see cref="GetFeedUnitsQueryHandler"/>, <see cref="LoadSpoolOntoFeedSlotCommandHandler"/>, and the live feed readings the API sends.</summary>
public static class FeedUnitDisplay
{
    public static string UnitLabel(FeedUnit unit, PrinterModel? model) =>
        UnitLabel(unit.Kind, unit.UnitIndex, unit.Name, model);

    /// <summary>Column-level overload, so read queries can label a slot from projected columns without loading the entities.</summary>
    public static string UnitLabel(string kind, int unitIndex, string? name, PrinterModel? model) => kind switch
    {
        FeedUnitKindCatalog.MultiMaterialUnit => model?.FeedUnitStyle switch
        {
            FeedUnitStyle.Toolheads => "Toolheads",
            FeedUnitStyle.Ams => $"AMS {unitIndex + 1}",
            _ => $"Multi-Material Unit {unitIndex + 1}",
        },
        FeedUnitKindCatalog.ExternalSpool => "External Spool",
        FeedUnitKindCatalog.AutomaticFilamentReloader => name ?? "Automatic Filament Reloader",
        _ => kind,
    };

    public static string SlotLabel(FeedUnit unit, FeedSlot slot, PrinterModel? model) =>
        SlotLabel(unit.Kind, unit.UnitIndex, unit.Name, slot.SlotPosition, model);

    public static string SlotLabel(string kind, int unitIndex, string? name, int slotPosition, PrinterModel? model) => kind switch
    {
        FeedUnitKindCatalog.ExternalSpool => UnitLabel(kind, unitIndex, name, model),
        // A toolchanger's slot n is the spool on extruder n, named as on the printer (T1–T4).
        FeedUnitKindCatalog.MultiMaterialUnit when model is { FeedUnitStyle: FeedUnitStyle.Toolheads }
            && slotPosition < model.Extruders.Count => model.Extruders[slotPosition],
        _ => $"{UnitLabel(kind, unitIndex, name, model)} · Slot {slotPosition + 1}",
    };

    /// <summary>FR-021, gated purely on live per-slot data presence — no <c>Kind</c> restriction (research.md §8).</summary>
    public static FeedSlotReading? ResolveDetectedTray(FeedUnit unit, FeedSlot slot, Printer printer) => unit.Kind switch
    {
        FeedUnitKindCatalog.MultiMaterialUnit or FeedUnitKindCatalog.ExternalSpool => printer.FeedReadings
            .FirstOrDefault(r => r.Kind == unit.Kind && r.UnitIndex == unit.UnitIndex)?.Slots
            .FirstOrDefault(s => s.SlotPosition == slot.SlotPosition),
        // ponytail: the one external port (unit 0); the port the reloader replaces once a model has one per extruder
        FeedUnitKindCatalog.AutomaticFilamentReloader when unit.ArchivedAtUtc is null
            && unit.ReplacesFeedUnitId is not null && unit.FeedingSlotId == slot.Id => printer.FeedReadings
            .FirstOrDefault(r => r.Kind == FeedUnitKindCatalog.ExternalSpool)?.Slots is [var port, ..] ? port : null,
        _ => null,
    };

    /// <summary>
    /// research.md §9/§11 priority order: an explicit <see cref="FeedUnit.ReplacesFeedUnitId"/> declaration first, then an
    /// active Multi-Material Unit feeding the same extruder — the two share that extruder's one filament inlet.
    /// </summary>
    public static Guid? ResolveSupersededByFeedUnitId(FeedUnit unit, IReadOnlyList<FeedUnit> allUnits)
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

        var extruders = unit.FeedSlots.Select(s => s.ExtruderIndex).ToHashSet();
        return allUnits.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit
            && u.FeedSlots.Any(s => s.DeactivatedAtUtc is null && extruders.Contains(s.ExtruderIndex)))?.Id;
    }
}
