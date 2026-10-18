using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OpenFarm.Api.Hubs;

/// <summary>
/// Push-only real-time channel (contracts/farm-hub.md) — every authenticated client joins the
/// <see cref="Group"/> on connect; no client-invoked methods this phase.
/// </summary>
[Authorize]
public sealed class FarmHub : Hub
{
    public const string Group = "farm";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Group);
        await base.OnConnectedAsync();
    }
}
