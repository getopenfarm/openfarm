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

/// <summary>Checks sessions and read permissions for every event, cached briefly; no shared broadcast bypass.</summary>
public sealed class AuthorizedFarmClients(
    IHubContext<FarmHub> hub,
    IServiceScopeFactory scopes,
    FarmConnections connections,
    TimeSpan? accessCacheDuration = null) : IFarmEventClients
{
    public static string[] RequiredPermissions(string method) => method switch
    {
        "PrinterActivityChanged" or "PrinterStatusChanged" or "PrinterTelemetryUpdated" => ["printers:get-detail"],
        "ProductionRunStatusChanged" or "ProductionDemandChanged" => ["production-runs:view"],
        "PrintJobStatusChanged" or "PrintJobDispatched" or "PrintJobSendProgress" => ["print-jobs:view"],
        "SpoolStateChanged" => ["spools:list"],
        "FeedUnitAutoContinued" => ["feed-units:list", "spools:list"],
        "FeedUnitsChanged" => ["feed-units:list"],
        "PartProcessingJobStatusChanged" or "PartUpserted" or "PartEstimateResolved" or "PartEstimateUnavailable" or "PartDeleted" => ["parts:read"],
        "MaintenanceChanged" => ["maintenance:view"],
        "NotificationsChanged" => ["print-jobs:view", "spools:list", "filaments:list"],
        _ => [],
    };

    // ponytail: 10 s cache, so a revoked session, disabled user or removed permission can still receive events for up to
    // 10 s; evict on those changes if that ever matters. Without it every 1 Hz telemetry push queried each user.
    private readonly TimeSpan _accessCacheDuration = accessCacheDuration ?? TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset ExpiresUtc, HashSet<string>? Permissions)> _users = new();
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset ExpiresUtc, bool Active)> _sessions = new();

    public async Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        var required = RequiredPermissions(method);
        if (required.Length == 0 || connections.All.IsEmpty) return;
        var now = DateTimeOffset.UtcNow;
        AsyncServiceScope? scope = null;
        try
        {
            var eligible = new List<string>();
            foreach (var user in connections.All.ToArray().GroupBy(c => c.Value.UserId))
            {
                if (!_users.TryGetValue(user.Key, out var access) || access.ExpiresUtc <= now)
                {
                    scope ??= scopes.CreateAsyncScope();
                    var services = scope.Value.ServiceProvider;
                    var permissions = await services.GetRequiredService<OpenFarmDbContext>().Users
                        .AnyAsync(u => u.Id == user.Key && u.IsEnabled, cancellationToken)
                        ? (await services.GetRequiredService<PermissionService>().ResolveAsync(user.Key, cancellationToken))
                            .Permissions.ToHashSet(StringComparer.Ordinal)
                        : null;
                    _users[user.Key] = access = (now + _accessCacheDuration, permissions);
                }
                if (access.Permissions is null || !required.All(access.Permissions.Contains)) continue;
                foreach (var pair in user)
                {
                    if (pair.Value.ExpiresAtUtc <= now) continue;
                    if (!_sessions.TryGetValue(pair.Value.SessionId, out var session) || session.ExpiresUtc <= now)
                    {
                        scope ??= scopes.CreateAsyncScope();
                        var active = await scope.Value.ServiceProvider.GetRequiredService<BrowserSessionService>()
                            .IsActiveAsync(user.Key, pair.Value.SessionId, cancellationToken);
                        _sessions[pair.Value.SessionId] = session = (now + _accessCacheDuration, active);
                    }
                    if (session.Active) eligible.Add(pair.Key);
                }
            }
            if (eligible.Count > 0) await hub.Clients.Clients(eligible).SendCoreAsync(method, args, cancellationToken);
        }
        finally
        {
            if (scope is { } created) await created.DisposeAsync();
        }
    }
}
