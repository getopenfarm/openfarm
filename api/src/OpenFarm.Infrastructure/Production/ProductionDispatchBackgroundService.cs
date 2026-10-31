using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;
using System.Collections.Concurrent;
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
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Bounded <see cref="Channel{T}"/> of printer ids (research.md R4) — mirrors
/// <c>PartProcessingBackgroundService</c>'s shape, but consumed by exactly one worker, never several:
/// a dispatch decision reads the whole farm's Unassigned candidate pool and must not race a concurrent
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

    // Printers that deferred an unassigned match, keyed by the printer they deferred to. Only the worker loop touches it.
    private readonly Dictionary<Guid, HashSet<Guid>> _deferredTo = [];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductionDispatchBackgroundService> _logger;

    // A send that fails is tried again in place this many times, after these pauses, before the job is blocked and backs off.
    private static readonly TimeSpan[] DefaultSendRetryDelays = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
    private readonly IReadOnlyList<TimeSpan> _sendRetryDelays;

    /// <param name="sendRetryDelays">Test seam: the pauses between send attempts (one fewer than the attempts).</param>
    public ProductionDispatchBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ProductionDispatchBackgroundService> logger,
        IReadOnlyList<TimeSpan>? sendRetryDelays = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _sendRetryDelays = sendRetryDelays ?? DefaultSendRetryDelays;
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
            using (var scope = _scopeFactory.CreateScope())
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
                        await WakeDeferredAsync(printerId, stoppingToken);
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
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var jobRepository = services.GetRequiredService<IPrintJobRepository>();
        var printerRepository = services.GetRequiredService<IPrinterRepository>();

        var inFlight = await jobRepository.ListDispatchInFlightAsync(stoppingToken);
        foreach (var job in inFlight)
        {
            if (job.ObjectExecution is { StartRequested: true })
            {
                job.RecordBlock("Print start is unconfirmed. Check the printer before confirming that it did not start.", DateTimeOffset.UtcNow);
                continue;
            }
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
        using var scope = _scopeFactory.CreateScope();
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

        var nozzlesMm = await queryService.ResolvePrinterNozzlesMmAsync(printer, cancellationToken);
        if (nozzlesMm.Count == 0)
        {
            return;
        }

        var loadedMaterials = await queryService.ResolveLoadedMaterialsAsync(printer, cancellationToken);
        var candidates = await queryService.FilterReadyAsync(printer.Id, await queryService.GetUnassignedCandidatesAsync(cancellationToken), cancellationToken);
        var pinnedCandidates = await queryService.FilterReadyAsync(printer.Id, await queryService.GetPinnedCandidatesAsync(printer.Id, cancellationToken), cancellationToken);
        var jobId = ProductionDispatchMatcher.SelectNextJob(nozzlesMm, loadedMaterials, candidates, pinnedCandidates);
        if (jobId is null)
        {
            // FR-006b — backfill is only ever consulted once no finite (Unassigned or Pinned) job matched.
            await TryDispatchBackfillAsync(services, printer, nozzlesMm, loadedMaterials, queryService, jobRepository, cancellationToken);
            return;
        }

        // FR-007b/research.md R11 — only an Unassigned match can be contested by another idle
        // printer (a Pinned match has exactly one eligible printer by construction). If a simpler
        // idle printer also qualifies for this exact job, defer to it: skip dispatching here and
        // proactively wake that printer's own attempt instead, rather than leaving nothing to ever
        // trigger it. That printer may take an earlier job of its own instead; WakeDeferredAsync
        // then wakes this one again.
        var unassignedMatch = candidates.FirstOrDefault(c => c.JobId == jobId.Value);
        if (unassignedMatch is not null)
        {
            var capabilityScore = printer.FeedReadings.Where(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit).Sum(u => u.Slots.Count);
            var otherQualifying = await queryService.GetOtherIdleQualifyingPrintersAsync(
                printer.Id, unassignedMatch.JobId, unassignedMatch.RequiredNozzleDiameterMm, unassignedMatch.FilamentRequirement, cancellationToken);

            if (!ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters(printer.Id, capabilityScore, otherQualifying))
            {
                foreach (var (otherPrinterId, _) in otherQualifying)
                {
                    RequestDispatchAttempt(otherPrinterId);
                    if (!_deferredTo.TryGetValue(otherPrinterId, out var deferring))
                    {
                        _deferredTo[otherPrinterId] = deferring = [];
                    }

                    deferring.Add(printer.Id);
                }

                return;
            }
        }

        var job = await jobRepository.GetByIdAsync(jobId.Value, cancellationToken);
        if (job is null || job.Status != PrintJobStatus.Queued || job.PrinterId is not null
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

        var runLine = await services.GetRequiredService<IProductionDemandRepository>().GetAsync(requirement.RunLineId, cancellationToken);
        if (runLine is null || !await queryService.CanDispatchDemandAsync(runLine.Id, cancellationToken))
        {
            LogMissingDispatchTarget(_logger, job.Id);
            return;
        }

        await DispatchJobAsync(services, printer, job, part, runLine.RevisionMode, jobRepository, cancellationToken);
    }

    /// <summary>
    /// Once a printer that others deferred a match to holds a job, wakes them: the match may still be waiting (that printer took
    /// an earlier job of its own), and a manual printer gets no telemetry to wake it. Waking only once it holds a job means it
    /// no longer contends, so two printers never keep waking each other.
    /// </summary>
    private async Task WakeDeferredAsync(Guid printerId, CancellationToken cancellationToken)
    {
        if (!_deferredTo.TryGetValue(printerId, out var deferring))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
        if (await jobs.GetActiveJobByDispatchedPrinterIdAsync(printerId, cancellationToken) is null)
        {
            return;
        }

        _deferredTo.Remove(printerId);
        foreach (var deferringPrinterId in deferring)
        {
            RequestDispatchAttempt(deferringPrinterId);
        }
    }

    /// <summary>
    /// FR-006a/research.md R2 — only ever consulted once no finite job matched. Lazily creates
    /// exactly one <see cref="PrintJob"/> for the first backfill-eligible line/part still under its
    /// occupancy cap and cumulative-unit ceiling, then dispatches it exactly like any other matched
    /// job — never a batch, never queued ahead of real demand.
    /// </summary>
    private async Task TryDispatchBackfillAsync(
        IServiceProvider services, Printer printer, IReadOnlyList<decimal> nozzlesMm,
        IReadOnlyList<(string? MaterialType, string? ColorHex, decimal? NozzleMm)> loadedMaterials, ProductionDispatchQueryService queryService,
        IPrintJobRepository jobRepository, CancellationToken cancellationToken)
    {
        var retries = await queryService.FilterReadyAsync(printer.Id,
            await queryService.GetBackfillRetryCandidatesAsync(printer.Id, cancellationToken), cancellationToken);
        var retryId = ProductionDispatchMatcher.SelectNextJob(nozzlesMm, loadedMaterials, retries);
        if (retryId is { } waitingId)
        {
            var waiting = await jobRepository.GetByIdAsync(waitingId, cancellationToken);
            if (waiting is null) return;
            var req = await services.GetRequiredService<IPartRequirementRepository>().GetByIdAsync(waiting.PartRequirementId, cancellationToken);
            var current = req is null ? null : await services.GetRequiredService<IPartRepository>().GetByIdAsync(req.PartId, cancellationToken);
            if (current is not null) await DispatchJobAsync(services, printer, waiting, current, RevisionMode.FollowLatest, jobRepository, cancellationToken);
            return;
        }
        var backfillCandidates = await queryService.GetBackfillCandidatesAsync(cancellationToken, printer.Id);
        var readyBackfill = new List<BackfillLineCandidate>();
        foreach (var candidate in backfillCandidates)
        {
            var candidateRequirement = await services.GetRequiredService<IPartRequirementRepository>().GetByIdAsync(candidate.PartRequirementId, cancellationToken);
            if (candidateRequirement is not null && await queryService.GetReadinessIssueAsync(printer.Id, candidateRequirement.PartId, candidate.FilamentRequirement, cancellationToken) is null)
                readyBackfill.Add(candidate);
        }
        backfillCandidates = readyBackfill;
        var requirementId = ProductionDispatchMatcher.SelectNextBackfillLine(nozzlesMm, loadedMaterials, backfillCandidates);
        if (requirementId is null)
        {
            return;
        }

        var db = services.GetRequiredService<OpenFarm.Infrastructure.Persistence.OpenFarmDbContext>();
        await using var claim = await db.Database.BeginTransactionAsync(cancellationToken);
        var requirementRepository = services.GetRequiredService<IPartRequirementRepository>();
        var partRepository = services.GetRequiredService<IPartRepository>();
        var requirement = await requirementRepository.GetByIdAsync(requirementId.Value, cancellationToken);
        var part = requirement is null ? null : await partRepository.GetByIdAsync(requirement.PartId, cancellationToken);
        if (requirement is null || part is null || part.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete)
        {
            return;
        }

        var runLine = await services.GetRequiredService<IProductionDemandRepository>().GetAsync(requirement.RunLineId, cancellationToken);
        if (runLine is null || !await queryService.CanDispatchDemandAsync(runLine.Id, cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"RunLines\" WHERE \"Id\" = {runLine.Id} FOR UPDATE", cancellationToken);
        await db.Entry(runLine).ReloadAsync(cancellationToken);
        if (!await queryService.CanDispatchDemandAsync(runLine.Id, cancellationToken)) return;
        var freshCandidates = await queryService.GetBackfillCandidatesAsync(cancellationToken, printer.Id);
        if (!freshCandidates.Any(c => c.PartRequirementId == requirement.Id && c.CurrentOccupancy < c.BackfillPrinterCap)) return;
        var nextQueuePosition = (await jobRepository.GetMaxQueuePositionAsync(cancellationToken) ?? 0) + 1024;
        var filamentSpecs = part.FilamentRequirements
            .Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.DetectedColorHex, r.NarrowFilamentVariantId, r.NarrowFilamentProfileId))
            .ToList();

        var newJob = PrintJob.Create(
            part.Name, requirement.Id, nextQueuePosition, part.UnitsPerPrint,
            runLine.DefaultAssignmentMode, runLine.DefaultPinnedPrinterId, part.RevisionHash, part.RevisionNumber, filamentSpecs);

        await services.GetRequiredService<ProductionSnapshotFactory>().CaptureAsync(newJob, part, cancellationToken);
        await jobRepository.AddAsync(newJob, cancellationToken);
        await jobRepository.SaveChangesAsync(cancellationToken);
        await claim.CommitAsync(cancellationToken);

        await DispatchJobAsync(services, printer, newJob, part, runLine.RevisionMode, jobRepository, cancellationToken);
    }

    private async Task DispatchJobAsync(
        IServiceProvider services, Printer printer, PrintJob job, Part part, RevisionMode revisionMode,
        IPrintJobRepository jobRepository, CancellationToken cancellationToken)
    {
        if (part.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete) return;
        var effective = await services.GetRequiredService<ProductionJobRequirements>().ResolveAsync(job, part, cancellationToken);
        var quantityStorage = services.GetRequiredService<IPartFileStorage>();
        var quantityPath = effective.RevisionHash == part.RevisionHash ? part.StoragePath
            : await quantityStorage.GetRevisionStoragePathAsync(effective.RevisionHash, part.OriginalFileName, cancellationToken);
        var inventory = await PartObjectInventory.ReadAsync(quantityStorage, quantityPath, part.PlateNumber, cancellationToken);
        var expectedCapacity = revisionMode == RevisionMode.FollowLatest ? part.UnitsPerPrint : job.PlateQuantity;
        if (inventory.Count is { } count && count != expectedCapacity
            || job.ObjectExecution is { } legacy && legacy.Manifest.Units.Any(u => u.ObjectIds.Count != 1))
        {
            await BlockAsync(job, "The saved quantity or object grouping disagrees with this file. Reprocess the Part and recreate this job.", cancellationToken);
            return;
        }
        var currentPrinter = await services.GetRequiredService<IPrinterRepository>().GetByIdAsync(printer.Id, cancellationToken);
        if (currentPrinter is null || currentPrinter.Status != PrinterStatus.Idle
            || part.NozzleDiameterMm is not { } nozzleMm || !currentPrinter.ConfirmedNozzlesMm().Contains(nozzleMm)
            || await services.GetRequiredService<ProductionProfileReadiness>().GetIssueAsync(printer.Id, part, effective.Requirements, cancellationToken, effective.RevisionHash, job) is not null) return;
        if (revisionMode == RevisionMode.FollowLatest && !job.HasFilamentRequirementOverride)
        {
            job.SetFilamentRequirements(effective.Requirements, false);
            jobRepository.TrackNewFilamentConsumption(job.Filaments);
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

            job.ClearBlock();
            await jobRepository.SaveChangesAsync(cancellationToken);
            await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
            return;
        }

        var sent = false;
        try
        {
            sent = await DispatchToLiveIntegratedPrinterAsync(services, printer, job, part, nozzleMm, revisionMode, jobRepository, cancellationToken);
        }
        finally
        {
            // Whatever stopped this attempt, pages showing its progress go back to the job's own state.
            if (!sent)
            {
                try
                {
                    await services.GetRequiredService<IProductionEventPublisher>()
                        .PrintJobSendProgressAsync(job.Id, PrintSendStage.Ended, null, 1, null, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { LogSendProgressFailed(_logger, job.Id, ex); }
            }
        }
    }

    /// <returns>Whether the job was sent.</returns>
    private async Task<bool> DispatchToLiveIntegratedPrinterAsync(
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
            await BlockAsync(job, $"Printer '{printer.Name}' has no saved access code. Enter the access code in the printer settings.", cancellationToken);
            return false;
        }

        // Follow-latest: re-read the Part's current revision immediately before send (research.md R6)
        // so the sliced/resolved output matches the revision the job is about to be dispatched with.
        var dispatchPart = revisionMode == RevisionMode.FollowLatest
            ? await partRepository.GetByIdAsync(part.Id, cancellationToken) ?? part
            : part;

        if (dispatchPart.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete) return false;
        if (revisionMode == RevisionMode.FollowLatest && !job.HasFilamentRequirementOverride
            && !ProductionJobRequirements.FromPart(dispatchPart).SequenceEqual(job.Filaments.Select(c => c.Requirement))) return false;
        if (revisionMode == RevisionMode.FollowLatest)
        {
            job.ResetUnsentBackfillSnapshot(dispatchPart);
            var manifest = await services.GetRequiredService<ProductionPlanner>().ReadObjectsAsync(dispatchPart, cancellationToken);
            job.SetExactTarget(dispatchPart.UnitsPerPrint, manifest);
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
        var materialRequirements = job.Filaments.Select(c => c.Requirement).ToList();
        var eventPublisher = services.GetRequiredService<IProductionEventPublisher>();
        await eventPublisher.PrintJobSendProgressAsync(job.Id, PrintSendStage.Slicing, null, 1, null, cancellationToken);
        try
        {
            // One resolution produces both the file and the stamp of the exact configuration it was made for.
            fileContent = await fileResolver.ResolveAsync(dispatchPart, job, printer.Id, nozzleDiameterMm, cancellationToken);
            configurationStamp = fileContent.ConfigurationStamp
                ?? await profileReadiness.GetConfigurationStampAsync(printer.Id, job.GetProductionPart()!, materialRequirements, cancellationToken, job.FileRevisionHash, job);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Resolving/slicing the output failed before any dispatch was recorded — nothing was sent. The job stays
            // Queued/Unassigned, says why, and is tried again after a growing pause.
            LogFileResolutionFailed(_logger, job.Id, ex);
            await BlockAsync(job, Describe(ex), cancellationToken);
            return false;
        }

        // Slicing can take minutes: confirm fresh persisted inventory and nozzle state immediately before sending.
        IReadOnlyList<OpenFarm.Application.Profiles.ProductionFilamentSelection> loadedSelections;
        try
        {
            using var freshScope = _scopeFactory.CreateScope();
            var currentPrinter = await freshScope.ServiceProvider.GetRequiredService<IPrinterRepository>().GetByIdAsync(printer.Id, cancellationToken);
            var freshJobs = freshScope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
            var persistedJob = await freshJobs.GetByIdAsync(job.Id, cancellationToken);
            var requirement = await freshScope.ServiceProvider.GetRequiredService<IPartRequirementRepository>().GetByIdAsync(job.PartRequirementId, cancellationToken);
            var demandAllowed = requirement is not null && await freshScope.ServiceProvider.GetRequiredService<IProductionDemandRepository>().CanDispatchAsync(requirement.RunLineId, cancellationToken);
            var currentPart = revisionMode == RevisionMode.FollowLatest
                ? await freshScope.ServiceProvider.GetRequiredService<IPartRepository>().GetByIdAsync(dispatchPart.Id, cancellationToken)
                : job.GetProductionPart();
            if (!demandAllowed
                || persistedJob is not null && (persistedJob.Status != PrintJobStatus.Queued || persistedJob.DispatchedAtUtc is not null)
                || currentPrinter is null || !currentPrinter.IsReachable || currentPrinter.Status != PrinterStatus.Idle || currentPart is null
                || revisionMode == RevisionMode.FollowLatest && (currentPart.RevisionHash != dispatchPart.RevisionHash || currentPart.UpdatedAtUtc != dispatchPart.UpdatedAtUtc
                    || !job.HasFilamentRequirementOverride && !ProductionJobRequirements.FromPart(currentPart).SequenceEqual(materialRequirements)))
            {
                job.RevertDispatch();
                return false;
            }

            // The spools loaded right now are the ones this job draws from; deduction on the terminal transition uses them.
            var freshReadiness = freshScope.ServiceProvider.GetRequiredService<ProductionProfileReadiness>();
            var fresh = await freshReadiness.ResolveAsync(printer.Id, job.GetProductionPart()!, materialRequirements, cancellationToken, job.FileRevisionHash, job);
            if (configurationStamp != await freshReadiness.StampAsync(printer.Id, fresh, cancellationToken))
            {
                // The printer, spools or presets changed while the file was being made: what was made is not what is current.
                job.RevertDispatch();
                await BlockAsync(job, "Printer settings changed during slicing. The job will be tried again.", cancellationToken);
                return false;
            }

            loadedSelections = fresh.Selections;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogFileResolutionFailed(_logger, job.Id, ex);
            await BlockAsync(job, Describe(ex), cancellationToken);
            return false;
        }

        var fileUsage = job.ObjectExecution is { } objectExecution
            ? JobFilamentUsageReader.ReadForExecution(fileContent.Bytes, fileContent.Plate, objectExecution)
            : JobFilamentUsageReader.Read(fileContent.Bytes, fileContent.Plate);
        var lineUsage = JobFilamentUsageMatcher.Match(
            job.Filaments.Select(c => (c.MaterialType, c.DetectedColorHex)).ToList(), fileUsage.Usages);

        // The exact grams exist only now, sliced for this printer. A job they no longer fit is not sent; its grams are kept, so
        // the next attempt checks them before slicing again and a printer that has enough takes it.
        if (FilamentCover.FirstShortfall(loadedSelections
                .Select((s, i) => (s.FeedUnitId, s.FilamentVariantId, s.AvailableGrams, lineUsage[i].Grams)).ToList()) is { } shortfall)
        {
            job.RevertDispatch();
            var reason = FilamentShortfall.Of(loadedSelections[shortfall.Line], shortfall.Estimated, shortfall.Needed, shortfall.Available)
                .Describe(printer.Name);
            await BlockAsync(job, reason, cancellationToken, lineUsage.Select(u => u.Grams).ToList());
            return false;
        }
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
                await BlockAsync(job, FeedMappingReason, cancellationToken);
                return false;
            }

            mappedLines.Add(new MappedLine(lineUsage[i].Tools, placement));
        }

        var feedMapping = PrintFeedMappingBuilder.Build(mappedLines, fileUsage.FilamentCount);
        if (feedMapping is null)
        {
            job.RevertDispatch();
            LogFeedMappingUnavailable(_logger, job.Id);
            await BlockAsync(job, FeedMappingReason, cancellationToken);
            return false;
        }

        var details = new PrinterConnectionDetails(
            printer.Brand, printer.ModelCode, printer.IpAddress, printer.SerialNumber, secretProtector.Unprotect(secret.EncryptedAccessCode));

        // What this file was made with is recorded with the dispatch itself — in the log and on the job.
        if (fileContent.Profiles is { } sliced)
        {
            job.RecordDispatchProvenance(DispatchProvenance.From(sliced, configurationStamp, DateTimeOffset.UtcNow).ToJson());
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var filaments = string.Join(", ", (sliced.Filaments.Count == 0 ? [sliced.Filament] : sliced.Filaments.Select(f => f.Preset)).Distinct());
                LogSlicingProfilesUsed(_logger, job.Id, printer.Id, sliced.Pin.Engine, sliced.Pin.Version, sliced.Machine, sliced.Process, filaments);
            }
        }

        await jobRepository.SaveChangesAsync(cancellationToken);

        var connector = connectorFactory.CreateConnector(details);
        for (var attempt = 1; ; attempt++)
        {
            var progress = new SendProgressRelay(eventPublisher, job.Id, attempt);
            try
            {
                using var stream = new MemoryStream(fileContent.Bytes);
                if (job.ObjectExecution is { } execution)
                {
                    if (execution.PlannedExclusions.Count > 0)
                        services.GetRequiredService<ObjectSkippingPolicy>().EnsureVerified(details, live: false);
                    var extension = fileContent.FileName.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) ? ".gcode.3mf" : ".gcode";
                    var description = fileContent.FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                        ? fileContent.FileName[..^extension.Length] : Path.GetFileNameWithoutExtension(fileContent.FileName);
                    // Keep the human description first and the correlation ID inside the upload stem's 100-character limit.
                    var artifactName = $"{description[..Math.Min(description.Length, 45)]}-{job.Id:N}{extension}";
                    var hash = ObjectExecutionArtifact.Hash(fileContent.Bytes);
                    using var retained = new MemoryStream(fileContent.Bytes);
                    var path = await services.GetRequiredService<IPartFileStorage>().SaveAsync(hash, artifactName, retained, cancellationToken);
                    job.RecordObjectExecution(execution with { ArtifactPath = path, ArtifactHash = hash, ArtifactName = artifactName, StartRequested = true });
                    await jobRepository.SaveChangesAsync(cancellationToken);
                    var exclusions = execution.Manifest.Dialect == "Bambu" ? execution.PlannedExclusions : [];
                    await connector.StartSelectedJobAsync(details, stream, artifactName, fileContent.Plate, feedMapping, exclusions, progress, cancellationToken);
                }
                else await connector.StartJobAsync(details, stream, fileContent.FileName, fileContent.Plate, feedMapping, progress, cancellationToken);
                await progress.FlushAsync();
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await progress.FlushAsync();
                // A lost start acknowledgement must not resend an exact-quantity plate, even after restart.
                if (job.ObjectExecution is { StartRequested: true })
                {
                    job.RecordBlock("Print start is unconfirmed. Check the printer before confirming that it did not start.", DateTimeOffset.UtcNow);
                    await jobRepository.SaveChangesAsync(cancellationToken);
                    await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
                    LogStartJobFailed(_logger, job.Id, printer.Id, ex);
                    return false;
                }
                // A blip (Wi-Fi, a busy broker) is retried here instead of costing a backoff and a new slice — but only while the
                // printer is still idle: one that started meanwhile may have taken this very send, and it must not get it twice.
                // ponytail: the pause holds the single dispatch loop (~40 s worst case), so other printers wait too; move retries
                // onto a per-printer task if a farm ever feels it.
                if (attempt <= _sendRetryDelays.Count)
                {
                    LogSendRetrying(_logger, job.Id, printer.Id, attempt, ex);
                    await eventPublisher.PrintJobSendProgressAsync(job.Id, PrintSendStage.Retrying, null, attempt + 1, Truncate(ex.Message), cancellationToken);
                    await Task.Delay(_sendRetryDelays[attempt - 1], cancellationToken);
                    if (await IsStillIdleAsync(printer.Id, cancellationToken)) continue;
                }

                // The connector never confirmed the send — revert immediately rather than leaving this
                // dispatch-in-flight until the next process restart's reconciliation pass.
                job.RevertDispatch();
                await jobRepository.SaveChangesAsync(cancellationToken);
                LogStartJobFailed(_logger, job.Id, printer.Id, ex);
                await BlockAsync(job, Truncate($"Send failed: {ex.Message}"), cancellationToken);
                return false;
            }
        }

        job.ClearBlock();
        await jobRepository.SaveChangesAsync(cancellationToken);
        await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
        await eventPublisher.PrintJobSendProgressAsync(job.Id, PrintSendStage.Sent, null, 1, null, cancellationToken);
        return true;
    }

    private async Task<bool> IsStillIdleAsync(Guid printerId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var current = await scope.ServiceProvider.GetRequiredService<IPrinterRepository>().GetByIdAsync(printerId, cancellationToken);
        return current is { IsReachable: true, Status: PrinterStatus.Idle };
    }

    /// <summary>
    /// Forwards a connector's send progress as hub events: on a stage change, or every 5 % of the upload. The connector reports
    /// synchronously; the events are chained so they leave in order.
    /// </summary>
    private sealed class SendProgressRelay(IProductionEventPublisher publisher, Guid jobId, int attempt) : IProgress<PrintSendProgress>
    {
        private Task _sending = Task.CompletedTask;
        private PrintSendProgress? _last;

        public void Report(PrintSendProgress value)
        {
            if (_last is { } last && last.Stage == value.Stage && (value.Percent ?? 0) - (last.Percent ?? 0) < 5 && value.Percent != 100) return;
            _last = value;
            _sending = _sending.ContinueWith(
                _ => publisher.PrintJobSendProgressAsync(jobId, value.Stage, value.Percent, attempt, null, CancellationToken.None),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }

        // Live feedback only: a lost event must never fail the send.
        public async Task FlushAsync()
        {
            try { await _sending; }
            catch (Exception) { /* the next event or a page reload shows the state again */ }
        }
    }

    private const string FeedMappingReason = "OpenFarm cannot map the filaments to the printer feed. Check the loaded spools.";

    // Persists why an attempt failed through a fresh instance, so nothing half-prepared by this attempt is saved with it.
    // `slicedGrams`, per line, are what the file for this revision takes: kept so readiness checks them next time.
    private async Task BlockAsync(PrintJob job, string reason, CancellationToken cancellationToken, IReadOnlyList<decimal?>? slicedGrams = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
        if (await repository.GetByIdAsync(job.Id, cancellationToken) is not { Status: PrintJobStatus.Queued, DispatchedAtUtc: null } persisted)
        {
            return;
        }

        if (slicedGrams is not null && persisted.FileRevisionHash == job.FileRevisionHash)
            persisted.BindFilamentUsage(slicedGrams.Select(grams => ((Guid?)null, grams, (FilamentUsageCurve?)null)).ToList());
        persisted.RecordBlock(Truncate(reason), DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        LogJobBlocked(_logger, job.Id, persisted.BlockFailureCount, PrintJob.RetryDelay(persisted.BlockFailureCount), reason);
    }

    private static string Describe(Exception ex) => Truncate(ex is SlicingProfileUnconfiguredException or SlicerUnavailableException
        or SlicingFailedException or ProductionSnapshotUnavailableException or JobNotEligibleForRemoteControlException
        ? ex.Message : "Unexpected error while preparing the file. See the server log.");

    private static string Truncate(string text) => text.Length > 500 ? text[..500] : text;

    private static async Task PublishDispatchedAsync(IServiceProvider services, PrintJob job, Guid printerId, CancellationToken cancellationToken)
    {
        var activityLogWriter = services.GetRequiredService<IActivityLogWriter>();
        var entry = ActivityLogEntry.Create(
            entityType: "PrintJob", entityId: job.Id.ToString(),
            eventType: "PrintJobDispatched", actorId: null, actorType: ActorType.System);
        await activityLogWriter.WriteAsync(entry, cancellationToken);

        var publisher = services.GetRequiredService<IProductionEventPublisher>();
        await publisher.PrintJobDispatchedAsync(job.Id, printerId, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} to printer {PrinterId} sliced with {Engine} {Version}: machine={Machine} process={Process} filaments={Filaments}")]
    private static partial void LogSlicingProfilesUsed(ILogger logger, Guid jobId, Guid printerId, SlicerEngine engine, string version, OpenFarm.Application.Profiles.ResolvedPreset machine, OpenFarm.Application.Profiles.ResolvedPreset process, string filaments);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} not sent (failed attempt {Attempts}, next try in {Delay}): {Reason}")]
    private static partial void LogJobBlocked(ILogger logger, Guid jobId, int attempts, TimeSpan delay, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish send progress for job {JobId}.")]
    private static partial void LogSendProgressFailed(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending job {JobId} to printer {PrinterId} failed (attempt {Attempt}); retrying.")]
    private static partial void LogSendRetrying(ILogger logger, Guid jobId, Guid printerId, int attempt, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending job {JobId} to printer {PrinterId} failed — reverted to Queued/Unassigned.")]
    private static partial void LogStartJobFailed(ILogger logger, Guid jobId, Guid printerId, Exception exception);
}
