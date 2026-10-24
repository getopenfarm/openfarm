using Microsoft.Extensions.Logging;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Production;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Loads a registered RFID spool into the slot whose tray reads its tag, once each time the tag arrives there
/// (<see cref="FeedSlot.ObserveRfid"/>): the registered spool that sat in the slot goes back to storage, and the tagged
/// spool leaves any slot it was in. Runs after each telemetry flush under the printer's feed-path lock; when another
/// change wins the race, the next reading tries again.
/// </summary>
public sealed partial class RfidSpoolLinker(
    IFeedUnitRepository feedUnits, ISpoolRepository spools, IActivityLogWriter activityLog,
    IFilamentTrackingEventPublisher feedEvents, IProductionEventPublisher notifications, ILogger<RfidSpoolLinker> logger)
{
    public async Task LinkAsync(Printer printer, CancellationToken cancellationToken)
    {
        var units = await feedUnits.GetByPrinterIdAsync(printer.Id, cancellationToken);
        var readings = Readings(units, printer).ToList();
        var tags = readings.Select(r => r.Tray.RfidId).OfType<string>().ToList();
        var owners = tags.Count == 0 ? []
            : (await spools.FindByRfidIdsAsync(tags.Distinct().ToList(), cancellationToken)).ToDictionary(s => s.RfidId!);
        // A tag read in two slots at once is a mix-up, not a place: neither slot takes the spool.
        string? Registered(string? tag) => tag is not null && owners.ContainsKey(tag) && tags.Count(t => t == tag) == 1 ? tag : null;

        // Almost every reading is the same as the last one; only a change takes the lock.
        if (readings.All(r => Registered(r.Tray.RfidId) == r.Slot.LinkedRfidId)) return;

        var touched = new HashSet<Guid> { printer.Id };
        try
        {
            await feedUnits.ExecuteAtomicallyAsync(async () =>
            {
                foreach (var (_, slot, tray) in readings)
                {
                    if (!slot.ObserveRfid(tray.RfidId, Registered(tray.RfidId) is not null)) continue;
                    var spool = owners[tray.RfidId!];
                    if (spool.FeedSlotId == slot.Id) continue;
                    if (await spools.FindUsingFeedSlotAsync(slot.Id, cancellationToken) is { } occupant)
                    {
                        await TakeOutAsync(occupant, touched, cancellationToken);
                        await activityLog.WriteAsync(
                            ActivityLogEntry.Create("Spool", occupant.Id.ToString(), "SpoolReturnedToStorage", null, ActorType.System), cancellationToken);
                    }

                    if (spool.State == SpoolState.Using) await TakeOutAsync(spool, touched, cancellationToken);
                    await spools.SaveChangesAsync(cancellationToken); // frees the slot for the one-spool-per-slot index
                    spool.LoadOntoFeedSlot(slot.Id);
                    await spools.SaveChangesAsync(cancellationToken);
                    await activityLog.WriteAsync(
                        ActivityLogEntry.Create("Spool", spool.Id.ToString(), "SpoolLoadedFromRfid", null, ActorType.System), cancellationToken);
                }

                await feedUnits.SaveChangesAsync(cancellationToken);
                return true;
            }, cancellationToken, printer.Id);
        }
        catch (Exception ex) when (ex is SpoolConcurrencyConflictException or InvalidSpoolStateTransitionException)
        {
            LogLinkRaced(logger, printer.Id, ex);
            return;
        }

        foreach (var printerId in touched) await feedEvents.FeedUnitsChangedAsync(printerId, cancellationToken);
        await notifications.NotificationsChangedAsync(cancellationToken);
    }

    /// <summary>
    /// What each slot's tray reads now. Reloader slots are left out (their feeding slot can be an estimate, so a reading is
    /// never theirs), and so is an external spool a reloader replaced. A tray holding a spool but giving no tag is skipped
    /// rather than read as empty: an untagged spool, or a report that left the tag out, says nothing about the tag.
    /// </summary>
    internal static IEnumerable<(FeedUnit Unit, FeedSlot Slot, FeedSlotReading Tray)> Readings(IReadOnlyList<FeedUnit> units, Printer printer) =>
        from unit in units
        where unit.ArchivedAtUtc is null && unit.Kind != FeedUnitKindCatalog.AutomaticFilamentReloader
            && !units.Any(r => r.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader && r.ArchivedAtUtc is null && r.ReplacesFeedUnitId == unit.Id)
        from slot in unit.FeedSlots
        where slot.DeactivatedAtUtc is null
        let tray = FeedUnitDisplay.ResolveDetectedTray(unit, slot, printer)
        where tray is not null && (tray.Empty || tray.RfidId is not null)
        select (unit, slot, tray);

    private async Task TakeOutAsync(Spool spool, HashSet<Guid> touched, CancellationToken cancellationToken)
    {
        var unit = await feedUnits.GetByFeedSlotIdAsync(spool.FeedSlotId!.Value, cancellationToken);
        if (unit is { Kind: FeedUnitKindCatalog.AutomaticFilamentReloader, ArchivedAtUtc: null } && unit.FeedingSlotId == spool.FeedSlotId)
            unit.SelectFeedingSlot(null);
        spool.ReturnToStorage();
        if (unit is not null) touched.Add(unit.PrinterId);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Loading an RFID spool on printer {PrinterId} lost a race; the next reading tries again.")]
    private static partial void LogLinkRaced(ILogger logger, Guid printerId, Exception exception);
}
