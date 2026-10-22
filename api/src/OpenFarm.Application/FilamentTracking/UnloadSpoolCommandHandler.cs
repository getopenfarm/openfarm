using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// FR-007 — return a loaded spool to storage (keeps RemainingWeightGrams), or finish one from a slot or from storage, writing off
/// what is left. 409 on a concurrent conflicting change.
/// </summary>
public sealed class UnloadSpoolCommandHandler(
    ISpoolRepository spoolRepository, IFeedUnitRepository feedUnitRepository, IFilamentTrackingEventPublisher events,
    ISpoolUsageRecordRepository usageRecordRepository) : ICommandHandler<UnloadSpoolCommand, Spool>
{
    public async Task<Spool> HandleAsync(UnloadSpoolCommand command, CancellationToken cancellationToken)
    {
        var spool = await spoolRepository.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);

        var unit = spool.FeedSlotId is { } slotId
            ? await feedUnitRepository.GetByFeedSlotIdAsync(slotId, cancellationToken) : null;
        if (unit?.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader && unit.FeedingSlotId == spool.FeedSlotId
            && unit.ArchivedAtUtc is null)
            unit.SelectFeedingSlot(null);
        switch (command.Action)
        {
            case UnloadSpoolAction.ReturnToStorage:
                spool.ReturnToStorage();
                break;
            case UnloadSpoolAction.MarkFinished:
                // Add, not AddAsync: the write-off commits with the spool below, so a stale row version is the spool's 409 and neither is saved.
                if (spool.MarkFinished(command.Reason) is { } writeOff)
                    usageRecordRepository.Add(writeOff);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }

        await spoolRepository.SaveChangesAsync(cancellationToken);

        if (unit is not null)
            await events.FeedUnitsChangedAsync(unit.PrinterId, cancellationToken);
        return spool;
    }
}
