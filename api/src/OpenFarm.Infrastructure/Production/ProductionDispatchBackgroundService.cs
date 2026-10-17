using System.Threading.Channels;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
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

    public void RequestDispatchAttempt(Guid printerId)
    {
        // Keep one durable-in-this-process pending intent per printer. A full signal channel is
        // already guaranteed to wake the consumer, which drains pending intents after each signal.
        // This also permits startup reconciliation of fleets larger than the channel capacity.
        if (_pending.TryAdd(printerId, 0)) _channel.Writer.TryWrite(printerId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EnsureTenantInfoResolvedAsync(stoppingToken);
            using (var scope = CreateTenantScope())
                await scope.ServiceProvider.GetRequiredService<ProductionSnapshotReconciler>().ReconcileAsync(stoppingToken);
            await ReconcileAsync(stoppingToken);

            await foreach (var signal in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                foreach (var printerId in _pending.Keys)
                {
                    if (!_pending.TryRemove(printerId, out _)) continue;
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
        if (printer is null || printer.Status != PrinterStatus.Idle
            || (PrinterModelCatalog.SupportsLiveIntegration(printer.Brand) && !printer.IsReachable)
            || await jobRepository.GetActiveJobByDispatchedPrinterIdAsync(printerId, cancellationToken) is not null)
        {
            return;
        }

        var nozzleDiameterMm = await queryService.ResolvePrinterNozzleDiameterMmAsync(printer, cancellationToken);
        if (nozzleDiameterMm is null)
        {
            return;
        }

        var loadedMaterials = await queryService.ResolveLoadedMaterialsAsync(printer, cancellationToken);
        var candidates = await queryService.FilterReadyAsync(printer.Id, await queryService.GetUnassignedCandidatesAsync(cancellationToken), cancellationToken);
        var pinnedCandidates = await queryService.FilterReadyAsync(printer.Id, await queryService.GetPinnedCandidatesAsync(printer.Id, cancellationToken), cancellationToken);
        var jobId = ProductionDispatchMatcher.SelectNextJob(nozzleDiameterMm.Value, loadedMaterials, candidates, pinnedCandidates);
        if (jobId is null)
        {
            // FR-006b — backfill is only ever consulted once no finite (Unassigned or Pinned) job matched.
            await TryDispatchBackfillAsync(services, printer, nozzleDiameterMm.Value, loadedMaterials, queryService, jobRepository, cancellationToken);
            return;
        }

        // FR-007b/research.md R11 — only an Unassigned match can be contested by another idle
        // printer (a Pinned match has exactly one eligible printer by construction). If a simpler
        // idle printer also qualifies for this exact job, defer to it: skip dispatching here and
        // proactively wake that printer's own attempt instead, rather than leaving nothing to ever
        // trigger it.
        var unassignedMatch = candidates.FirstOrDefault(c => c.JobId == jobId.Value);
        if (unassignedMatch is not null)
        {
            var capabilityScore = printer.AmsUnits.Sum(u => u.Trays.Count);
            var otherQualifying = await queryService.GetOtherIdleQualifyingPrintersAsync(
                printer.Id, unassignedMatch.JobId, unassignedMatch.RequiredNozzleDiameterMm, unassignedMatch.FilamentRequirement, cancellationToken);

            if (!ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters(printer.Id, capabilityScore, otherQualifying))
            {
                foreach (var (otherPrinterId, _) in otherQualifying)
                {
                    RequestDispatchAttempt(otherPrinterId);
                }

                return;
            }
        }

        var job = await jobRepository.GetByIdAsync(jobId.Value, cancellationToken);
        if (job is null || job.Status != PrintJobStatus.Queued || job.DispatchedPrinterId is not null
            || (job.AssignmentMode == AssignmentMode.Pinned && job.PinnedPrinterId != printer.Id))
        {
            // Lost a race against another dispatch decision (or a manual pin/cancel) between the
            // candidate query above and this re-check — simply skip; the printer's own next trigger
            // (or this reconciliation pass on next boot) will try again against whatever is left.
            return;
        }

        var requirement = await requirementRepository.GetByIdAsync(job.PartRequirementId, cancellationToken);
        var libraryPart = requirement is not null && requirement.Quantity is null
            ? await partRepository.GetByIdAsync(requirement.PartId, cancellationToken) : null;
        var part = ProductionJobRequirements.ResolvePart(job, requirement, libraryPart);
        if (requirement is null || part is null || part.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete)
        {
            LogMissingDispatchTarget(_logger, job.Id);
            return;
        }

        var runRepository = services.GetRequiredService<IProductionRunRepository>();
        var run = await runRepository.GetByRunLineIdAsync(requirement.RunLineId, cancellationToken);
        var runLine = run?.Lines.FirstOrDefault(l => l.Id == requirement.RunLineId);
        if (runLine is null)
        {
            LogMissingDispatchTarget(_logger, job.Id);
            return;
        }

        await DispatchJobAsync(services, printer, job, part, nozzleDiameterMm.Value, runLine.RevisionMode, jobRepository, cancellationToken);
    }

    /// <summary>
    /// FR-006a/research.md R2 — only ever consulted once no finite job matched. Lazily creates
    /// exactly one <see cref="PrintJob"/> for the first backfill-eligible line/part still under its
    /// occupancy cap and cumulative-unit ceiling, then dispatches it exactly like any other matched
    /// job — never a batch, never queued ahead of real demand.
    /// </summary>
    private async Task TryDispatchBackfillAsync(
        IServiceProvider services, Printer printer, decimal nozzleDiameterMm,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> loadedMaterials, ProductionDispatchQueryService queryService,
        IPrintJobRepository jobRepository, CancellationToken cancellationToken)
    {
        var backfillCandidates = await queryService.GetBackfillCandidatesAsync(cancellationToken, printer.Id);
        var readyBackfill = new List<BackfillLineCandidate>();
        foreach (var candidate in backfillCandidates)
        {
            var candidateRequirement = await services.GetRequiredService<IPartRequirementRepository>().GetByIdAsync(candidate.PartRequirementId, cancellationToken);
            if (candidateRequirement is not null && await queryService.GetReadinessIssueAsync(printer.Id, candidateRequirement.PartId, candidate.FilamentRequirement, cancellationToken) is null)
                readyBackfill.Add(candidate);
        }
        backfillCandidates = readyBackfill;
        var requirementId = ProductionDispatchMatcher.SelectNextBackfillLine(nozzleDiameterMm, loadedMaterials, backfillCandidates);
        if (requirementId is null)
        {
            return;
        }

        var requirementRepository = services.GetRequiredService<IPartRequirementRepository>();
        var partRepository = services.GetRequiredService<IPartRepository>();
        var runRepository = services.GetRequiredService<IProductionRunRepository>();

        var requirement = await requirementRepository.GetByIdAsync(requirementId.Value, cancellationToken);
        var part = requirement is null ? null : await partRepository.GetByIdAsync(requirement.PartId, cancellationToken);
        if (requirement is null || part is null || part.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete)
        {
            return;
        }

        var run = await runRepository.GetByRunLineIdAsync(requirement.RunLineId, cancellationToken);
        var runLine = run?.Lines.FirstOrDefault(l => l.Id == requirement.RunLineId);
        if (runLine is null)
        {
            return;
        }

        var nextQueuePosition = (await jobRepository.GetMaxQueuePositionAsync(requirement.OrganizationId, cancellationToken) ?? 0) + 1024;
        var filamentSpecs = part.FilamentRequirements
            .Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.DetectedColorHex, r.NarrowFilamentVariantId, r.NarrowFilamentProfileId))
            .ToList();

        var newJob = PrintJob.Create(
            requirement.OrganizationId, requirement.Id, nextQueuePosition, part.UnitsPerPrint,
            runLine.DefaultAssignmentMode, runLine.DefaultPinnedPrinterId, part.RevisionHash, part.RevisionNumber, filamentSpecs);

        await services.GetRequiredService<ProductionSnapshotFactory>().CaptureAsync(newJob, part, cancellationToken);
        await jobRepository.AddAsync(newJob, cancellationToken);
        await jobRepository.SaveChangesAsync(cancellationToken);

        await DispatchJobAsync(services, printer, newJob, part, nozzleDiameterMm, runLine.RevisionMode, jobRepository, cancellationToken);
    }

    private async Task DispatchJobAsync(
        IServiceProvider services, Printer printer, PrintJob job, Part part, decimal nozzleDiameterMm, RevisionMode revisionMode,
        IPrintJobRepository jobRepository, CancellationToken cancellationToken)
    {
        if (part.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete) return;
        var effective = await services.GetRequiredService<ProductionJobRequirements>().ResolveAsync(job, part, cancellationToken);
        var currentPrinter = await services.GetRequiredService<IPrinterRepository>().GetByIdAsync(printer.Id, cancellationToken);
        if (currentPrinter is null || currentPrinter.Status != PrinterStatus.Idle || !currentPrinter.NozzleConfirmed
            || currentPrinter.ReportedNozzleDiameterMm != part.NozzleDiameterMm
            || await services.GetRequiredService<ProductionProfileReadiness>().GetIssueAsync(printer.Id, part, effective.Requirements, cancellationToken, effective.RevisionHash) is not null) return;
        if (revisionMode == RevisionMode.FollowLatest && !job.HasFilamentRequirementOverride)
        {
            job.SetFilamentRequirements(effective.Requirements, false);
            jobRepository.TrackNewFilamentConsumption(job.FilamentConsumption);
        }
        if (!PrinterModelCatalog.SupportsLiveIntegration(printer.Brand))
        {
            if (revisionMode == RevisionMode.FollowLatest)
            {
                job.ResetUnsentBackfillSnapshot(part);
                // The lazy backfill job was created from this revision in the same attempt.
                await services.GetRequiredService<ProductionSnapshotFactory>().CaptureAsync(job, part, cancellationToken);
            }
            // FR-012 manual-brand path: mark the assignment only — Status stays Queued until the
            // operator retrieves the file themselves and calls MarkPrintJobStartedCommand (T055).
            // research.md R6 — Follow-latest re-reads the Part's current revision immediately before
            // this dispatch and records it via PrintJob.Dispatch's overwrite overload; Snapshot keeps
            // whatever revision the job was created/queued with.
            if (revisionMode == RevisionMode.FollowLatest)
            {
                var partRepository = services.GetRequiredService<IPartRepository>();
                var freshPart = await partRepository.GetByIdAsync(part.Id, cancellationToken) ?? part;
                if (freshPart.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete) return;
                job.Dispatch(printer.Id, freshPart.RevisionHash, freshPart.RevisionNumber);
            }
            else
            {
                job.Dispatch(printer.Id);
            }

            await jobRepository.SaveChangesAsync(cancellationToken);
            await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
            return;
        }

        await DispatchToLiveIntegratedPrinterAsync(services, printer, job, part, nozzleDiameterMm, revisionMode, jobRepository, cancellationToken);
    }

    private async Task DispatchToLiveIntegratedPrinterAsync(
        IServiceProvider services, Printer printer, PrintJob job, Part part, decimal nozzleDiameterMm, RevisionMode revisionMode,
        IPrintJobRepository jobRepository, CancellationToken cancellationToken)
    {
        var connectionSecrets = services.GetRequiredService<IConnectionSecretRepository>();
        var secretProtector = services.GetRequiredService<ISecretProtector>();
        var connectorFactory = services.GetRequiredService<IPrinterConnectorFactory>();
        var fileResolver = services.GetRequiredService<IJobFileResolver>();
        var partRepository = services.GetRequiredService<IPartRepository>();

        var secret = await connectionSecrets.GetByPrinterSerialNumberAsync(printer.SerialNumber, cancellationToken);
        if (secret is null)
        {
            LogNoConnectionSecret(_logger, printer.Id);
            return;
        }

        // Follow-latest: re-read the Part's current revision immediately before send (research.md R6)
        // so the sliced/resolved output matches the revision the job is about to be dispatched with.
        var dispatchPart = revisionMode == RevisionMode.FollowLatest
            ? await partRepository.GetByIdAsync(part.Id, cancellationToken) ?? part
            : part;

        if (dispatchPart.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete) return;
        if (revisionMode == RevisionMode.FollowLatest && !job.HasFilamentRequirementOverride
            && !ProductionJobRequirements.FromPart(dispatchPart).SequenceEqual(job.FilamentConsumption.Select(c => c.Requirement))) return;
        if (revisionMode == RevisionMode.FollowLatest)
        {
            job.ResetUnsentBackfillSnapshot(dispatchPart);
            await services.GetRequiredService<ProductionSnapshotFactory>().CaptureAsync(job, dispatchPart, cancellationToken);
        }
        if (revisionMode == RevisionMode.FollowLatest)
        {
            job.Dispatch(printer.Id, dispatchPart.RevisionHash, dispatchPart.RevisionNumber);
        }
        else
        {
            job.Dispatch(printer.Id);
        }

        JobFileContent fileContent;
        string configurationStamp;
        var profileReadiness = services.GetRequiredService<ProductionProfileReadiness>();
        var materialRequirements = job.FilamentConsumption.Select(c => c.Requirement).ToList();
        try
        {
            configurationStamp = await profileReadiness.GetConfigurationStampAsync(printer.Id, job.GetProductionPart()!, materialRequirements, cancellationToken, job.FileRevisionHash);
            fileContent = await fileResolver.ResolveAsync(dispatchPart, job, printer.Id, nozzleDiameterMm, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Resolving/slicing the output failed before any dispatch was recorded — the job is
            // untouched (still Queued/Unassigned) and simply tried again on the next trigger.
            job.RevertDispatch();
            LogFileResolutionFailed(_logger, job.Id, ex);
            return;
        }

        // Slicing can take minutes: confirm fresh persisted inventory and nozzle state immediately before sending.
        IReadOnlyList<OpenFarm.Application.Profiles.ProductionFilamentSelection> loadedSelections;
        try
        {
            using var freshScope = CreateTenantScope();
            var currentPrinter = await freshScope.ServiceProvider.GetRequiredService<IPrinterRepository>().GetByIdAsync(printer.Id, cancellationToken);
            var freshJobs = freshScope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
            var persistedJob = await freshJobs.GetByIdAsync(job.Id, cancellationToken);
            var requirement = await freshScope.ServiceProvider.GetRequiredService<IPartRequirementRepository>().GetByIdAsync(job.PartRequirementId, cancellationToken);
            var run = requirement is null ? null : await freshScope.ServiceProvider.GetRequiredService<IProductionRunRepository>().GetByRunLineIdAsync(requirement.RunLineId, cancellationToken);
            var currentPart = revisionMode == RevisionMode.FollowLatest
                ? await freshScope.ServiceProvider.GetRequiredService<IPartRepository>().GetByIdAsync(dispatchPart.Id, cancellationToken)
                : job.GetProductionPart();
            if (run is null || run.Status is ProductionRunStatus.Cancelled or ProductionRunStatus.Closed
                || persistedJob is not null && (persistedJob.Status != PrintJobStatus.Queued || persistedJob.DispatchedAtUtc is not null)
                || currentPrinter is null || !currentPrinter.IsReachable || currentPrinter.Status != PrinterStatus.Idle || currentPart is null
                || revisionMode == RevisionMode.FollowLatest && (currentPart.RevisionHash != dispatchPart.RevisionHash || currentPart.UpdatedAtUtc != dispatchPart.UpdatedAtUtc
                    || !job.HasFilamentRequirementOverride && !ProductionJobRequirements.FromPart(currentPart).SequenceEqual(materialRequirements))
                || configurationStamp != await freshScope.ServiceProvider.GetRequiredService<ProductionProfileReadiness>()
                    .GetConfigurationStampAsync(printer.Id, job.GetProductionPart()!, materialRequirements, cancellationToken, job.FileRevisionHash))
            {
                job.RevertDispatch();
                return;
            }

            // The spools loaded right now are the ones this job draws from; deduction on the terminal transition uses them.
            loadedSelections = await freshScope.ServiceProvider.GetRequiredService<ProductionProfileReadiness>()
                .ResolveAsync(printer.Id, job.GetProductionPart()!, materialRequirements, cancellationToken, job.FileRevisionHash);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.RevertDispatch();
            LogFileResolutionFailed(_logger, job.Id, ex);
            return;
        }

        var fileUsage = JobFilamentUsageReader.Read(fileContent.Bytes);
        var lineUsage = JobFilamentUsageMatcher.Match(
            job.FilamentConsumption.Select(c => (c.MaterialType, c.DetectedColorHex)).ToList(), fileUsage.Usages);
        job.BindFilamentUsage(loadedSelections.Select((s, i) => ((Guid?)s.SpoolId, lineUsage[i].Grams,
            FilamentUsageCurve.FromExtrusion(fileUsage.Extrusion.Marks,
                lineUsage[i].Tools.Where(fileUsage.Extrusion.Tools.ContainsKey).Select(t => fileUsage.Extrusion.Tools[t])))).ToList());

        // Tell the printer which tray each filament of the file comes from; without it a Bambu prints from the external spool.
        var placementResolver = services.GetRequiredService<FeedPlacementResolver>();
        var mappedLines = new List<MappedLine>();
        for (var i = 0; i < loadedSelections.Count; i++)
        {
            if (await placementResolver.ResolveAsync(loadedSelections[i].SpoolId, cancellationToken) is not { } placement)
            {
                job.RevertDispatch();
                LogFeedMappingUnavailable(_logger, job.Id);
                return;
            }

            mappedLines.Add(new MappedLine(lineUsage[i].Tools, placement));
        }

        var feedMapping = PrintFeedMappingBuilder.Build(mappedLines, fileUsage.FilamentCount);
        if (feedMapping is null)
        {
            job.RevertDispatch();
            LogFeedMappingUnavailable(_logger, job.Id);
            return;
        }

        var details = new PrinterConnectionDetails(
            printer.Brand, printer.ModelCode, printer.IpAddress, printer.SerialNumber, secretProtector.Unprotect(secret.EncryptedAccessCode));

        await jobRepository.SaveChangesAsync(cancellationToken);

        try
        {
            var connector = connectorFactory.CreateConnector(details);
            using var stream = new MemoryStream(fileContent.Bytes);
            await connector.StartJobAsync(details, stream, fileContent.FileName, feedMapping, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} not sent: its filaments could not be mapped to the printer's feed (unknown spool slot, unreadable file, or AMS mixed with reloader) — reverted to Queued/Unassigned.")]
    private static partial void LogFeedMappingUnavailable(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Resolving job {JobId}'s sliceable output failed.")]
    private static partial void LogFileResolutionFailed(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending job {JobId} to printer {PrinterId} failed — reverted to Queued/Unassigned.")]
    private static partial void LogStartJobFailed(ILogger logger, Guid jobId, Guid printerId, Exception exception);
}
