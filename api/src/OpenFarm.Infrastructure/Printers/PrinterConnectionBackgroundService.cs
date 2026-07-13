using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Printers;

/// <summary>
/// Owns the per-printer <see cref="IPrinterConnector"/> connection lifecycle: one long-lived
/// consumption loop per registered printer, tracking <see cref="Printer.LastMessageUtc"/> on every
/// message and running the ~30s-threshold/5s-interval unreachable sweep (research.md §6). Any
/// connector-reported status reconciliation is logged directly via <see cref="IActivityLogWriter"/>
/// with <see cref="ActorType.System"/> since this runs outside any HTTP request — the
/// ActivityLogPostProcessor never fires here (plan.md's Security &amp; Multi-Tenancy decision).
/// </summary>
public sealed partial class PrinterConnectionBackgroundService : BackgroundService, IPrinterConnectionManager
{
    private static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultUnreachableThreshold = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PrinterConnectionBackgroundService> _logger;
    private readonly TimeSpan _sweepInterval;
    private readonly TimeSpan _unreachableThreshold;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeConnections = new();

    public PrinterConnectionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PrinterConnectionBackgroundService> logger,
        TimeSpan? sweepInterval = null,
        TimeSpan? unreachableThreshold = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _sweepInterval = sweepInterval ?? DefaultSweepInterval;
        _unreachableThreshold = unreachableThreshold ?? DefaultUnreachableThreshold;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StartConnectionsForRegisteredPrintersAsync(stoppingToken);

        using var timer = new PeriodicTimer(_sweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepUnreachablePrintersAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Starts a connection loop for a printer that wasn't already connected (e.g. one just
    /// registered). Safe to call repeatedly — a no-op if that printer is already connected.
    /// </summary>
    public void StartConnection(Guid printerId, CancellationToken cancellationToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_activeConnections.TryAdd(printerId, linkedCts))
        {
            linkedCts.Dispose();
            return;
        }

        _ = RunConnectionLoopAsync(printerId, linkedCts.Token);
    }

    /// <summary>Stops a printer's connection loop (e.g. it was just removed).</summary>
    public void StopConnection(Guid printerId)
    {
        if (_activeConnections.TryRemove(printerId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task StartConnectionsForRegisteredPrintersAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();
        var printers = await repository.ListAsync(stoppingToken);
        foreach (var printer in printers)
        {
            StartConnection(printer.Id, stoppingToken);
        }
    }

    private async Task RunConnectionLoopAsync(Guid printerId, CancellationToken connectionToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var printerRepository = services.GetRequiredService<IPrinterRepository>();
            var connectionSecrets = services.GetRequiredService<IConnectionSecretRepository>();
            var secretProtector = services.GetRequiredService<ISecretProtector>();
            var connector = services.GetRequiredService<IPrinterConnector>();

            var printer = await printerRepository.GetByIdAsync(printerId, connectionToken);
            if (printer is null)
            {
                return;
            }

            var secret = await connectionSecrets.GetByPrinterSerialNumberAsync(printer.SerialNumber, connectionToken);
            if (secret is null)
            {
                LogNoConnectionSecret(_logger, printerId);
                return;
            }

            var details = new PrinterConnectionDetails(
                printer.IpAddress,
                printer.SerialNumber,
                secretProtector.Unprotect(secret.EncryptedAccessCode));

            await foreach (var connectorEvent in connector.ConnectAsync(details, connectionToken))
            {
                await HandleEventAsync(printer, connectorEvent, services, connectionToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown or StopConnection().
        }
        catch (Exception ex)
        {
            LogConnectionLoopFailed(_logger, printerId, ex);
        }
        finally
        {
            if (_activeConnections.TryRemove(printerId, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    private static async Task HandleEventAsync(
        Printer printer,
        PrinterConnectorEvent connectorEvent,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var wasReachable = printer.IsReachable;
        printer.MarkReachable(connectorEvent.ReceivedAtUtc);

        var statusChanged = connectorEvent.GcodeState is not null && printer.ObserveStatus(connectorEvent.GcodeState);

        if (connectorEvent.Telemetry is { } telemetry)
        {
            printer.RecordTelemetry(
                telemetry.NozzleTempC,
                telemetry.BedTempC,
                telemetry.PrintProgressPercent,
                telemetry.CurrentStageLabel,
                telemetry.AmsUnits);
        }

        var printerRepository = services.GetRequiredService<IPrinterRepository>();
        await printerRepository.SaveChangesAsync(cancellationToken);

        var publisher = services.GetRequiredService<IPrinterEventPublisher>();

        if (statusChanged)
        {
            var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();
            var entry = ActivityLogEntry.Create(
                printer.OrganizationId,
                entityType: "Printer",
                entityId: printer.Id.ToString(),
                eventType: "PrinterStatusObserved",
                actorId: null,
                actorType: ActorType.System);
            await activityLogWriter.WriteAsync(entry, cancellationToken);
        }

        if (statusChanged || !wasReachable)
        {
            await publisher.PrinterStatusChangedAsync(
                printer.OrganizationId,
                printer.Id,
                printer.Status,
                printer.IsReachable,
                cancellationToken);
        }

        if (connectorEvent.Telemetry is not null)
        {
            await publisher.PrinterTelemetryUpdatedAsync(printer.OrganizationId, printer.Id, connectorEvent.Telemetry, cancellationToken);
        }

        if (connectorEvent.CameraFrameJpeg is { } frame)
        {
            await publisher.CameraFrameReceivedAsync(printer.OrganizationId, printer.Id, Convert.ToBase64String(frame), cancellationToken);
        }
    }

    private async Task SweepUnreachablePrintersAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var printerRepository = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPrinterEventPublisher>();
        var printers = await printerRepository.ListAsync(stoppingToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var summary in printers)
        {
            if (!summary.IsReachable || summary.LastMessageUtc is not { } lastMessageUtc)
            {
                continue;
            }

            if (now - lastMessageUtc > _unreachableThreshold)
            {
                var tracked = await printerRepository.GetByIdAsync(summary.Id, stoppingToken);
                if (tracked is null || !tracked.IsReachable)
                {
                    continue;
                }

                tracked.MarkUnreachable();
                await printerRepository.SaveChangesAsync(stoppingToken);
                await publisher.PrinterStatusChangedAsync(
                    tracked.OrganizationId,
                    tracked.Id,
                    tracked.Status,
                    tracked.IsReachable,
                    stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No connection secret found for printer {PrinterId}; not connecting.")]
    private static partial void LogNoConnectionSecret(ILogger logger, Guid printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Printer {PrinterId} connection loop terminated unexpectedly.")]
    private static partial void LogConnectionLoopFailed(ILogger logger, Guid printerId, Exception exception);
}
