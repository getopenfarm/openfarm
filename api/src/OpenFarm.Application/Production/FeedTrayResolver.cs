using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Production;

/// <summary>The inverse of <see cref="FeedPlacementResolver"/>: which spool is loaded on the slot a printer reports as active.</summary>
public sealed class FeedTrayResolver(ISpoolRepository spools, IFeedUnitRepository feedUnits)
{
    /// <summary>Null when the slot holds no known spool in the <see cref="SpoolState.Using"/> state.</summary>
    public async Task<Guid?> ResolveSpoolAsync(Guid printerId, FeedSlotRef activeSlot, CancellationToken cancellationToken)
    {
        FeedSlot? slot;
        if (!activeSlot.IsMultiMaterialUnit)
        {
            var units = await feedUnits.GetByPrinterIdAsync(printerId, cancellationToken);
            var external = units.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.ExternalSpool && u.UnitIndex == activeSlot.UnitIndex);
            // A reloader declared to replace the external port is what feeds this path, from the slot it is feeding; the external
            // slot it replaces is then not in use (ProductionFeedPath). With none selected the spool is unknown, not the replaced one.
            var reloader = units.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader && u.ArchivedAtUtc is null
                && u.ReplacesFeedUnitId is { } replaces && (external is null || replaces == external.Id));
            slot = reloader is not null
                ? reloader.FeedSlots.FirstOrDefault(s => s.Id == reloader.FeedingSlotId && s.DeactivatedAtUtc is null)
                : external?.FeedSlots.OrderBy(s => s.SlotPosition).FirstOrDefault();
        }
        else
        {
            var unit = await feedUnits.FindByPrinterKindUnitIndexAsync(printerId, activeSlot.Kind, activeSlot.UnitIndex, cancellationToken);
            slot = unit?.FeedSlots.FirstOrDefault(s => s.SlotPosition == activeSlot.SlotPosition);
        }

        return slot is null ? null : (await spools.FindUsingFeedSlotAsync(slot.Id, cancellationToken))?.Id;
    }
}
