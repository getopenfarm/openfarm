using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Production;

/// <summary>The inverse of <see cref="PrintFeedMappingBuilder"/>: which spool is loaded on the tray a printer reports as active.</summary>
public sealed class FeedTrayResolver(ISpoolRepository spools, IFeedUnitRepository feedUnits)
{
    private const int FirstSingleSlotUnit = 128;
    private const int ExternalTray = 254;

    /// <summary>Null when the tray holds no known spool in the <see cref="SpoolState.Using"/> state.</summary>
    public async Task<Guid?> ResolveSpoolAsync(Guid printerId, int trayId, CancellationToken cancellationToken)
    {
        FeedSlot? slot;
        if (trayId == ExternalTray)
        {
            var units = await feedUnits.GetByPrinterIdAsync(printerId, cancellationToken);
            var external = units.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.ExternalSpool);
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
            var single = trayId >= FirstSingleSlotUnit;
            var unit = await feedUnits.FindByPrinterKindUnitIndexAsync(
                printerId, FeedUnitKindCatalog.MultiMaterialUnit, single ? trayId : trayId / 4, cancellationToken);
            slot = unit?.FeedSlots.FirstOrDefault(s => s.SlotPosition == (single ? 0 : trayId % 4));
        }

        return slot is null ? null : (await spools.FindUsingFeedSlotAsync(slot.Id, cancellationToken))?.Id;
    }
}
