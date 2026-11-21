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
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Bounded <see cref="Channel{T}"/> of printer ids (research.md R4) — mirrors
/// <c>PartProcessingBackgroundService</c>'s shape. One attempt per printer, in two parts:
/// <list type="bullet">
/// <item><b>Decide</b>, one at a time behind <see cref="_decide"/>: a decision reads the whole farm's Unassigned candidate pool
/// and must not race a decision for another printer into double-booking the same job (see
/// <c>ProductionDispatchQueryService.GetUnassignedCandidatesAsync</c>'s own remarks on the dispatch-in-flight window).</item>
/// <item><b>Prepare and send</b> (slice, upload, retries), concurrently for different printers, at most
/// <c>maxConcurrentDispatches</c> at a time. The job stays <c>Queued</c> and undispatched in the database meanwhile, so
/// cancel, run removal and telemetry see what they always saw; the matching queries are told about it through
/// <see cref="_reserved"/> instead.</item>
/// </list>
/// <see cref="PrintJob.Dispatch(Guid)"/>/<see cref="PrintJob.RevertDispatch"/>
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

    // One decision at a time, farm-wide. Held for the queries and the checks before a send, never for the send itself.
    private readonly SemaphoreSlim _decide = new(1, 1);

    // Caps attempts in progress (slicing is CPU-heavy). Taken before the gate, so a decision never waits on a slot while holding it.
    private readonly SemaphoreSlim _slots;

    // Printers with an attempt running: a printer never has two. Ends when the attempt does (completed task).
    private readonly ConcurrentDictionary<Guid, Task> _attempting = new();

    // Jobs a running attempt is about to send, to the printer it chose: not dispatched in the database yet, so the matching
    // queries count them as in flight (candidates, idle printers, backfill occupancy) through this map.
    private readonly ConcurrentDictionary<Guid, Guid> _reserved = new();

    // Printers whose decision found nothing while a send was reserved: the reserved job (or its line's printer cap) may have been
    // why. They look again if that send ends without sending, which the serial loop gave for free.
    private readonly ConcurrentDictionary<Guid, byte> _mayHaveSkipped = new();

    // Printers that deferred an unassigned match, keyed by the printer they deferred to. Only touched behind _decide.
    private readonly Dictionary<Guid, HashSet<Guid>> _deferredTo = [];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductionDispatchBackgroundService> _logger;

    // A send that fails is tried again in place this many times, after these pauses, before the job fails.
    private static readonly TimeSpan[] DefaultSendRetryDelays = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
    private readonly IReadOnlyList<TimeSpan> _sendRetryDelays;

    // How long an idle printer must keep reporting after an unconfirmed start before the start is known not to have happened.
    private readonly TimeSpan _startConfirmGrace;

    // Consecutive failed sends per printer, so a printer that cannot take a file does not fail the whole queue one job at a time.
    // ponytail: in-memory, lost on restart; persist on Printer if restarts hide a flapping printer.
    private readonly ConcurrentDictionary<Guid, (int Count, DateTimeOffset AtUtc)> _sendFailures = [];

    /// <param name="sendRetryDelays">Test seam: the pauses between send attempts (one fewer than the attempts).</param>
    /// <param name="startConfirmGrace">Test seam: see <see cref="_startConfirmGrace"/>.</param>
    /// <param name="maxConcurrentDispatches">How many printers may be slicing or sending at once; 1 is the old one-at-a-time behaviour.</param>
    public ProductionDispatchBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ProductionDispatchBackgroundService> logger,
        IReadOnlyList<TimeSpan>? sendRetryDelays = null, TimeSpan? startConfirmGrace = null, int maxConcurrentDispatches = 3)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _sendRetryDelays = sendRetryDelays ?? DefaultSendRetryDelays;
        _startConfirmGrace = startConfirmGrace ?? TimeSpan.FromMinutes(3);
        _slots = new SemaphoreSlim(Math.Max(1, maxConcurrentDispatches));
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
                    // One attempt per printer: a request that arrives meanwhile stays pending, and the attempt signals again when it ends.
                    if (_attempting.ContainsKey(printerId) || !_pending.TryRemove(printerId, out _)) continue;
                    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _attempting[printerId] = done.Task;
                    _ = RunAttemptAsync(printerId, done, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown while idling for the next channel item.
        }
        finally
        {
            // Attempts see the same stopping token; let them unwind before the host goes on.
            await Task.WhenAll(_attempting.Values);
        }
    }

    /// <summary>One printer's attempt: decide behind the gate, then send outside it. Never throws.</summary>
    private async Task RunAttemptAsync(Guid printerId, TaskCompletionSource done, CancellationToken stoppingToken)
    {
        var wakeOthers = false;
        try
        {
            await _slots.WaitAsync(stoppingToken);
            try
            {
                Func<Task<bool>>? send;
                await _decide.WaitAsync(stoppingToken);
                try
                {
                    send = await ProcessDispatchAttemptAsync(printerId, stoppingToken);
                    try
                    {
                        await WakeDeferredAsync(printerId, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Only a nudge to other printers: it must not strand the send this decision just reserved.
                        LogDispatchAttemptFailed(_logger, printerId, ex);
                    }
                }
                finally
                {
                    _decide.Release();
                }

                if (send is not null)
                {
                    var sent = false;
                    try { sent = await send(); }
                    finally { wakeOthers = !sent; }
                }
            }
            finally
            {
                _slots.Release();
            }
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
        finally
        {
            _attempting.TryRemove(printerId, out _);
            done.SetResult();
            // A job this attempt held back from the others is free again: printers that found nothing meanwhile look once more.
            // Only those, never every idle printer, so a failure that repeats cannot make printers wake each other for ever.
            if (wakeOthers)
            {
                foreach (var other in _mayHaveSkipped.Keys)
                {
                    if (other != printerId && _mayHaveSkipped.TryRemove(other, out _)) RequestDispatchAttempt(other);
                }
            }

            // A request that came in while this attempt ran was left pending: wake the loop for it.
            if (_pending.ContainsKey(printerId)) _channel.Writer.TryWrite(printerId);
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
            if (job.ObjectExecution is { StartRequested: true } || job.LastBlockReason == StartUnconfirmedReason)
            {
                // Left for the printer's own telemetry to settle (ProcessDispatchAttemptAsync); the first failure stays the anchor.
                if (job.LastBlockReason is null) job.RecordBlock(StartUnconfirmedReason, DateTimeOffset.UtcNow);
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

    /// <summary>
    /// Runs the decision (call it behind <see cref="_decide"/>). Returns what is left to do once the gate is open — the slow
    /// send, true when the file was sent — or null when the attempt ended here. The send keeps this attempt's scope until it ends.
    /// </summary>
    private async Task<Func<Task<bool>>?> ProcessDispatchAttemptAsync(Guid printerId, CancellationToken cancellationToken)
    {
        // Sampled before deciding: reservations are only added behind the gate, so a set seen now covers every one that could hide a
        // job from this decision. Registered up front, because a send ending mid-decision wakes only those already registered.
        if (!_reserved.IsEmpty) _mayHaveSkipped[printerId] = 0;

        var scope = _scopeFactory.CreateScope();
        Func<Task<bool>>? send = null;
        try
        {
            send = await DecideAsync(scope.ServiceProvider, printerId, cancellationToken);
        }
        finally
        {
            if (send is null) scope.Dispose();
        }

        if (send is null) return null;
        _mayHaveSkipped.TryRemove(printerId, out _);

        return async () =>
        {
            try { return await send(); }
            finally { scope.Dispose(); }
        };
    }

    private async Task<Func<Task<bool>>?> DecideAsync(IServiceProvider services, Guid printerId, CancellationToken cancellationToken)
    {
        var printerRepository = services.GetRequiredService<IPrinterRepository>();
        var jobRepository = services.GetRequiredService<IPrintJobRepository>();
        var requirementRepository = services.GetRequiredService<IPartRequirementRepository>();
        var partRepository = services.GetRequiredService<IPartRepository>();
        var queryService = services.GetRequiredService<ProductionDispatchQueryService>();

        var printer = await printerRepository.GetByIdAsync(printerId, cancellationToken);
        if (printer is null || printer.Status != PrinterStatus.Idle
            || (PrinterModelCatalog.SupportsLiveIntegration(printer.Brand) && !printer.IsReachable))
        {
            return null;
        }

        if (await jobRepository.GetActiveJobByDispatchedPrinterIdAsync(printerId, cancellationToken) is { } active)
        {
            // A send that never became a print must not hold the printer: once the idle printer has kept reporting long enough
            // without taking this job's file, the start did not happen.
            if (StartNeverCame(active, printer, _startConfirmGrace))
            {
                active.FailSend(PrinterDidNotStartReason, DateTimeOffset.UtcNow);
                await StageJobEventAsync(services, active, "PrintJobSendFailed", cancellationToken);
                await jobRepository.SaveChangesAsync(cancellationToken);
                await PublishSendFailedAsync(services, active, cancellationToken);
                RequestDispatchAttempt(printerId);
            }

            return null;
        }

        if (_sendFailures.TryGetValue(printerId, out var failures) && DateTimeOffset.UtcNow < failures.AtUtc + SendCooldown(failures.Count))
        {
            return null;
        }

        var nozzlesMm = await queryService.ResolvePrinterNozzlesMmAsync(printer, cancellationToken);
        if (nozzlesMm.Count == 0)
        {
            return null;
        }

        var loadedMaterials = await queryService.ResolveLoadedMaterialsAsync(printer, cancellationToken);
        var candidates = await queryService.FilterReadyAsync(printer.Id, await queryService.GetUnassignedCandidatesAsync(cancellationToken, _reserved), cancellationToken);
        var pinnedCandidates = await queryService.FilterReadyAsync(printer.Id, await queryService.GetPinnedCandidatesAsync(printer.Id, cancellationToken, _reserved), cancellationToken);
        var jobId = ProductionDispatchMatcher.SelectNextJob(nozzlesMm, loadedMaterials, candidates, pinnedCandidates);
        if (jobId is null)
        {
            // FR-006b — backfill is only ever consulted once no finite (Unassigned or Pinned) job matched.
            return await TryDispatchBackfillAsync(services, printer, nozzlesMm, loadedMaterials, queryService, jobRepository, cancellationToken);
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
                printer.Id, unassignedMatch.JobId, unassignedMatch.RequiredNozzleDiameterMm, unassignedMatch.FilamentRequirement, cancellationToken, _reserved);

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

                return null;
            }
        }

        var job = await jobRepository.GetByIdAsync(jobId.Value, cancellationToken);
        if (job is null || job.Status != PrintJobStatus.Queued || job.PrinterId is not null || _reserved.ContainsKey(job.Id)
            || (job.AssignmentMode == AssignmentMode.Pinned && job.PinnedPrinterId != printer.Id))
        {
            // Lost a race against another dispatch decision (or a manual pin/cancel) between the
            // candidate query above and this re-check — simply skip; the printer's own next trigger
            // (or this reconciliation pass on next boot) will try again against whatever is left.
            return null;
        }

        var requirement = await requirementRepository.GetByIdAsync(job.PartRequirementId, cancellationToken);
        var libraryPart = requirement is not null && requirement.Quantity is null
            ? await partRepository.GetByIdAsync(requirement.PartId, cancellationToken) : null;
        var part = ProductionJobRequirements.ResolvePart(job, requirement, libraryPart);
        if (requirement is null || part is null || part.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete)
        {
            LogMissingDispatchTarget(_logger, job.Id);
            return null;
        }

        var runLine = await services.GetRequiredService<IProductionDemandRepository>().GetAsync(requirement.RunLineId, cancellationToken);
        if (runLine is null || !await queryService.CanDispatchDemandAsync(runLine.Id, cancellationToken))
        {
            LogMissingDispatchTarget(_logger, job.Id);
            return null;
        }

        return await DispatchJobAsync(services, printer, job, part, runLine.RevisionMode, jobRepository, cancellationToken);
    }

    /// <summary>
    /// Once a printer that others deferred a match to holds a job (dispatched, or reserved for a send in progress), wakes them:
    /// the match may still be waiting (that printer took an earlier job of its own), and a manual printer gets no telemetry to
    /// wake it. Waking only once it holds a job means it no longer contends, so two printers never keep waking each other.
    /// </summary>
    private async Task WakeDeferredAsync(Guid printerId, CancellationToken cancellationToken)
    {
        if (!_deferredTo.TryGetValue(printerId, out var deferring))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
        if (!_reserved.Values.Contains(printerId) && await jobs.GetActiveJobByDispatchedPrinterIdAsync(printerId, cancellationToken) is null)
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
    private async Task<Func<Task<bool>>?> TryDispatchBackfillAsync(
        IServiceProvider services, Printer printer, IReadOnlyList<decimal> nozzlesMm,
        IReadOnlyList<(string? MaterialType, string? ColorHex, decimal? NozzleMm)> loadedMaterials, ProductionDispatchQueryService queryService,
        IPrintJobRepository jobRepository, CancellationToken cancellationToken)
    {
        var retries = await queryService.FilterReadyAsync(printer.Id,
            await queryService.GetBackfillRetryCandidatesAsync(printer.Id, cancellationToken, _reserved), cancellationToken);
        var retryId = ProductionDispatchMatcher.SelectNextJob(nozzlesMm, loadedMaterials, retries);
        if (retryId is { } waitingId)
        {
            var waiting = await jobRepository.GetByIdAsync(waitingId, cancellationToken);
            if (waiting is null) return null;
            var req = await services.GetRequiredService<IPartRequirementRepository>().GetByIdAsync(waiting.PartRequirementId, cancellationToken);
            var current = req is null ? null : await services.GetRequiredService<IPartRepository>().GetByIdAsync(req.PartId, cancellationToken);
            return current is null ? null
                : await DispatchJobAsync(services, printer, waiting, current, RevisionMode.FollowLatest, jobRepository, cancellationToken);
        }
        var backfillCandidates = await queryService.GetBackfillCandidatesAsync(cancellationToken, printer.Id, _reserved);
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
            return null;
        }

        var db = services.GetRequiredService<OpenFarm.Infrastructure.Persistence.OpenFarmDbContext>();
        await using var claim = await db.Database.BeginTransactionAsync(cancellationToken);
        var requirementRepository = services.GetRequiredService<IPartRequirementRepository>();
        var partRepository = services.GetRequiredService<IPartRepository>();
        var requirement = await requirementRepository.GetByIdAsync(requirementId.Value, cancellationToken);
        var part = requirement is null ? null : await partRepository.GetByIdAsync(requirement.PartId, cancellationToken);
        if (requirement is null || part is null || part.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete)
        {
            return null;
        }

        var runLine = await services.GetRequiredService<IProductionDemandRepository>().GetAsync(requirement.RunLineId, cancellationToken);
        if (runLine is null || !await queryService.CanDispatchDemandAsync(runLine.Id, cancellationToken))
        {
            return null;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"RunLines\" WHERE \"Id\" = {runLine.Id} FOR UPDATE", cancellationToken);
        await db.Entry(runLine).ReloadAsync(cancellationToken);
        if (!await queryService.CanDispatchDemandAsync(runLine.Id, cancellationToken)) return null;
        var freshCandidates = await queryService.GetBackfillCandidatesAsync(cancellationToken, printer.Id, _reserved);
        if (!freshCandidates.Any(c => c.PartRequirementId == requirement.Id && c.CurrentOccupancy < c.BackfillPrinterCap)) return null;
        var nextQueuePosition = (await jobRepository.GetMaxQueuePositionAsync(cancellationToken) ?? 0) + 1024;
        var filamentSpecs = ProductionJobRequirements.FromPart(part);

        var newJob = PrintJob.Create(
            part.Name, requirement.Id, nextQueuePosition, part.UnitsPerPrint,
            runLine.DefaultAssignmentMode, runLine.DefaultPinnedPrinterId, part.RevisionHash, part.RevisionNumber, filamentSpecs);

        await services.GetRequiredService<ProductionSnapshotFactory>().CaptureAsync(newJob, part, cancellationToken);
        await jobRepository.AddAsync(newJob, cancellationToken);
        await jobRepository.SaveChangesAsync(cancellationToken);
        await claim.CommitAsync(cancellationToken);

        return await DispatchJobAsync(services, printer, newJob, part, runLine.RevisionMode, jobRepository, cancellationToken);
    }

    /// <returns>
    /// The send, to run once the decision gate is open, or null when nothing is left to do (blocked, or a manual printer, whose
    /// assignment is complete here). The job is reserved for this printer until the send returns.
    /// </returns>
    private async Task<Func<Task<bool>>?> DispatchJobAsync(
        IServiceProvider services, Printer printer, PrintJob job, Part part, RevisionMode revisionMode,
        IPrintJobRepository jobRepository, CancellationToken cancellationToken)
    {
        if (part.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete) return null;
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
            return null;
        }
        var currentPrinter = await services.GetRequiredService<IPrinterRepository>().GetByIdAsync(printer.Id, cancellationToken);
        if (currentPrinter is null || currentPrinter.Status != PrinterStatus.Idle
            || part.NozzleDiameterMm is not { } nozzleMm || !currentPrinter.ConfirmedNozzlesMm().Contains(nozzleMm)
            || await services.GetRequiredService<ProductionProfileReadiness>().GetIssueAsync(printer.Id, part, effective.Requirements, cancellationToken, effective.RevisionHash, job) is not null) return null;
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
                if (freshPart.PrintRequirementsStatus != OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete) return null;
                job.Dispatch(printer.Id, freshPart.RevisionHash, freshPart.RevisionNumber);
            }
            else
            {
                job.Dispatch(printer.Id);
            }

            job.ClearBlock();
            await StageJobEventAsync(services, job, "PrintJobDispatched", cancellationToken);
            await jobRepository.SaveChangesAsync(cancellationToken);
            await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
            return null;
        }

        // Slicing and sending take minutes; the caller runs this after the gate is open so other printers decide meanwhile.
        // Until it returns the job is reserved for this printer, which keeps every other decision away from it.
        if (!_reserved.TryAdd(job.Id, printer.Id)) return null;
        return async () =>
        {
            var sent = false;
            try
            {
                sent = await DispatchToLiveIntegratedPrinterAsync(services, printer, job, part, nozzleMm, revisionMode, jobRepository, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Nothing thrown before the send was confirmed may leave the job in flight, holding its printer until a restart.
                LogDispatchAttemptFailed(_logger, printer.Id, ex);
                await BlockAsync(job, "Unexpected error while sending. See the server log.", cancellationToken);
            }
            finally
            {
                // Persisted state (dispatched, or reverted and blocked) is final by now, so releasing the reservation leaves no gap.
                _reserved.TryRemove(new KeyValuePair<Guid, Guid>(job.Id, printer.Id));

                // Whatever stopped this attempt, pages showing its progress go back to the job's own state.
                if (!sent)
                {
                    try
                    {
                        await services.GetRequiredService<IProductionEventPublisher>()
                            .PrintJobSendProgressAsync(job.Id, printer.Id, PrintSendStage.Ended, null, 1, null, CancellationToken.None);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { LogSendProgressFailed(_logger, job.Id, ex); }
                }
            }

            return sent;
        };
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
        await eventPublisher.PrintJobSendProgressAsync(job.Id, printer.Id, PrintSendStage.Slicing, null, 1, null, cancellationToken);
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
                || persistedJob is not null && (persistedJob.Status != PrintJobStatus.Queued || persistedJob.DispatchedAtUtc is not null
                    || persistedJob.AssignmentMode == AssignmentMode.Pinned && persistedJob.PinnedPrinterId != printer.Id)
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
            var progress = new SendProgressRelay(eventPublisher, job.Id, printer.Id, attempt);
            try
            {
                using var stream = new MemoryStream(fileContent.Bytes);
                if (job.ObjectExecution is { } execution)
                {
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
                if (ex is OpenFarm.Infrastructure.Printers.PrinterStartUnconfirmedException)
                {
                    job.RecordBlock(StartUnconfirmedReason, DateTimeOffset.UtcNow);
                    await StageJobEventAsync(services, job, "PrintJobDispatched", cancellationToken);
                    await jobRepository.SaveChangesAsync(cancellationToken);
                    await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
                    LogStartJobFailed(_logger, job.Id, printer.Id, ex);
                    return false;
                }
                if (job.ObjectExecution is { StartRequested: true } requested)
                {
                    // A lost start acknowledgement must not resend an exact-quantity plate, even after restart: the printer's
                    // own telemetry settles it (ProcessDispatchAttemptAsync). A send that failed before the start command
                    // left cannot have started anything, so it is retried like any other.
                    if (progress.LastStage == PrintSendStage.Starting)
                    {
                        job.RecordBlock(StartUnconfirmedReason, DateTimeOffset.UtcNow);
                        await StageJobEventAsync(services, job, "PrintJobDispatched", cancellationToken);
                        await jobRepository.SaveChangesAsync(cancellationToken);
                        await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
                        LogStartJobFailed(_logger, job.Id, printer.Id, ex);
                        return false;
                    }

                    job.RecordObjectExecution(requested with { StartRequested = false });
                    await jobRepository.SaveChangesAsync(cancellationToken);
                }
                // A blip (Wi-Fi, a busy broker) is retried here instead of costing a backoff and a new slice — but only while the
                // printer is still idle: one that started meanwhile may have taken this very send, and it must not get it twice.
                // The pause holds this printer's slot, not the farm's decision gate (~40 s worst case).
                if (attempt <= _sendRetryDelays.Count && ex is not BambuTransferException)
                {
                    LogSendRetrying(_logger, job.Id, printer.Id, attempt, ex);
                    await eventPublisher.PrintJobSendProgressAsync(job.Id, printer.Id, PrintSendStage.Retrying, null, attempt + 1, Truncate(ex.Message), cancellationToken);
                    await Task.Delay(_sendRetryDelays[attempt - 1], cancellationToken);
                    if (await IsStillIdleAsync(printer.Id, cancellationToken)) continue;
                }

                // Retries used up and the printer never took it: the job fails (requeue or write it off) and frees the printer,
                // which itself pauses before the next job so a printer that cannot take files does not fail the whole queue.
                job.FailSend(Truncate($"Send failed: {ex.Message}"), DateTimeOffset.UtcNow);
                await StageJobEventAsync(services, job, "PrintJobSendFailed", cancellationToken);
                await jobRepository.SaveChangesAsync(cancellationToken);
                LogStartJobFailed(_logger, job.Id, printer.Id, ex);
                _sendFailures[printer.Id] = (_sendFailures.GetValueOrDefault(printer.Id).Count + 1, DateTimeOffset.UtcNow);
                await PublishSendFailedAsync(services, job, cancellationToken);
                return false;
            }
        }

        _sendFailures.TryRemove(printer.Id, out _);
        try
        {
            job.ClearBlock();
            await StageJobEventAsync(services, job, "PrintJobDispatched", cancellationToken);
            await jobRepository.SaveChangesAsync(cancellationToken);
            await PublishDispatchedAsync(services, job, printer.Id, cancellationToken);
            await eventPublisher.PrintJobSendProgressAsync(job.Id, printer.Id, PrintSendStage.Sent, null, 1, null, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The printer has the job and it is saved as dispatched; telemetry marks it started. Never undo a send that worked.
            LogSendBookkeepingFailed(_logger, job.Id, ex);
        }

        return true;
    }

    /// <summary>A dispatched, unstarted job on an idle live printer that has kept reporting past the grace without taking its file.</summary>
    internal static bool StartNeverCame(PrintJob job, Printer printer, TimeSpan grace) =>
        job is { Status: PrintJobStatus.Queued, DispatchedAtUtc: { } dispatchedAt }
        && PrinterModelCatalog.SupportsLiveIntegration(printer.Brand)
        && printer is { Status: PrinterStatus.Idle, IsReachable: true, StateObservedAtUtc: { } observedAt }
        && observedAt >= (job.LastBlockedAtUtc ?? dispatchedAt) + grace
        && printer.CurrentGcodeFile?.Contains(job.Id.ToString("N"), StringComparison.Ordinal) != true
        && printer.CurrentJobName?.Contains(job.Id.ToString("N"), StringComparison.Ordinal) != true;

    /// <summary>1, 2, 4 … minutes after consecutive failed sends to one printer, at most an hour.</summary>
    private static TimeSpan SendCooldown(int failures) => TimeSpan.FromMinutes(Math.Min(60, 1 << Math.Min(failures - 1, 6)));

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
    private sealed class SendProgressRelay(IProductionEventPublisher publisher, Guid jobId, Guid printerId, int attempt) : IProgress<PrintSendProgress>
    {
        private Task _sending = Task.CompletedTask;
        private PrintSendProgress? _last;

        /// <summary>The furthest the connector got: <see cref="PrintSendStage.Starting"/> means the start command may have left.</summary>
        public PrintSendStage? LastStage { get; private set; }

        public void Report(PrintSendProgress value)
        {
            LastStage = value.Stage;
            if (_last is { } last && last.Stage == value.Stage && (value.Percent ?? 0) - (last.Percent ?? 0) < 5 && value.Percent != 100) return;
            _last = value;
            _sending = _sending.ContinueWith(
                _ => publisher.PrintJobSendProgressAsync(jobId, printerId, value.Stage, value.Percent, attempt, null, CancellationToken.None),
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
    private const string StartUnconfirmedReason = "Print start is unconfirmed. Waiting for the printer to report.";
    private const string PrinterDidNotStartReason = "Send failed: the printer did not start the print.";

    // Persists why an attempt failed through a fresh instance, so nothing half-prepared by this attempt is saved with it.
    // `slicedGrams`, per line, are what the file for this revision takes: kept so readiness checks them next time.
    private async Task BlockAsync(PrintJob job, string reason, CancellationToken cancellationToken, IReadOnlyList<decimal?>? slicedGrams = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
        if (await repository.GetByIdAsync(job.Id, cancellationToken) is not { Status: PrintJobStatus.Queued } persisted
            || persisted.DispatchedAtUtc is not null && (persisted.PrinterId != job.PrinterId || persisted.ObjectExecution is { StartRequested: true }))
        {
            return;
        }

        // This attempt's own dispatch, saved before a failure that sent nothing: the printer is let go.
        persisted.RevertDispatch();

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

    private static Task StageJobEventAsync(IServiceProvider services, PrintJob job, string eventType, CancellationToken ct) =>
        services.GetRequiredService<IActivityLogWriter>().StageAsync(
            ActivityLogEntry.Create("PrintJob", job.Id.ToString(), eventType, null, ActorType.System), ct);

    private static async Task PublishSendFailedAsync(IServiceProvider services, PrintJob job, CancellationToken cancellationToken)
    {
        var publisher = services.GetRequiredService<IProductionEventPublisher>();
        await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, cancellationToken);
        await publisher.NotificationsChangedAsync(cancellationToken);
    }

    private static async Task PublishDispatchedAsync(IServiceProvider services, PrintJob job, Guid printerId, CancellationToken cancellationToken)
    {
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
    private static partial void LogSlicingProfilesUsed(ILogger logger, Guid jobId, Guid printerId, SlicerEngine engine, string version, OpenFarm.Application.Profiles.ResolvedPreset? machine, OpenFarm.Application.Profiles.ResolvedPreset? process, string filaments);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} not sent (failed attempt {Attempts}, next try in {Delay}): {Reason}")]
    private static partial void LogJobBlocked(ILogger logger, Guid jobId, int attempts, TimeSpan delay, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish send progress for job {JobId}.")]
    private static partial void LogSendProgressFailed(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending job {JobId} to printer {PrinterId} failed (attempt {Attempt}); retrying.")]
    private static partial void LogSendRetrying(ILogger logger, Guid jobId, Guid printerId, int attempt, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} was sent but recording it failed; telemetry will mark it started.")]
    private static partial void LogSendBookkeepingFailed(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending job {JobId} to printer {PrinterId} failed.")]
    private static partial void LogStartJobFailed(ILogger logger, Guid jobId, Guid printerId, Exception exception);
}
