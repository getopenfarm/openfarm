using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Dispatches on the target row's own Kind (research.md §13): <c>AutomaticFilamentReloader</c>
/// archives the unit and returns loaded spools to inventory (history and remaining weight preserved); <c>ExternalSpool</c> re-hides
/// it (idempotent, still subject to FR-025's safety net); any connector-derived kind is rejected
/// (FR-005a).
/// </summary>
public sealed class RemoveFeedUnitCommandHandler(
    IFeedUnitRepository feedUnitRepository,
    IFilamentTrackingEventPublisher eventPublisher, ISpoolRepository spoolRepository) : ICommandHandler<RemoveFeedUnitCommand, FeedUnit>
{
    public async Task<FeedUnit> HandleAsync(RemoveFeedUnitCommand command, CancellationToken cancellationToken)
    {
        var printerId = await feedUnitRepository.GetPrinterIdAsync(command.FeedUnitId, cancellationToken)
            ?? throw new FeedUnitNotFoundException(command.FeedUnitId);
        var unit = await feedUnitRepository.ExecuteAtomicallyAsync(async () =>
        {
            var unit = await feedUnitRepository.GetByIdAsync(command.FeedUnitId, cancellationToken)
                ?? throw new FeedUnitNotFoundException(command.FeedUnitId);

            switch (unit.Kind)
            {
                case FeedUnitKindCatalog.AutomaticFilamentReloader:
                    foreach (var spool in await spoolRepository.FindLoadedInFeedUnitAsync(unit.Id, cancellationToken))
                        spool.ReturnToStorage();
                    unit.Archive();
                    break;
                case FeedUnitKindCatalog.ExternalSpool:
                    unit.Hide();
                    break;
                default:
                    throw new NotAnAutomaticFilamentReloaderException(unit.Id, unit.Kind);
            }

            await feedUnitRepository.SaveChangesAsync(cancellationToken);
            return unit;
        }, cancellationToken, printerId);
        await eventPublisher.FeedUnitsChangedAsync(unit.PrinterId, cancellationToken);

        return unit;
    }
}
