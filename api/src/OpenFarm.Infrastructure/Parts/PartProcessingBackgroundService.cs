using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Bounded <see cref="Channel{T}"/> consumer for <see cref="PartProcessingWorkItem"/> — the
/// Processing → Ready for Review (milestone 1) transition, and, for a requires-slicing job, the
/// trailing milestone-2 estimate continuation that may target the job's own preview, an
/// already-confirmed <see cref="Part"/>, or nothing at all if discarded (research.md §3/§3b). Test-after (research.md §12).
/// </summary>
public sealed partial class PartProcessingBackgroundService : BackgroundService, IPartProcessingDispatcher
{
    private readonly Channel<PartProcessingWorkItem> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PartProcessingBackgroundService> _logger;
    private readonly int _maxConcurrency;

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

    private async Task ReconcileOrphanedWorkAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var jobRepository = services.GetRequiredService<IPartProcessingJobRepository>();
        var partRepository = services.GetRequiredService<IPartRepository>();
        var publisher = services.GetRequiredService<IPartEventPublisher>();
        var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();

        const string interruptedMessage = "Processing was interrupted by a server restart. Please re-upload this file.";

        foreach (var job in await jobRepository.ListOrphanedInFlightAsync(stoppingToken))
        {
            if (job.Status == PartProcessingJobStatus.ReadyForReview)
                job.MarkEstimateUnavailable("Estimation was interrupted by a server restart. Reprocess to retry.");
            else
                job.MarkFailed(interruptedMessage);
            await jobRepository.SaveChangesAsync(stoppingToken);
            await WriteFailureActivityLogAsync(activityLogWriter, job,
                job.Status == PartProcessingJobStatus.ReadyForReview ? "PartEstimateUnavailable" : "PartFileProcessingFailed", stoppingToken);
            await publisher.PartProcessingJobStatusChangedAsync(job, stoppingToken);
            LogOrphanedJobReconciled(_logger, job.Id);
        }

        foreach (var part in await partRepository.ListWithResolvingEstimateAsync(stoppingToken))
        {
            part.MarkEstimateUnavailable();
            await partRepository.SaveChangesAsync(stoppingToken);
            await WriteFailureActivityLogAsync(
                activityLogWriter, "PartEstimateUnavailable", "Part", part.Id.ToString(), stoppingToken);
            await publisher.PartEstimateUnavailableAsync(part.Id, stoppingToken);
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
        using var scope = _scopeFactory.CreateScope();
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
            await publisher.PartProcessingJobStatusChangedAsync(job, stoppingToken);
            return;
        }

        // Discard is valid while Processing too — a race may have landed while milestone 1 ran.
        job = await jobRepository.GetByIdAsync(workItem.JobId, stoppingToken);
        if (job is null || job.Status != PartProcessingJobStatus.Processing)
        {
            return;
        }

        if (job.Kind != PartProcessingJobKind.Add && job.PartId is { } sourcePartId)
        {
            var source = await services.GetRequiredService<IPartRepository>().GetByIdAsync(sourcePartId, stoppingToken);
            if (source is not null)
                materialResolution = materialResolution with
                {
                    FilamentRequirements = materialResolution.FilamentRequirements.Select(r =>
                    {
                        var previous = source.FilamentRequirements.FirstOrDefault(p => p.MaterialType == r.MaterialType
                            && p.DetectedColorHex == r.DetectedColorHex && p.DetectedSubBrand == r.DetectedSubBrand);
                        return r with { NarrowFilamentVariantId = previous?.NarrowFilamentVariantId, NarrowFilamentProfileId = previous?.NarrowFilamentProfileId };
                    }).ToList(),
                };
        }

        job.SetEstimation(materialResolution.Estimation is { } details && job.RequiresSlicing
            ? details with { Engine = job.SlicerEngineUsed?.ToString(), Version = job.SlicerVersionUsed } : materialResolution.Estimation);
        job.SetEstimationConfiguration(materialResolution.EstimationConfiguration is { } configuration
            ? System.Text.Json.JsonSerializer.Serialize(configuration) : null);
        job.SetMetadataWarnings(materialResolution.MetadataWarnings ?? []);
        job.MarkReadyForReview(
            materialResolution.NozzleDiameterMm,
            materialResolution.FilamentRequirements,
            materialResolution.PlateCount,
            materialResolution.ImmediateEstimate is { } estimate
                ? PartEstimateAvailability.From(estimate.EstimatedPrintTimeMinutes, estimate.EstimatedFilamentWeightGrams)
                : materialResolution.EstimateUnavailableReason is null ? EstimateStatus.Resolving : EstimateStatus.Unavailable,
            materialResolution.ImmediateEstimate?.EstimatedPrintTimeMinutes,
            materialResolution.ImmediateEstimate?.EstimatedFilamentWeightGrams,
            materialResolution.ImmediateEstimate?.Thumbnails ?? materialResolution.Thumbnails ?? [],
            materialResolution.ResolvedTargetMaterialType,
            materialResolution.MaterialMetadataComplete,
            materialResolution.EstimateUnavailableReason);
        await jobRepository.SaveChangesAsync(stoppingToken);
        await publisher.PartProcessingJobStatusChangedAsync(job, stoppingToken);

        if (materialResolution.ImmediateEstimate is not null || materialResolution.EstimateUnavailableReason is not null)
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
    private async Task RunEstimateContinuationAsync(PartProcessingWorkItem workItem, PartProcessingJob job, IServiceProvider services, CancellationToken stoppingToken)
    {
        PartEstimateResolution? resolution = null;
        string? unavailableReason = null;
        try
        {
            await using var stream = await services.GetRequiredService<IPartFileStorage>().OpenReadAsync(job.StoragePath, stoppingToken);
            resolution = await services.GetRequiredService<IPartFileProcessor>().ResolveEstimateAsync(job, stream, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            unavailableReason = $"Estimation unavailable: {ex.Message}";
        }

        // A fresh context observes confirmations/replacements made while the slicer was running.
        // If confirmation wins a concurrent preview write, retry against its confirmed Part.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var updateScope = _scopeFactory.CreateScope();
            var updateServices = updateScope.ServiceProvider;
            var jobs = updateServices.GetRequiredService<IPartProcessingJobRepository>();
            var parts = updateServices.GetRequiredService<IPartRepository>();
            var publisher = updateServices.GetRequiredService<IPartEventPublisher>();
            var activity = updateServices.GetRequiredService<IActivityLogWriter>();
            var current = await jobs.GetByIdAsync(workItem.JobId, stoppingToken);
            if (current is null) return;
            try
            {
                if (current.Status == PartProcessingJobStatus.ReadyForReview)
                {
                    if (resolution is not null)
                        current.ResolveEstimate(resolution.EstimatedPrintTimeMinutes, resolution.EstimatedFilamentWeightGrams, resolution.Thumbnails, resolution.FilamentGrams);
                    else
                        current.MarkEstimateUnavailable(unavailableReason!);
                    await jobs.SaveChangesAsync(stoppingToken);
                    if (resolution is null)
                        await WriteFailureActivityLogAsync(activity, current, "PartEstimateUnavailable", stoppingToken);
                    await publisher.PartProcessingJobStatusChangedAsync(current, stoppingToken);
                }
                else if (current.Status == PartProcessingJobStatus.Confirmed && current.PartId is { } partId)
                {
                    var part = await parts.GetByIdAsync(partId, stoppingToken);
                    if (part is null || part.ActiveProcessingJobId != current.Id) return;
                    if (resolution is not null)
                        part.ResolveEstimate(resolution.EstimatedPrintTimeMinutes, resolution.EstimatedFilamentWeightGrams, resolution.Thumbnails, resolution.FilamentGrams);
                    else
                        part.MarkEstimateUnavailable(unavailableReason);
                    await parts.SaveChangesAsync(stoppingToken);
                    if (resolution is not null)
                        await publisher.PartEstimateResolvedAsync(part.Id, stoppingToken);
                    else
                    {
                        await WriteFailureActivityLogAsync(activity, current, "PartEstimateUnavailable", stoppingToken, entityType: "Part", entityId: part.Id.ToString());
                        await publisher.PartEstimateUnavailableAsync(part.Id, stoppingToken);
                    }
                }
                return;
            }
            catch (PartConcurrencyConflictException) when (attempt == 0)
            {
                // Re-read current state rather than allowing old results to overwrite newer work.
            }
        }
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
            using var scope = _scopeFactory.CreateScope();
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
            await publisher.PartProcessingJobStatusChangedAsync(job, stoppingToken);
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
            activityLogWriter, eventType, entityType, entityId ?? job.Id.ToString(), cancellationToken, actorId, actorType);
    }

    /// <summary>Reconciliation has no originating job/user to attribute to — always a system actor.</summary>
    private static async Task WriteFailureActivityLogAsync(
        IActivityLogWriter activityLogWriter, string eventType,
        string entityType, string entityId, CancellationToken cancellationToken, Guid? actorId = null, ActorType actorType = ActorType.System)
    {
        var entry = ActivityLogEntry.Create(entityType, entityId, eventType, actorId, actorType);
        await activityLogWriter.WriteAsync(entry, cancellationToken);
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
