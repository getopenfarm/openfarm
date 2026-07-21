using System.Threading.Channels;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Multitenancy;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Bounded <see cref="Channel{T}"/> consumer for <see cref="PartProcessingWorkItem"/> — the
/// Processing → Ready for Review (milestone 1) transition, and, for a requires-slicing job, the
/// trailing milestone-2 estimate continuation that may target the job's own preview, an
/// already-confirmed <see cref="Part"/>, or nothing at all if discarded (research.md §3/§3b). Uses
/// the exact <see cref="IServiceScopeFactory"/> + <c>CreateTenantScope()</c> pattern
/// <c>PrinterConnectionBackgroundService</c> already established. Test-after (research.md §12).
/// </summary>
public sealed partial class PartProcessingBackgroundService : BackgroundService, IPartProcessingDispatcher
{
    private readonly Channel<PartProcessingWorkItem> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PartProcessingBackgroundService> _logger;
    private readonly int _maxConcurrency;
    private AppTenantInfo? _tenantInfo;

    public PartProcessingBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PartProcessingBackgroundService> logger, int maxConcurrency = 2)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maxConcurrency = maxConcurrency;
        _channel = Channel.CreateBounded<PartProcessingWorkItem>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public void Enqueue(PartProcessingWorkItem workItem) => _channel.Writer.TryWrite(workItem);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureTenantInfoResolvedAsync(stoppingToken);

        // The work queue is an in-memory Channel, never durable across restarts — anything left
        // Processing or ReadyForReview-with-a-Resolving-preview-estimate at this exact moment was
        // orphaned by a previous process lifetime ending mid-job (crash, restart, deploy), not
        // something this run enqueued. Reconcile once, before any real work starts, or those
        // rows sit stuck forever with the UI spinning and nothing left to ever pick them back up.
        await ReconcileOrphanedWorkAsync(stoppingToken);

        var workers = Enumerable.Range(0, _maxConcurrency)
            .Select(_ => ConsumeAsync(stoppingToken))
            .ToArray();
        await Task.WhenAll(workers);
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
            ?? throw new InvalidOperationException("No organization found — FirstBootSeeder must run before part processing starts.");
    }

    private async Task ReconcileOrphanedWorkAsync(CancellationToken stoppingToken)
    {
        using var scope = CreateTenantScope();
        var services = scope.ServiceProvider;
        var jobRepository = services.GetRequiredService<IPartProcessingJobRepository>();
        var partRepository = services.GetRequiredService<IPartRepository>();
        var publisher = services.GetRequiredService<IPartEventPublisher>();
        var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();

        const string interruptedMessage = "Processing was interrupted by a server restart. Please re-upload this file.";

        foreach (var job in await jobRepository.ListOrphanedInFlightAsync(stoppingToken))
        {
            job.MarkFailed(interruptedMessage);
            await jobRepository.SaveChangesAsync(stoppingToken);
            await WriteFailureActivityLogAsync(activityLogWriter, job, "PartFileProcessingFailed", stoppingToken);
            await publisher.PartProcessingJobStatusChangedAsync(job.OrganizationId, job, stoppingToken);
            LogOrphanedJobReconciled(_logger, job.Id);
        }

        foreach (var part in await partRepository.ListWithResolvingEstimateAsync(stoppingToken))
        {
            part.MarkEstimateUnavailable();
            await partRepository.SaveChangesAsync(stoppingToken);
            await WriteFailureActivityLogAsync(
                activityLogWriter, part.OrganizationId, "PartEstimateUnavailable", "Part", part.Id.ToString(), stoppingToken);
            await publisher.PartEstimateUnavailableAsync(part.OrganizationId, part.Id, stoppingToken);
            LogOrphanedEstimateReconciled(_logger, part.Id);
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var workItem in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessWorkItemAsync(workItem, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Ordinary host shutdown mid-item — not a real failure. The job/part this left
                    // behind stays in its in-flight status; ReconcileOrphanedWorkAsync resolves it
                    // (to Failed/Unavailable) the next time the host starts.
                }
                catch (Exception ex)
                {
                    LogWorkItemFailed(_logger, workItem.JobId, ex);
                    await TryMarkOrphanedWorkItemFailedAsync(workItem, ex, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ordinary host shutdown while idling for the next item — not a real failure. Left
            // uncaught, .NET's default HostOptions.BackgroundServiceExceptionBehavior (StopHost)
            // logs this as a fatal crash and tears down the whole host.
        }
    }

    private async Task ProcessWorkItemAsync(PartProcessingWorkItem workItem, CancellationToken stoppingToken)
    {
        using var scope = CreateTenantScope();
        var services = scope.ServiceProvider;
        var jobRepository = services.GetRequiredService<IPartProcessingJobRepository>();
        var storage = services.GetRequiredService<IPartFileStorage>();
        var processor = services.GetRequiredService<IPartFileProcessor>();
        var publisher = services.GetRequiredService<IPartEventPublisher>();
        var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();

        var job = await jobRepository.GetByIdAsync(workItem.JobId, stoppingToken);
        if (job is null || job.Status != PartProcessingJobStatus.Processing)
        {
            return;
        }

        PartMaterialResolution materialResolution;
        try
        {
            await using var stream = await storage.OpenReadAsync(job.StoragePath, stoppingToken);
            materialResolution = await processor.ResolveMaterialAsync(job, stream, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately catches everything (parsing failures, storage I/O errors reading the
            // uploaded file, anything else) rather than enumerating specific exception types —
            // anything that escapes here falls through to ConsumeAsync's log-only catch, leaving
            // the job stuck "Processing" forever since the frontend has no polling fallback.
            job.MarkFailed(ex.Message);
            await jobRepository.SaveChangesAsync(stoppingToken);
            await WriteFailureActivityLogAsync(activityLogWriter, job, "PartFileProcessingFailed", stoppingToken);
            await publisher.PartProcessingJobStatusChangedAsync(job.OrganizationId, job, stoppingToken);
            return;
        }

        // Discard is valid while Processing too — a race may have landed while milestone 1 ran.
        job = await jobRepository.GetByIdAsync(workItem.JobId, stoppingToken);
        if (job is null || job.Status != PartProcessingJobStatus.Processing)
        {
            return;
        }

        job.MarkReadyForReview(
            materialResolution.NozzleDiameterMm,
            materialResolution.FilamentRequirements,
            materialResolution.PlateCount,
            materialResolution.ImmediateEstimate is not null ? EstimateStatus.Available : EstimateStatus.Resolving,
            materialResolution.ImmediateEstimate?.EstimatedPrintTimeMinutes,
            materialResolution.ImmediateEstimate?.EstimatedFilamentWeightGrams,
            materialResolution.ImmediateEstimate?.Thumbnails ?? [],
            materialResolution.ResolvedTargetMaterialType);
        await jobRepository.SaveChangesAsync(stoppingToken);
        await publisher.PartProcessingJobStatusChangedAsync(job.OrganizationId, job, stoppingToken);

        if (materialResolution.ImmediateEstimate is not null)
        {
            // Already-sliced file — one atomic parse resolved everything.
            return;
        }

        await RunEstimateContinuationAsync(workItem, job, services, stoppingToken);
    }

    /// <summary>
    /// Milestone 2 (research.md §3b) — runs as a continuation of the same work item. Targets
    /// whichever of the job's own preview or the already-confirmed <see cref="Part"/> currently holds
    /// the truth by the time the slice finishes.
    /// </summary>
    private static async Task RunEstimateContinuationAsync(PartProcessingWorkItem workItem, PartProcessingJob job, IServiceProvider services, CancellationToken stoppingToken)
    {
        var jobRepository = services.GetRequiredService<IPartProcessingJobRepository>();
        var partRepository = services.GetRequiredService<IPartRepository>();
        var storage = services.GetRequiredService<IPartFileStorage>();
        var processor = services.GetRequiredService<IPartFileProcessor>();
        var publisher = services.GetRequiredService<IPartEventPublisher>();
        var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();

        PartEstimateResolution estimateResolution;
        try
        {
            await using var stream = await storage.OpenReadAsync(job.StoragePath, stoppingToken);
            estimateResolution = await processor.ResolveEstimateAsync(job, stream, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately catches everything, not just SlicingFailedException — the storage read
            // above can throw raw IOException/FileNotFoundException/UnauthorizedAccessException
            // (e.g. a misconfigured PartFileStorage:RootDirectory), and those must reach the job's
            // status too, or they escape to ConsumeAsync's log-only catch and stall the job forever.
            var current = await jobRepository.GetByIdAsync(workItem.JobId, stoppingToken);
            if (current is null)
            {
                return;
            }

            if (current.Status == PartProcessingJobStatus.ReadyForReview)
            {
                current.MarkFailed(ex.Message);
                await jobRepository.SaveChangesAsync(stoppingToken);
                await WriteFailureActivityLogAsync(activityLogWriter, current, "PartFileProcessingFailed", stoppingToken);
                await publisher.PartProcessingJobStatusChangedAsync(current.OrganizationId, current, stoppingToken);
            }
            else if (current.Status == PartProcessingJobStatus.Confirmed && current.PartId is { } confirmedPartId)
            {
                var part = await partRepository.GetByIdAsync(confirmedPartId, stoppingToken);
                if (part is not null)
                {
                    part.MarkEstimateUnavailable();
                    await partRepository.SaveChangesAsync(stoppingToken);
                    await WriteFailureActivityLogAsync(activityLogWriter, current, "PartEstimateUnavailable", stoppingToken, entityType: "Part", entityId: part.Id.ToString());
                    await publisher.PartEstimateUnavailableAsync(current.OrganizationId, part.Id, stoppingToken);
                }
            }

            return;
        }

        var refreshed = await jobRepository.GetByIdAsync(workItem.JobId, stoppingToken);
        if (refreshed is null)
        {
            return;
        }

        if (refreshed.Status == PartProcessingJobStatus.ReadyForReview)
        {
            refreshed.ResolveEstimate(estimateResolution.EstimatedPrintTimeMinutes, estimateResolution.EstimatedFilamentWeightGrams, estimateResolution.Thumbnails);
            await jobRepository.SaveChangesAsync(stoppingToken);
            await publisher.PartProcessingJobStatusChangedAsync(refreshed.OrganizationId, refreshed, stoppingToken);
        }
        else if (refreshed.Status == PartProcessingJobStatus.Confirmed && refreshed.PartId is { } partId)
        {
            var part = await partRepository.GetByIdAsync(partId, stoppingToken);
            if (part is not null)
            {
                part.ResolveEstimate(estimateResolution.EstimatedPrintTimeMinutes, estimateResolution.EstimatedFilamentWeightGrams, estimateResolution.Thumbnails);
                await partRepository.SaveChangesAsync(stoppingToken);
                await publisher.PartEstimateResolvedAsync(refreshed.OrganizationId, part.Id, stoppingToken);
            }
        }
        // Discarded — the result is simply dropped (best-effort cancellation was already attempted
        // by the Discard endpoint's caller; nothing left to write it to either way).
    }

    /// <summary>
    /// Last-resort safety net for exceptions that escape ProcessWorkItemAsync's own handling
    /// entirely (e.g. failing to even load the job/scope, or the failure-handling path itself
    /// throwing while persisting/publishing) — without this, such a job is left in a non-terminal
    /// status with nothing left to ever revisit it, since the in-memory queue is not durable across
    /// restarts and the frontend has no polling fallback. Best-effort: a second failure here is
    /// only logged, never rethrown, so it can't crash the host.
    /// </summary>
    private async Task TryMarkOrphanedWorkItemFailedAsync(PartProcessingWorkItem workItem, Exception originalException, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = CreateTenantScope();
            var services = scope.ServiceProvider;
            var jobRepository = services.GetRequiredService<IPartProcessingJobRepository>();
            var publisher = services.GetRequiredService<IPartEventPublisher>();
            var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();

            var job = await jobRepository.GetByIdAsync(workItem.JobId, stoppingToken);
            if (job is null || job.Status is not (PartProcessingJobStatus.Processing or PartProcessingJobStatus.ReadyForReview))
            {
                return;
            }

            job.MarkFailed($"Part processing failed unexpectedly: {originalException.Message}");
            await jobRepository.SaveChangesAsync(stoppingToken);
            await WriteFailureActivityLogAsync(activityLogWriter, job, "PartFileProcessingFailed", stoppingToken);
            await publisher.PartProcessingJobStatusChangedAsync(job.OrganizationId, job, stoppingToken);
            LogOrphanedWorkItemRecovered(_logger, job.Id);
        }
        catch (Exception ex)
        {
            LogWorkItemDoubleFault(_logger, workItem.JobId, ex);
        }
    }

    private static async Task WriteFailureActivityLogAsync(
        IActivityLogWriter activityLogWriter, PartProcessingJob job, string eventType, CancellationToken cancellationToken,
        string entityType = "PartProcessingJob", string? entityId = null)
    {
        var (actorId, actorType) = job.RequestedByUserId is { } userId
            ? (userId, ActorType.Operator)
            : ((Guid?)null, ActorType.System);

        await WriteFailureActivityLogAsync(
            activityLogWriter, job.OrganizationId, eventType, entityType, entityId ?? job.Id.ToString(), cancellationToken, actorId, actorType);
    }

    /// <summary>Reconciliation has no originating job/user to attribute to — always a system actor.</summary>
    private static async Task WriteFailureActivityLogAsync(
        IActivityLogWriter activityLogWriter, Guid organizationId, string eventType,
        string entityType, string entityId, CancellationToken cancellationToken, Guid? actorId = null, ActorType actorType = ActorType.System)
    {
        var entry = ActivityLogEntry.Create(organizationId, entityType, entityId, eventType, actorId, actorType);
        await activityLogWriter.WriteAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Creates a DI scope with the organization's tenant context pre-populated. Finbuckle's tenant
    /// resolution is HTTP-middleware-based (<c>UseMultiTenant()</c>), which never runs for a scope
    /// this background service creates itself — without this, every tenant-scoped EF query filter
    /// throws a <see cref="NullReferenceException"/> against a null <c>TenantInfo</c>. Mirrors
    /// <c>PrinterConnectionBackgroundService.CreateTenantScope</c> exactly: resolves the tenant once
    /// via <see cref="EnsureTenantInfoResolvedAsync"/> up front rather than doing a blocking DB call
    /// on every scope creation.
    /// </summary>
    private IServiceScope CreateTenantScope()
    {
        var scope = _scopeFactory.CreateScope();
        var contextSetter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        contextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(_tenantInfo!);
        return scope;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Part processing job {JobId} terminated unexpectedly.")]
    private static partial void LogWorkItemFailed(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Part processing job {JobId} was recovered by the last-resort safety net and marked failed.")]
    private static partial void LogOrphanedWorkItemRecovered(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Part processing job {JobId} could not be recovered by the last-resort safety net either — it may be stuck.")]
    private static partial void LogWorkItemDoubleFault(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Part processing job {JobId} was orphaned by a previous process lifetime and marked failed on startup.")]
    private static partial void LogOrphanedJobReconciled(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Part {PartId}'s estimate was orphaned by a previous process lifetime and marked unavailable on startup.")]
    private static partial void LogOrphanedEstimateReconciled(ILogger logger, Guid partId);
}
