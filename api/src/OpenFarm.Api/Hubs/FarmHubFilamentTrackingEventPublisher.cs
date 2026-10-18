using Microsoft.AspNetCore.SignalR;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Hubs;

public sealed class FarmHubFilamentTrackingEventPublisher(IHubContext<FarmHub> hubContext) : IFilamentTrackingEventPublisher
{
    public Task SpoolStateChangedAsync(
        Guid spoolId,
        SpoolState state,
        decimal remainingWeightGrams,
        Guid? feedSlotId,
        CancellationToken cancellationToken) =>
        Group.SendAsync(
            "SpoolStateChanged",
            new { spoolId, state = state.ToString(), remainingWeightGrams, feedSlotId },
            cancellationToken);

    public Task FeedUnitAutoContinuedAsync(
        Guid feedUnitId,
        Guid finishedSpoolId,
        Guid continuedSpoolId,
        Guid continuedFeedSlotId,
        bool materialMismatch,
        CancellationToken cancellationToken) =>
        Group.SendAsync(
            "FeedUnitAutoContinued",
            new { feedUnitId, finishedSpoolId, continuedSpoolId, continuedFeedSlotId, materialMismatch },
            cancellationToken);

    public Task FeedUnitsChangedAsync(Guid printerId, CancellationToken cancellationToken) =>
        Group.SendAsync("FeedUnitsChanged", new { printerId }, cancellationToken);

    private IClientProxy Group => hubContext.Clients.Group(FarmHub.Group);
}
