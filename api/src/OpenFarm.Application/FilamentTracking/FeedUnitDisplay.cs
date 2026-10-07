using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>Computed read-time-only fields (data-model.md's "Derived display fields") shared by <see cref="GetSpoolsQueryHandler"/>, <see cref="GetFeedUnitsQueryHandler"/>, and <see cref="LoadSpoolOntoFeedSlotCommandHandler"/>.</summary>
internal static class FeedUnitDisplay
{
    public static string UnitLabel(FeedUnit unit, string printerBrand) => unit.Kind switch
    {
        FeedUnitKindCatalog.MultiMaterialUnit => $"{FeedUnitKindCatalog.ResolveMultiMaterialUnitTerm(printerBrand)} {unit.UnitIndex + 1}",
        FeedUnitKindCatalog.ExternalSpool => "External Spool",
        FeedUnitKindCatalog.AutomaticFilamentReloader => unit.Name ?? "Automatic Filament Reloader",
        _ => unit.Kind,
    };

    public static string SlotLabel(FeedUnit unit, FeedSlot slot, string printerBrand) =>
        unit.Kind == FeedUnitKindCatalog.ExternalSpool
            ? UnitLabel(unit, printerBrand)
            : $"{UnitLabel(unit, printerBrand)} · Slot {slot.SlotPosition + 1}";

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
