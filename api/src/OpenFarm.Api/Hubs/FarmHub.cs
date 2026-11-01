using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Hubs;

[Authorize]
public sealed class FarmHub(FarmConnections connections, OpenFarmDbContext db) : Hub
{
    public const string Group = "farm";

    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var user)
            || !Guid.TryParse(Context.User?.FindFirst("sid")?.Value, out var session)
            || !long.TryParse(Context.User?.FindFirst("exp")?.Value, out var expiry)
            || !await db.Users.AnyAsync(u => u.Id == user && u.IsEnabled))
        {
            Context.Abort();
            return;
        }
        connections.All[Context.ConnectionId] = new(user, session, DateTimeOffset.FromUnixTimeSeconds(expiry));
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.All.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
}
