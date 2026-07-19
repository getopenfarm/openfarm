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
public sealed partial class PartProcessingBackgroundService : BackgroundService, IPartProcessingQueue
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
        var workers = Enumerable.Range(0, _maxConcurrency)
            .Select(_ => ConsumeAsync(stoppingToken))
            .ToArray();
        await Task.WhenAll(workers);
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessWorkItemAsync(workItem, stoppingToken);
            }
            catch (Exception ex)
            {
                LogWorkItemFailed(_logger, workItem.JobId, ex);
            }
        }
    }

    private async Task ProcessWorkItemAsync(PartProcessingWorkItem workItem, CancellationToken stoppingToken)
    {
        using var scope = CreateTenantScope(workItem.OrganizationId);
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
        catch (Exception ex) when (ex is PartFileParsingFailedException or UnslicedProjectMaterialParsingFailedException)
        {
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
    private async Task RunEstimateContinuationAsync(PartProcessingWorkItem workItem, PartProcessingJob job, IServiceProvider services, CancellationToken stoppingToken)
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
        catch (SlicingFailedException ex)
        {
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

    private static async Task WriteFailureActivityLogAsync(
        IActivityLogWriter activityLogWriter, PartProcessingJob job, string eventType, CancellationToken cancellationToken,
        string entityType = "PartProcessingJob", string? entityId = null)
    {
        var (actorId, actorType) = job.RequestedByUserId is { } userId
            ? (userId, ActorType.Operator)
            : ((Guid?)null, ActorType.System);

        var entry = ActivityLogEntry.Create(
            job.OrganizationId, entityType, entityId ?? job.Id.ToString(), eventType, actorId, actorType);
        await activityLogWriter.WriteAsync(entry, cancellationToken);
    }

    /// <summary>Mirrors <c>PrinterConnectionBackgroundService.CreateTenantScope</c> exactly — this app is single-organization, so the tenant is always the well-known org.</summary>
    private IServiceScope CreateTenantScope(Guid organizationId)
    {
        var scope = _scopeFactory.CreateScope();
        var tenantStore = scope.ServiceProvider.GetRequiredService<IMultiTenantStore<AppTenantInfo>>();
        var tenantInfo = tenantStore.GetByIdentifierAsync(OrganizationTenantStore.StaticIdentifier).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("No organization found — FirstBootSeeder must run before part processing starts.");
        var contextSetter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        contextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(tenantInfo);
        return scope;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Part processing job {JobId} terminated unexpectedly.")]
    private static partial void LogWorkItemFailed(ILogger logger, Guid jobId, Exception exception);
}
