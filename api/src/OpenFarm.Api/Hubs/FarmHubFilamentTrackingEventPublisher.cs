using Microsoft.AspNetCore.SignalR;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Hubs;

public sealed class FarmHubFilamentTrackingEventPublisher(IHubContext<FarmHub> hubContext) : IFilamentTrackingEventPublisher
{
    public Task SpoolStateChangedAsync(
        Guid organizationId,
        Guid spoolId,
        SpoolState state,
        decimal remainingWeightGrams,
        Guid? feedSlotId,
        CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync(
            "SpoolStateChanged",
            new { spoolId, state = state.ToString(), remainingWeightGrams, feedSlotId },
            cancellationToken);

    public Task FeedUnitAutoContinuedAsync(
        Guid organizationId,
        Guid feedUnitId,
        Guid finishedSpoolId,
        Guid continuedSpoolId,
        Guid continuedFeedSlotId,
        bool materialMismatch,
        CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync(
            "FeedUnitAutoContinued",
            new { feedUnitId, finishedSpoolId, continuedSpoolId, continuedFeedSlotId, materialMismatch },
            cancellationToken);

    public Task FeedUnitsChangedAsync(Guid organizationId, Guid printerId, CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync("FeedUnitsChanged", new { printerId }, cancellationToken);

    private IClientProxy Group(Guid organizationId) =>
        hubContext.Clients.Group(FarmHub.GroupName(organizationId.ToString()));
}
