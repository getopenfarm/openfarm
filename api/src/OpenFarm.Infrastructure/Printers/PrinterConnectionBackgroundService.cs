using System.Collections.Concurrent;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Multitenancy;

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

    // Resolved once and reused for every scope this service creates — see CreateTenantScope's
    // remarks for why. Written once from ExecuteAsync before any tenant-scoped work starts.
    private AppTenantInfo? _tenantInfo;

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
        try
        {
            await EnsureTenantInfoResolvedAsync(stoppingToken);
            await StartConnectionsForRegisteredPrintersAsync(stoppingToken);

            using var timer = new PeriodicTimer(_sweepInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepUnreachablePrintersAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown — PeriodicTimer.WaitForNextTickAsync(CancellationToken) throws
            // rather than returning false when the passed token (not the timer itself) is
            // cancelled. Left uncaught, this becomes an unhandled BackgroundService exception and
            // (with the default BackgroundServiceExceptionBehavior.StopHost) crashes the whole
            // host on every graceful shutdown instead of exiting cleanly.
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

        _ = RunConnectionLoopAsync(printerId, linkedCts);
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

    /// <summary>
    /// Resolves and caches the (single, static) organization's tenant info, once. Safe to call
    /// repeatedly/concurrently — cheap no-op once cached.
    /// </summary>
    private async Task EnsureTenantInfoResolvedAsync(CancellationToken cancellationToken)
    {
        if (_tenantInfo is not null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var tenantStore = scope.ServiceProvider.GetRequiredService<IMultiTenantStore<AppTenantInfo>>();
        _tenantInfo = await tenantStore.GetByIdentifierAsync(OrganizationTenantStore.StaticIdentifier)
            ?? throw new InvalidOperationException("No organization found — FirstBootSeeder must run before printer connections start.");
    }

    /// <summary>
    /// Creates a DI scope with the organization's tenant context pre-populated. Finbuckle's tenant
    /// resolution is HTTP-middleware-based (<c>UseMultiTenant()</c>), which never runs for a scope
    /// this background service creates itself — without this, every tenant-scoped EF query filter
    /// (e.g. Printers') throws a <see cref="NullReferenceException"/> against a null
    /// <c>TenantInfo</c>. This is Finbuckle's documented pattern for resolving tenant context outside
    /// the request pipeline (<see cref="IMultiTenantContextSetter"/>).
    ///
    /// Deliberately synchronous (relies on <see cref="EnsureTenantInfoResolvedAsync"/> having already
    /// cached <see cref="_tenantInfo"/>): the underlying accessor stores its value in an
    /// <see cref="System.Threading.AsyncLocal{T}"/>, and a value set inside a separately-awaited
    /// async helper does not flow back out to the caller once that helper's own internal await
    /// resumes on a captured continuation — only mutations made directly within the same
    /// synchronous continuation as their use are guaranteed visible.
    /// </summary>
    private IServiceScope CreateTenantScope()
    {
        var scope = _scopeFactory.CreateScope();
        var contextSetter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        contextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(_tenantInfo!);
        return scope;
    }

    private async Task StartConnectionsForRegisteredPrintersAsync(CancellationToken stoppingToken)
    {
        using var scope = CreateTenantScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();
        var printers = await repository.ListAsync(stoppingToken);
        foreach (var printer in printers)
        {
            StartConnection(printer.Id, stoppingToken);
        }
    }

    /// <summary>
    /// Owns <paramref name="ownedCts"/> for its full lifetime. Editing a printer's connection
    /// (<see cref="Application.Printers.EditPrinterConnectionCommandHandler"/>) calls
    /// <see cref="StopConnection"/> immediately followed by <see cref="StartConnection"/> with no
    /// synchronization between them, so by the time this loop's <c>finally</c> runs, a *newer* CTS
    /// may already occupy <paramref name="printerId"/>'s slot in <see cref="_activeConnections"/>.
    /// The identity-checked <c>Remove(KeyValuePair&lt;,&gt;)</c> below only tears down the dictionary
    /// entry (and disposes the CTS) if it's still this loop's own instance — never someone else's.
    /// </summary>
    private async Task RunConnectionLoopAsync(Guid printerId, CancellationTokenSource ownedCts)
    {
        var connectionToken = ownedCts.Token;
        try
        {
            await EnsureTenantInfoResolvedAsync(connectionToken);
            using var scope = CreateTenantScope();
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

            if (PrinterModelCatalog.TryResolve(printer.Brand, printer.ModelCode, out var model)
                && model!.CameraTransport == CameraTransport.BinaryImage)
            {
                _ = RunCameraLoopAsync(printerId, details, connectionToken);
            }

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
            var stillOwnsSlot = ((ICollection<KeyValuePair<Guid, CancellationTokenSource>>)_activeConnections)
                .Remove(new KeyValuePair<Guid, CancellationTokenSource>(printerId, ownedCts));
            if (stillOwnsSlot)
            {
                ownedCts.Dispose();
            }
        }
    }

    /// <summary>
    /// Runs alongside <see cref="RunConnectionLoopAsync"/> in its own DI scope/Printer instance —
    /// never sharing the MQTT loop's DbContext, since two loops concurrently mutating/saving the
    /// same tracked entity from the same scope isn't safe. Cancelled together with the MQTT loop via
    /// the shared <paramref name="connectionToken"/>.
    /// </summary>
    private async Task RunCameraLoopAsync(Guid printerId, PrinterConnectionDetails details, CancellationToken connectionToken)
    {
        try
        {
            using var scope = CreateTenantScope();
            var services = scope.ServiceProvider;
            var printerRepository = services.GetRequiredService<IPrinterRepository>();
            var cameraRelay = services.GetRequiredService<IPrinterCameraRelay>();

            var printer = await printerRepository.GetByIdAsync(printerId, connectionToken);
            if (printer is null)
            {
                return;
            }

            await foreach (var frame in cameraRelay.StreamFramesAsync(details, connectionToken))
            {
                await HandleEventAsync(printer, new PrinterConnectorEvent(DateTimeOffset.UtcNow, CameraFrameJpeg: frame), services, connectionToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown or StopConnection().
        }
        catch (Exception ex)
        {
            LogCameraLoopFailed(_logger, printerId, ex);
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
        using var scope = CreateTenantScope();
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Printer {PrinterId} camera loop terminated unexpectedly.")]
    private static partial void LogCameraLoopFailed(ILogger logger, Guid printerId, Exception exception);
}
