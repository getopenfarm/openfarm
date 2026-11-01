using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Hubs;

public interface IFarmEventClients : IClientProxy;

public sealed record FarmConnection(Guid UserId, Guid SessionId, DateTimeOffset ExpiresAtUtc);
public sealed class FarmConnections
{
    public ConcurrentDictionary<string, FarmConnection> All { get; } = new(StringComparer.Ordinal);
}

/// <summary>Rechecks current sessions and read permissions for every event; no shared broadcast bypass.</summary>
public sealed class AuthorizedFarmClients(IHubContext<FarmHub> hub, IServiceScopeFactory scopes, FarmConnections connections) : IFarmEventClients
{
    public static string[] RequiredPermissions(string method) => method switch
    {
        "PrinterActivityChanged" or "PrinterStatusChanged" or "PrinterTelemetryUpdated" or "CameraFrameReceived" => ["printers:get-detail"],
        "ProductionRunStatusChanged" or "ProductionDemandChanged" => ["production-runs:view"],
        "PrintJobStatusChanged" or "PrintJobDispatched" or "PrintJobSendProgress" => ["print-jobs:view"],
        "SpoolStateChanged" => ["spools:list"],
        "FeedUnitAutoContinued" => ["feed-units:list", "spools:list"],
        "FeedUnitsChanged" => ["feed-units:list"],
        "PartProcessingJobStatusChanged" or "PartUpserted" or "PartEstimateResolved" or "PartEstimateUnavailable" or "PartDeleted" => ["parts:read"],
        "NotificationsChanged" => ["print-jobs:view", "spools:list", "filaments:list"],
        _ => [],
    };

    public async Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        var required = RequiredPermissions(method);
        if (required.Length == 0 || connections.All.IsEmpty) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var permissionService = scope.ServiceProvider.GetRequiredService<PermissionService>();
        var sessionService = scope.ServiceProvider.GetRequiredService<BrowserSessionService>();
        var eligible = new List<string>();
        foreach (var user in connections.All.ToArray().GroupBy(c => c.Value.UserId))
        {
            if (!await db.Users.AnyAsync(u => u.Id == user.Key && u.IsEnabled, cancellationToken)) continue;
            var access = await permissionService.ResolveAsync(user.Key, cancellationToken);
            if (!required.All(p => access.Permissions.Contains(p, StringComparer.Ordinal))) continue;
            foreach (var pair in user)
                if (pair.Value.ExpiresAtUtc > DateTimeOffset.UtcNow && await sessionService.IsActiveAsync(user.Key, pair.Value.SessionId, cancellationToken)) eligible.Add(pair.Key);
        }
        if (eligible.Count > 0) await hub.Clients.Clients(eligible).SendCoreAsync(method, args, cancellationToken);
    }
}
