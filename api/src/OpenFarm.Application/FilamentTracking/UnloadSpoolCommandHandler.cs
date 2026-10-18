using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>FR-007 — return to storage (keeps RemainingWeightGrams) or mark finished. 409 on a concurrent conflicting change.</summary>
public sealed class UnloadSpoolCommandHandler(ISpoolRepository spoolRepository, IFeedUnitRepository feedUnitRepository, IFilamentTrackingEventPublisher events) : ICommandHandler<UnloadSpoolCommand, Spool>
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
                spool.MarkFinished();
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
