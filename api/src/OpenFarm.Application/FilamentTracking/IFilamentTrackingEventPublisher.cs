using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Application-defined contract for pushing real-time filament-tracking events, implemented in
/// <c>OpenFarm.Api</c> over the existing <c>/hubs/farm</c> hub (contracts/filament-tracking-api.md's
/// SignalR section) — no new hub.
/// </summary>
public interface IFilamentTrackingEventPublisher
{
    Task SpoolStateChangedAsync(
        Guid organizationId,
        Guid spoolId,
        SpoolState state,
        decimal remainingWeightGrams,
        Guid? feedSlotId,
        CancellationToken cancellationToken);

    Task FeedUnitAutoContinuedAsync(
        Guid organizationId,
        Guid feedUnitId,
        Guid finishedSpoolId,
        Guid continuedSpoolId,
        Guid continuedFeedSlotId,
        bool materialMismatch,
        CancellationToken cancellationToken);

    Task FeedUnitsChangedAsync(
        Guid organizationId,
        Guid printerId,
        CancellationToken cancellationToken);
}
