using Microsoft.AspNetCore.SignalR;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Hubs;

public sealed class FarmHubFilamentTrackingEventPublisher(IHubContext<FarmHub> hubContext) : IFilamentTrackingEventPublisher
{
    // ponytail: nothing raises this today. The web refreshes spool weights on NotificationsChanged (a print closing is
    // what changes them) and each mutating screen refreshes itself. Raise it per handler (register, edit, load,
    // unload, usage) if multi-operator sessions need live cross-tab updates.
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
