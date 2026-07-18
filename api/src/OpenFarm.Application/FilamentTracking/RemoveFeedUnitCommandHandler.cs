using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Dispatches on the target row's own Kind (research.md §13): <c>AutomaticFilamentReloader</c>
/// deactivates the unit and its slots (spool state/history undisturbed); <c>ExternalSpool</c> re-hides
/// it (idempotent, still subject to FR-025's safety net); any connector-derived kind is rejected
/// (FR-005a).
/// </summary>
public sealed class RemoveFeedUnitCommandHandler(
    IFeedUnitRepository feedUnitRepository,
    IFilamentTrackingEventPublisher eventPublisher) : ICommandHandler<RemoveFeedUnitCommand, FeedUnit>
{
    public async Task<FeedUnit> HandleAsync(RemoveFeedUnitCommand command, CancellationToken cancellationToken)
    {
        var unit = await feedUnitRepository.GetByIdAsync(command.FeedUnitId, cancellationToken)
            ?? throw new FeedUnitNotFoundException(command.FeedUnitId);

        switch (unit.Kind)
        {
            case FeedUnitKindCatalog.AutomaticFilamentReloader:
                unit.DeactivateAllSlots();
                break;
            case FeedUnitKindCatalog.ExternalSpool:
                unit.Hide();
                break;
            default:
                throw new NotAnAutomaticFilamentReloaderException(unit.Id, unit.Kind);
        }

        await feedUnitRepository.SaveChangesAsync(cancellationToken);
        await eventPublisher.FeedUnitsChangedAsync(unit.OrganizationId, unit.PrinterId, cancellationToken);

        return unit;
    }
}
