using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OpenFarm.Api.Hubs;

/// <summary>
/// Push-only real-time channel (contracts/farm-hub.md) — clients join their organization's group
/// on connect (from the JWT's "org" claim, same resolution as REST/ActivityLogPostProcessor); no
/// client-invoked methods this phase.
/// </summary>
[Authorize]
public sealed class FarmHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var organizationId = Context.User?.FindFirst("org")?.Value;
        if (!string.IsNullOrEmpty(organizationId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(organizationId));
        }

        await base.OnConnectedAsync();
    }

    public static string GroupName(string organizationId) => $"org:{organizationId}";
}
