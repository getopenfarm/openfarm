using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Production;

/// <summary>Where a loaded spool is physically fed from — the one place both the readiness check and dispatch read it.</summary>
public sealed class FeedPlacementResolver(ISpoolRepository spools, IFeedUnitRepository feedUnits)
{
    /// <summary>
    /// The slot as the printer sees it: a multi-material unit's own slot, else the external port it is loaded through (an
    /// external spool, or a reloader's feeding slot). Null when the spool isn't loaded on a known slot.
    /// </summary>
    public async Task<FeedSlotRef?> ResolveAsync(Guid spoolId, CancellationToken cancellationToken)
    {
        if (await spools.GetByIdAsync(spoolId, cancellationToken) is not { FeedSlotId: { } slotId }) return null;
        var unit = await feedUnits.GetByFeedSlotIdAsync(slotId, cancellationToken);
        var slot = unit?.FeedSlots.FirstOrDefault(s => s.Id == slotId);
        if (unit is null || slot is null) return null;
        // ponytail: one external port (unit 0), as every catalogued model has one inlet; a reloader's port by the unit it
        // replaces once a model has an external port per extruder
        return unit.Kind == FeedUnitKindCatalog.MultiMaterialUnit
            ? new FeedSlotRef(unit.Kind, unit.UnitIndex, slot.SlotPosition)
            : new FeedSlotRef(FeedUnitKindCatalog.ExternalSpool, 0, 0);
    }
}
