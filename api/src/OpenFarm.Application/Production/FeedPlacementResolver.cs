using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Production;

/// <summary>Where a loaded spool is physically fed from — the one place both the readiness check and dispatch read it.</summary>
public sealed class FeedPlacementResolver(ISpoolRepository spools, IFeedUnitRepository feedUnits)
{
    /// <summary>Null when the spool isn't loaded on a known slot.</summary>
    public async Task<FeedPlacement?> ResolveAsync(Guid spoolId, CancellationToken cancellationToken)
    {
        if (await spools.GetByIdAsync(spoolId, cancellationToken) is not { FeedSlotId: { } slotId }) return null;
        var unit = await feedUnits.GetByFeedSlotIdAsync(slotId, cancellationToken);
        var slot = unit?.FeedSlots.FirstOrDefault(s => s.Id == slotId);
        if (unit is null || slot is null) return null;
        return new FeedPlacement(unit.Kind == FeedUnitKindCatalog.MultiMaterialUnit ? FeedPath.Ams : FeedPath.External, unit.UnitIndex, slot.SlotPosition);
    }
}
