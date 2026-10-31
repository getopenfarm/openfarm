using OpenFarm.Domain.Parts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Builds the per-printer inputs <see cref="ProductionDispatchMatcher"/> (Domain) needs (research.md
/// R4 steps 1–3): a printer's own resolved nozzle diameter, what it currently has loaded, and the
/// flat, indexed `(Status, AssignmentMode, QueuePosition)` candidate query — kept as
/// plain data-fetching separate from the pure matching algorithm itself, matching the split
/// <c>AssemblyGraphQueryService</c>/<c>AssemblyGraph</c> already established for Assemblies.
/// </summary>
public sealed partial class ProductionDispatchQueryService(OpenFarmDbContext dbContext, OpenFarm.Application.Production.ProductionProfileReadiness readiness,
    OpenFarm.Application.Production.ProductionJobRequirements jobRequirements, ILogger<ProductionDispatchQueryService> logger, OpenFarm.Application.Parts.IPartFileStorage? fileStorage = null)
{
    public Task<bool> CanDispatchDemandAsync(Guid id, CancellationToken ct) =>
        dbContext.ProductionDemands.AnyAsync(d => d.Id == id && d.State == DemandState.Enabled
            && (d.ProductionRunId == null || dbContext.ProductionRuns.Any(r => r.Id == d.ProductionRunId
                && (r.Status == ProductionRunStatus.Active || r.Status == ProductionRunStatus.Complete))), ct);

    // Persisted connection state, so callers cannot dispatch with an older tracked instance.
    private Task<Printer?> CurrentAsync(Printer printer, CancellationToken cancellationToken) =>
        dbContext.Printers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == printer.Id, cancellationToken);

    /// <summary>
    /// Only nozzle telemetry confirmed on the current connection is usable for dispatch: one entry per distinct size.
    /// </summary>
    public async Task<IReadOnlyList<decimal>> ResolvePrinterNozzlesMmAsync(Printer printer, CancellationToken cancellationToken) =>
        (await CurrentAsync(printer, cancellationToken))?.ConfirmedNozzlesMm() ?? [];

    /// <summary>
    /// Operator-tracked, actively loaded spools provide production material facts, each with its extruder's nozzle.
    /// </summary>
    public async Task<IReadOnlyList<(string? MaterialType, string? ColorHex, decimal? NozzleMm)>> ResolveLoadedMaterialsAsync(
        Printer printer, CancellationToken cancellationToken) =>
        (await ResolveLoadedFilamentsAsync(printer, cancellationToken)).Select(f => ((string?)f.MaterialType, (string?)f.ColorHex, f.NozzleMm)).ToList();

    public async Task<IReadOnlyList<OpenFarm.Application.Production.LoadedProductionFilament>> ResolveLoadedFilamentsAsync(
        Printer printer, CancellationToken cancellationToken)
    {
        var current = await CurrentAsync(printer, cancellationToken);
        var units = await dbContext.FeedUnits.Include(u => u.FeedSlots)
            .Where(u => u.PrinterId == printer.Id).ToListAsync(cancellationToken);
        var feedSlotIds = ProductionFeedPath.ActiveSlotIds(units, printer.Extruders).ToList();
        var extruderBySlot = units.SelectMany(u => u.FeedSlots).ToDictionary(s => s.Id, s => s.ExtruderIndex);
        var loaded = await (from spool in dbContext.Spools
                            where spool.State == SpoolState.Using && spool.FeedSlotId != null && feedSlotIds.Contains(spool.FeedSlotId.Value)
                            join variant in dbContext.FilamentVariants on spool.FilamentVariantId equals variant.Id
                            join filament in dbContext.Filaments on variant.FilamentId equals filament.Id
                            select new OpenFarm.Application.Production.LoadedProductionFilament(variant.Id, filament.Id,
                                filament.MaterialType, variant.ColorHex, variant.ColorName, spool.FeedSlotId!.Value, 0, null)).ToListAsync(cancellationToken);
        return loaded.Select(f => f with
        {
            ExtruderIndex = extruderBySlot[f.SlotId],
            NozzleMm = current?.ConfirmedNozzleMm(extruderBySlot[f.SlotId]),
        }).ToList();
    }

    public async Task<OpenFarm.Application.Production.ReadinessIssue?> GetReadinessIssueAsync(Guid printerId, Guid partId,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, Guid? jobId = null)
    {
        var job = jobId is null ? null : await dbContext.PrintJobs.Include(j => j.Filaments).FirstAsync(j => j.Id == jobId, ct);
        var requirement = job is null ? null : await dbContext.PartRequirements.FirstAsync(r => r.Id == job.PartRequirementId, ct);
        var libraryPart = job is null || requirement?.Quantity is null && job.DispatchedAtUtc is null
            ? await dbContext.Parts.Include(p => p.FilamentRequirements).FirstOrDefaultAsync(p => p.Id == partId, ct) : null;
        var part = job is null ? libraryPart : OpenFarm.Application.Production.ProductionJobRequirements.ResolvePart(job, requirement, libraryPart);
        if (job is not null && part is null) return new(job.SnapshotRecoveryReason ?? "Production snapshot needs recovery.");
        if (part is null) return new("Part not found.");
        if (fileStorage is not null && job?.DispatchedAtUtc is null)
        {
            var inventory = await OpenFarm.Application.Parts.PartObjectInventory.ReadAsync(fileStorage, part.StoragePath, part.PlateNumber, ct);
            if (inventory.Count is { } count && count != part.UnitsPerPrint)
                return new(job is null || requirement?.Quantity is null
                    ? "The saved quantity disagrees with this file. Reprocess the Part before queueing."
                    : "This job's saved quantity disagrees with its file. Cancel and recreate it after reprocessing the Part.");
            if (job?.ObjectExecution is { } old && old.Manifest.Units.Any(u => u.ObjectIds.Count != 1))
                return new("This job uses an obsolete object grouping. Cancel and recreate it after reprocessing the Part.");
        }
        if (job is null) return await readiness.GetIssueAsync(printerId, part,
            OpenFarm.Application.Production.ProductionJobRequirements.FromPart(part), ct);
        var effective = await jobRequirements.ResolveAsync(job, part, ct);
        return await readiness.GetIssueAsync(printerId, part, effective.Requirements, ct, effective.RevisionHash, job);
    }

    public async Task<IReadOnlyList<DispatchCandidate>> FilterReadyAsync(Guid printerId, IReadOnlyList<DispatchCandidate> candidates, CancellationToken ct)
    {
        var ready = new List<DispatchCandidate>();
        foreach (var candidate in candidates)
        {
            var partId = await (from job in dbContext.PrintJobs
                                where job.Id == candidate.JobId
                                join requirement in dbContext.PartRequirements on job.PartRequirementId equals requirement.Id
                                select requirement.PartId).FirstAsync(ct);
            if (await GetReadinessIssueAsync(printerId, partId, candidate.FilamentRequirement, ct, candidate.JobId) is null) ready.Add(candidate);
        }
        return ready;
    }

    /// <summary>
    /// Every <c>Queued</c>/<c>Unassigned</c> job not already dispatch-in-flight to some other printer,
    /// ordered by <see cref="PrintJob.QueuePosition"/>, backed by the
    /// `(Status, AssignmentMode, QueuePosition)` index (<c>PrintJobConfiguration</c>)
    /// — an indexed SQL query, not an in-memory scan (research.md R4's scale note). The
    /// <see cref="Print.PrinterId"/> exclusion closes the race a naive Status/AssignmentMode-only
    /// filter would leave open: a job stays <c>Queued</c>/<c>Unassigned</c> during the dispatch-in-flight
    /// window between <see cref="PrintJob.Dispatch(Guid)"/> and <c>IJobTelemetrySync</c>'s eventual
    /// <c>Printing</c> confirmation, so without this it could be matched a second time to a different
    /// idle printer during that window.
    /// </summary>
    public Task<IReadOnlyList<DispatchCandidate>> GetUnassignedCandidatesAsync(CancellationToken cancellationToken) =>
        GetCandidatesAsync(job => job.Status == PrintJobStatus.Queued && job.AssignmentMode == AssignmentMode.Unassigned && job.PrinterId == null, cancellationToken);

    /// <summary>FR-007c — every Queued job pinned to this exact printer, never matched against any other (research.md R4 step 4).</summary>
    public Task<IReadOnlyList<DispatchCandidate>> GetPinnedCandidatesAsync(Guid printerId, CancellationToken cancellationToken) =>
        GetCandidatesAsync(
            job => job.Status == PrintJobStatus.Queued && job.AssignmentMode == AssignmentMode.Pinned
                && job.PinnedPrinterId == printerId && job.PrinterId == null,
            cancellationToken);

    public async Task<IReadOnlyList<DispatchCandidate>> GetBackfillRetryCandidatesAsync(Guid printerId, CancellationToken ct)
    {
        var candidates = await GetCandidatesAsync(j => j.Status == PrintJobStatus.Queued && j.DispatchedAtUtc == null
            && j.PrinterId == null && (j.AssignmentMode == AssignmentMode.Unassigned || j.PinnedPrinterId == printerId), ct, DemandMode.Backfill);
        var ready = new List<DispatchCandidate>();
        foreach (var candidate in candidates)
        {
            var job = await dbContext.PrintJobs.FirstAsync(j => j.Id == candidate.JobId, ct);
            var requirement = await dbContext.PartRequirements.FirstAsync(r => r.Id == job.PartRequirementId, ct);
            if (!requirement.RecipeActive) continue;
            var demand = await dbContext.ProductionDemands.AsNoTracking().FirstAsync(d => d.Id == requirement.RunLineId, ct);
            var requirementIds = dbContext.PartRequirements.Where(r => r.RunLineId == demand.Id).Select(r => r.Id);
            var occupancy = await dbContext.PrintJobs.Where(j => j.Id != job.Id && requirementIds.Contains(j.PartRequirementId)
                && (j.Status == PrintJobStatus.Printing || j.Status == PrintJobStatus.Paused
                    || j.Status == PrintJobStatus.Queued && (j.DispatchedAtUtc != null || j.AssignmentMode == AssignmentMode.Pinned)))
                .Select(j => j.PrinterId ?? j.PinnedPrinterId).Where(id => id != null).Distinct().CountAsync(ct);
            if (occupancy < demand.BackfillPrinterCap) ready.Add(candidate);
        }
        return ready;
    }

    private async Task<IReadOnlyList<DispatchCandidate>> GetCandidatesAsync(
        System.Linq.Expressions.Expression<Func<PrintJob, bool>> predicate, CancellationToken cancellationToken, DemandMode mode = DemandMode.Finite)
    {
        var jobs = await dbContext.PrintJobs.Where(predicate)
            .Where(j => dbContext.PartRequirements.Any(r => r.Id == j.PartRequirementId
                && dbContext.ProductionDemands.Any(d => d.Id == r.RunLineId && d.Mode == mode)))
            .Include(j => j.Filaments)
            .OrderBy(j => j.QueuePosition).ToListAsync(cancellationToken);
        var candidates = new List<DispatchCandidate>();
        var now = DateTimeOffset.UtcNow;
        foreach (var job in jobs)
        {
            // A job whose last attempt failed waits out its backoff instead of being re-sliced on every telemetry tick.
            if (job.IsInRetryBackoff(now)) continue;
            DispatchCandidate? candidate;
            try
            {
                candidate = await CandidateOrNullAsync(job, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A job whose data cannot be read must not stop every job queued behind it from being looked at.
                LogCandidateFailed(logger, job.Id, ex);
                continue;
            }

            if (candidate is not null) candidates.Add(candidate);
        }

        return candidates;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Print job {JobId} could not be evaluated for dispatch and was skipped.")]
    private static partial void LogCandidateFailed(ILogger logger, Guid jobId, Exception exception);

    private async Task<DispatchCandidate?> CandidateOrNullAsync(PrintJob job, CancellationToken cancellationToken)
    {
        var requirement = await dbContext.PartRequirements.FirstAsync(r => r.Id == job.PartRequirementId, cancellationToken);
        if (!await CanDispatchDemandAsync(requirement.RunLineId, cancellationToken)) return null;
        var libraryPart = requirement.Quantity is null && job.DispatchedAtUtc is null
            ? await dbContext.Parts.Include(p => p.FilamentRequirements).FirstOrDefaultAsync(p => p.Id == requirement.PartId, cancellationToken) : null;
        var part = OpenFarm.Application.Production.ProductionJobRequirements.ResolvePart(job, requirement, libraryPart);
        if (part?.PrintRequirementsStatus != PrintRequirementsStatus.Complete) return null;
        var effective = await jobRequirements.ResolveAsync(job, part, cancellationToken);
        return new DispatchCandidate(job.Id, job.QueuePosition, part.NozzleDiameterMm!.Value,
            effective.Requirements.Select(r => r.MatchingValues).ToList());
    }

    /// <summary>
    /// FR-007b/research.md R11: every other currently-<c>Idle</c> printer that also qualifies for the
    /// exact nozzle/filament requirement a match was just found for, paired with its own capability
    /// score (multi-material slot count) — the input <see cref="ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters"/>
    /// needs to decide whether <paramref name="excludePrinterId"/> should actually claim the match or
    /// defer to a simpler one. Deliberately scoped to <c>Idle</c> printers only — a printer that's
    /// merely about to go idle isn't a real contender yet.
    /// </summary>
    public async Task<IReadOnlyList<(Guid PrinterId, int CapabilityScore)>> GetOtherIdleQualifyingPrintersAsync(
        Guid excludePrinterId,
        Guid jobId,
        decimal requiredNozzleDiameterMm,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> filamentRequirement,
        CancellationToken cancellationToken)
    {
        var idlePrinters = await dbContext.Printers
            .Where(p => p.Status == PrinterStatus.Idle && p.Id != excludePrinterId)
            .ToListAsync(cancellationToken);

        var qualifying = new List<(Guid, int)>();
        foreach (var printer in idlePrinters)
        {
            if (PrinterModelCatalog.SupportsLiveIntegration(printer.Brand) && !printer.IsReachable) continue;
            if (await dbContext.PrintJobs.AnyAsync(j => j.PrinterId == printer.Id && (j.Status == PrintJobStatus.Queued || j.Status == PrintJobStatus.Printing || j.Status == PrintJobStatus.Paused), cancellationToken)) continue;
            if (!ProductionDispatchMatcher.CanServe(await ResolvePrinterNozzlesMmAsync(printer, cancellationToken),
                    await ResolveLoadedMaterialsAsync(printer, cancellationToken), requiredNozzleDiameterMm, filamentRequirement))
            {
                continue;
            }

            var partId = await (from job in dbContext.PrintJobs
                                where job.Id == jobId
                                join requirement in dbContext.PartRequirements on job.PartRequirementId equals requirement.Id
                                select requirement.PartId).FirstAsync(cancellationToken);
            if (await GetReadinessIssueAsync(printer.Id, partId, filamentRequirement, cancellationToken, jobId) is not null) continue;
            var capabilityScore = printer.FeedReadings.Where(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit).Sum(u => u.Slots.Count);
            qualifying.Add((printer.Id, capabilityScore));
        }

        return qualifying;
    }

    /// <summary>
    /// research.md R4 step 5: every (ProductionDemand, PartRequirement) pair belonging to an unlimited
    /// (<c>Quantity IS NULL</c>) line, with the owning line's own occupancy
    /// (<c>COUNT(DISTINCT PrinterId)</c> over its <c>Printing</c>/dispatched-but-unconfirmed/
    /// Pinned-and-queued jobs) and cumulative units (<c>SUM(PlateQuantity)</c> over its
    /// completed and in-flight jobs) — both aggregated across every requirement on the line, per
    /// data-model.md's "one cap, counted together across every distinct printed part" note for an
    /// Assembly-targeted backfill line.
    /// </summary>
    public async Task<IReadOnlyList<BackfillLineCandidate>> GetBackfillCandidatesAsync(CancellationToken cancellationToken, Guid? printerId = null)
    {
        var lines = await dbContext.ProductionDemands.AsNoTracking().Where(line => line.Mode == DemandMode.Backfill && line.State == DemandState.Enabled
            && (line.ProductionRunId == null || dbContext.ProductionRuns.Any(run => run.Id == line.ProductionRunId
                && (run.Status == ProductionRunStatus.Active || run.Status == ProductionRunStatus.Complete)))
            && (printerId == null || line.DefaultAssignmentMode != AssignmentMode.Pinned || line.DefaultPinnedPrinterId == printerId))
            .OrderBy(line => line.CreatedAtUtc).ThenBy(line => line.Id).ToListAsync(cancellationToken);
        if (lines.Count == 0)
        {
            return [];
        }

        var lineIds = lines.Select(l => l.Id).ToList();
        var allRequirements = await dbContext.PartRequirements.Where(r => lineIds.Contains(r.RunLineId)).ToListAsync(cancellationToken);
        var requirements = allRequirements.Where(r => r.RecipeActive).ToList();
        if (requirements.Count == 0)
        {
            return [];
        }

        var partIds = requirements.Select(r => r.PartId).Distinct().ToList();
        var partsById = await dbContext.Parts.Include(p => p.FilamentRequirements).Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var filamentRequirementsByPart = await dbContext.PartFilamentRequirements
            .Where(r => partIds.Contains(r.PartId))
            .GroupBy(r => r.PartId)
            .ToDictionaryAsync(
                g => g.Key,
                g => (IReadOnlyList<(string?, string?)>)g.Select(r => (r.MaterialType, r.NarrowFilamentVariantId == null ? r.DetectedColorHex : null)).ToList(),
                cancellationToken);

        var occupancyByLine = new Dictionary<Guid, int>();
        var cumulativeUnitsByLine = new Dictionary<Guid, int>();
        var linesWithAWaitingJob = new HashSet<Guid>();

        foreach (var line in lines)
        {
            var requirementIdsForLine = allRequirements.Where(r => r.RunLineId == line.Id).Select(r => r.Id).ToList();
            if (requirementIdsForLine.Count == 0)
            {
                occupancyByLine[line.Id] = 0;
                cumulativeUnitsByLine[line.Id] = 0;
                continue;
            }

            // A job created for this line that could not be sent (and now waits out its backoff) is the line's pending
            // work: creating another one every attempt would pile up jobs behind a failure that has not been fixed.
            if (await dbContext.PrintJobs.AnyAsync(j => requirementIdsForLine.Contains(j.PartRequirementId)
                    && j.Status == PrintJobStatus.Queued && j.PrinterId == null && j.DispatchedAtUtc == null
                    && j.AssignmentMode == AssignmentMode.Unassigned, cancellationToken))
            {
                linesWithAWaitingJob.Add(line.Id);
            }

            var occupiedPrinterIds = await dbContext.PrintJobs
                .Where(j => requirementIdsForLine.Contains(j.PartRequirementId) &&
                    (j.Status == PrintJobStatus.Printing || j.Status == PrintJobStatus.Paused ||
                     (j.Status == PrintJobStatus.Queued && j.DispatchedAtUtc != null) ||
                     (j.Status == PrintJobStatus.Queued && j.AssignmentMode == AssignmentMode.Pinned)))
                .Select(j => j.PrinterId ?? j.PinnedPrinterId)
                .Where(id => id != null)
                .Distinct()
                .ToListAsync(cancellationToken);
            occupancyByLine[line.Id] = occupiedPrinterIds.Count;

            // Units still printing count too: with a printer cap above 1, completed units alone let the ceiling overshoot.
            var producedOrPrintingUnits = await dbContext.PrintJobs
                .Where(j => requirementIdsForLine.Contains(j.PartRequirementId) &&
                    (j.Status == PrintJobStatus.Completed || j.Status == PrintJobStatus.Printing || j.Status == PrintJobStatus.Paused ||
                     (j.Status == PrintJobStatus.Queued && j.DispatchedAtUtc != null)))
                .Select(j => new { j.Status, j.PlateQuantity, j.TargetUnits, j.ObjectExecutionJson })
                .ToListAsync(cancellationToken);
            cumulativeUnitsByLine[line.Id] = producedOrPrintingUnits.Sum(j => j.Status == PrintJobStatus.Completed
                ? PrintJob.ProducedUnitsFor(j.Status, j.PlateQuantity, j.TargetUnits, j.ObjectExecutionJson) : j.TargetUnits ?? j.PlateQuantity);
        }

        var coverage = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
            foreach (var requirement in requirements.Where(r => r.RunLineId == line.Id))
            {
                var coverageJobs = await dbContext.PrintJobs.Where(j => j.PartRequirementId == requirement.Id
                    && j.CreatedAtUtc >= line.BalanceSinceUtc
                    && (j.Status == PrintJobStatus.Completed || j.Status == PrintJobStatus.Printing || j.Status == PrintJobStatus.Paused
                        || j.Status == PrintJobStatus.Queued))
                    .Select(j => new { j.Status, j.PlateQuantity, j.TargetUnits, j.ObjectExecutionJson })
                    .ToListAsync(cancellationToken);
                var units = coverageJobs.Sum(j => j.Status == PrintJobStatus.Completed
                    ? PrintJob.ProducedUnitsFor(j.Status, j.PlateQuantity, j.TargetUnits, j.ObjectExecutionJson) : j.TargetUnits ?? j.PlateQuantity);
                coverage[requirement.Id] = units / requirement.UnitsPerSet;
            }
        var candidates = new List<BackfillLineCandidate>();
        // Oldest run first, so which unlimited line backfills next is decided, not left to row order.
        foreach (var requirement in lines.SelectMany(l => requirements.Where(r => r.RunLineId == l.Id).OrderBy(r => coverage[r.Id]).ThenBy(r => r.Id)))
        {
            if (!partsById.TryGetValue(requirement.PartId, out var part) || part.PrintRequirementsStatus != PrintRequirementsStatus.Complete)
            {
                continue;
            }

            var line = lines.First(l => l.Id == requirement.RunLineId);
            var milestone = Math.Floor(requirements.Where(r => r.RunLineId == line.Id).Min(r => coverage[r.Id])) + 1;
            if (coverage[requirement.Id] >= milestone) continue;
            if (linesWithAWaitingJob.Contains(line.Id))
            {
                continue;
            }

            candidates.Add(new BackfillLineCandidate(
                line.Id,
                requirement.Id,
                part.NozzleDiameterMm!.Value,
                filamentRequirementsByPart.GetValueOrDefault(part.Id, []),
                line.BackfillPrinterCap,
                line.BackfillCumulativeUnitCeiling,
                occupancyByLine[line.Id],
                cumulativeUnitsByLine[line.Id]));
        }

        return candidates;
    }
}
