using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed partial class SessionCleanupService(IServiceScopeFactory scopes, ILogger<SessionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
                // Keep revoked sessions until absolute expiry for audit/reuse detection; credentials cascade.
                await db.BrowserSessions.Where(s => s.ExpiresAtUtc <= DateTimeOffset.UtcNow).ExecuteDeleteAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogCleanupFailed(logger, exception);
            }
        }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Expired session cleanup failed; retrying on the next scheduled pass.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
