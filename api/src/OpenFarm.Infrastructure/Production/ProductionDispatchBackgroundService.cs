using System.Threading.Channels;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Multitenancy;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Bounded <see cref="Channel{T}"/> of printer ids (research.md R4) — mirrors
/// <c>PartProcessingBackgroundService</c>'s shape, but consumed by exactly one worker, never several:
/// a dispatch decision reads the whole org's Unassigned candidate pool and must not race a concurrent
/// decision for a different printer into double-booking the same job (see
/// <c>ProductionDispatchQueryService.GetUnassignedCandidatesAsync</c>'s own remarks on the
/// dispatch-in-flight window this closes). <see cref="PrintJob.Dispatch(Guid)"/>/<see cref="PrintJob.RevertDispatch"/>
/// bracket the connector call (constitution Durable Recovery guardrail): marked dispatched before
/// <see cref="IPrinterConnector.StartJobAsync"/> is attempted so a crash mid-call is caught by
/// <see cref="ReconcileAsync"/> on the next boot, then immediately reverted in place if the call itself
/// fails synchronously rather than waiting for that next boot. Test-after (research.md §12, matching
/// <c>PrinterConnectionBackgroundService</c>/<c>PartProcessingBackgroundService</c>'s own carve-out).
/// </summary>
public sealed partial class ProductionDispatchBackgroundService : BackgroundService, IProductionDispatcher
{
    private readonly Channel<Guid> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductionDispatchBackgroundService> _logger;
    private AppTenantInfo? _tenantInfo;

    public ProductionDispatchBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ProductionDispatchBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public void RequestDispatchAttempt(Guid printerId) => _channel.Writer.TryWrite(printerId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EnsureTenantInfoResolvedAsync(stoppingToken);
            await ReconcileAsync(stoppingToken);

            await foreach (var printerId in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessDispatchAttemptAsync(printerId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Ordinary host shutdown mid-attempt — nothing was left dispatch-in-flight by this
                    // path specifically (see ProcessDispatchAttemptAsync's own revert-on-failure), and
                    // anything a prior lifetime did leave in-flight is ReconcileAsync's job on next boot.
                }
                catch (Exception ex)
                {
                    LogDispatchAttemptFailed(_logger, printerId, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown while idling for the next channel item.
        }
    }

    /// <summary>
    /// Constitution Durable Recovery guardrail: any job left dispatch-in-flight (<c>DispatchedAtUtc</c>
    /// set, still <c>Queued</c>) from a prior process lifetime reverts to ordinary matching, then every
    /// already-<c>Idle</c> printer gets a fresh dispatch attempt queued so nothing sits stuck waiting for
    /// its next real status-change trigger.
    /// </summary>
    private async Task ReconcileAsync(CancellationToken stoppingToken)
    {
        using var scope = CreateTenantScope();
        var services = scope.ServiceProvider;
        var jobRepository = services.GetRequiredService<IPrintJobRepository>();
        var printerRepository = services.GetRequiredService<IPrinterRepository>();

        var inFlight = await jobRepository.ListDispatchInFlightAsync(stoppingToken);
        foreach (var job in inFlight)
        {
            job.RevertDispatch();
            LogDispatchReverted(_logger, job.Id);
        }

        if (inFlight.Count > 0)
        {
            await jobRepository.SaveChangesAsync(stoppingToken);
        }

        var printers = await printerRepository.ListAsync(stoppingToken);
        foreach (var printer in printers)
        {
            if (printer.Status == PrinterStatus.Idle)
            {
                RequestDispatchAttempt(printer.Id);
            }
        }
    }

    private async Task ProcessDispatchAttemptAsync(Guid printerId, CancellationToken cancellationToken)
    {
        using var scope = CreateTenantScope();
        var services = scope.ServiceProvider;
        var printerRepository = services.GetRequiredService<IPrinterRepository>();
        var jobRepository = services.GetRequiredService<IPrintJobRepository>();
        var requirementRepository = services.GetRequiredService<IPartRequirementRepository>();
        var partRepository = services.GetRequiredService<IPartRepository>();
        var queryService = services.GetRequiredService<ProductionDispatchQueryService>();

        var printer = await printerRepository.GetByIdAsync(printerId, cancellationToken);
        if (printer is null || printer.Status != PrinterStatus.Idle)
        {
            return;
        }

        var nozzleDiameterMm = await queryService.ResolvePrinterNozzleDiameterMmAsync(printer, cancellationToken);
        if (nozzleDiameterMm is null)
        {
            return;
        }

        var loadedMaterials = await queryService.ResolveLoadedMaterialsAsync(printer, cancellationToken);
        var candidates = await queryService.GetUnassignedCandidatesAsync(cancellationToken);
        var jobId = ProductionDispatchMatcher.SelectNextJob(nozzleDiameterMm.Value, loadedMaterials, candidates);
        if (jobId is null)
        {
            return;
        }

        var job = await jobRepository.GetByIdAsync(jobId.Value, cancellationToken);
        if (job is null || job.Status != PrintJobStatus.Queued || job.AssignmentMode != AssignmentMode.Unassigned || job.DispatchedPrinterId is not null)
        {
            // Lost a race against another dispatch decision (or a manual pin/cancel) between the
            // candidate query above and this re-check — simply skip; the printer's own next trigger
            // (or this reconciliation pass on next boot) will try again against whatever is left.
            return;
        }

        var requirement = await requirementRepository.GetByIdAsync(job.PartRequirementId, cancellationToken);
        var part = requirement is null ? null : await partRepository.GetByIdAsync(requirement.PartId, cancellationToken);
        if (requirement is null || part is null)
        {
            LogMissingDispatchTarget(_logger, job.Id);
            return;
        }

        if (!PrinterModelCatalog.SupportsLiveIntegration(printer.Brand))
        {
            // FR-012 manual-brand path: mark the assignment only — Status stays Queued until the
            // operator retrieves the file themselves and calls MarkPrintJobStartedCommand (T055).
            job.Dispatch(printer.Id);
            await jobRepository.SaveChangesAsync(cancellationToken);
            await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
            return;
        }

        await DispatchToLiveIntegratedPrinterAsync(services, printer, job, part, nozzleDiameterMm.Value, jobRepository, cancellationToken);
    }

    private async Task DispatchToLiveIntegratedPrinterAsync(
        IServiceProvider services, Printer printer, PrintJob job, Part part, decimal nozzleDiameterMm,
        IPrintJobRepository jobRepository, CancellationToken cancellationToken)
    {
        var connectionSecrets = services.GetRequiredService<IConnectionSecretRepository>();
        var secretProtector = services.GetRequiredService<ISecretProtector>();
        var connectorFactory = services.GetRequiredService<IPrinterConnectorFactory>();
        var fileResolver = services.GetRequiredService<IJobFileResolver>();

        var secret = await connectionSecrets.GetByPrinterSerialNumberAsync(printer.SerialNumber, cancellationToken);
        if (secret is null)
        {
            LogNoConnectionSecret(_logger, printer.Id);
            return;
        }

        JobFileContent fileContent;
        try
        {
            fileContent = await fileResolver.ResolveAsync(part, job, printer.Id, nozzleDiameterMm, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Resolving/slicing the output failed before any dispatch was recorded — the job is
            // untouched (still Queued/Unassigned) and simply tried again on the next trigger.
            LogFileResolutionFailed(_logger, job.Id, ex);
            return;
        }

        var details = new PrinterConnectionDetails(
            printer.Brand, printer.ModelCode, printer.IpAddress, printer.SerialNumber, secretProtector.Unprotect(secret.EncryptedAccessCode));

        job.Dispatch(printer.Id);
        await jobRepository.SaveChangesAsync(cancellationToken);

        try
        {
            var connector = connectorFactory.CreateConnector(details);
            using var stream = new MemoryStream(fileContent.Bytes);
            await connector.StartJobAsync(details, stream, fileContent.FileName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The connector never confirmed the send — revert immediately rather than leaving this
            // dispatch-in-flight until the next process restart's reconciliation pass.
            job.RevertDispatch();
            await jobRepository.SaveChangesAsync(cancellationToken);
            LogStartJobFailed(_logger, job.Id, printer.Id, ex);
            return;
        }

        await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
    }

    private static async Task PublishDispatchedAsync(IServiceProvider services, PrintJob job, Guid printerId, CancellationToken cancellationToken)
    {
        var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();
        var entry = ActivityLogEntry.Create(
            job.OrganizationId, entityType: "PrintJob", entityId: job.Id.ToString(),
            eventType: "PrintJobDispatched", actorId: null, actorType: ActorType.System);
        await activityLogWriter.WriteAsync(entry, cancellationToken);

        var publisher = services.GetRequiredService<IProductionEventPublisher>();
        await publisher.PrintJobDispatchedAsync(job.OrganizationId, job.Id, printerId, cancellationToken);
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
            ?? throw new InvalidOperationException("No organization found — FirstBootSeeder must run before dispatch starts.");
    }

    /// <summary>Mirrors <c>PartProcessingBackgroundService.CreateTenantScope</c> exactly — see its remarks.</summary>
    private IServiceScope CreateTenantScope()
    {
        var scope = _scopeFactory.CreateScope();
        var contextSetter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        contextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(_tenantInfo!);
        return scope;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Dispatch attempt for printer {PrinterId} terminated unexpectedly.")]
    private static partial void LogDispatchAttemptFailed(ILogger logger, Guid printerId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Print job {JobId} was left dispatch-in-flight by a previous process lifetime and reverted to Queued/Unassigned on startup.")]
    private static partial void LogDispatchReverted(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Print job {JobId} matched for dispatch but its PartRequirement/Part could not be found.")]
    private static partial void LogMissingDispatchTarget(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {PrinterId} has no connection secret — skipping dispatch attempt.")]
    private static partial void LogNoConnectionSecret(ILogger logger, Guid printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Resolving job {JobId}'s sliceable output failed.")]
    private static partial void LogFileResolutionFailed(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending job {JobId} to printer {PrinterId} failed — reverted to Queued/Unassigned.")]
    private static partial void LogStartJobFailed(ILogger logger, Guid jobId, Guid printerId, Exception exception);
}
