using OpenFarm.Domain.Parts;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Builds the per-printer inputs <see cref="ProductionDispatchMatcher"/> (Domain) needs (research.md
/// R4 steps 1–3): a printer's own resolved nozzle diameter, what it currently has loaded, and the
/// flat, indexed `(OrganizationId, Status, AssignmentMode, QueuePosition)` candidate query — kept as
/// plain data-fetching separate from the pure matching algorithm itself, matching the split
/// <c>AssemblyGraphQueryService</c>/<c>AssemblyGraph</c> already established for Assemblies.
/// </summary>
public sealed class ProductionDispatchQueryService(OpenFarmDbContext dbContext, OpenFarm.Application.Production.ProductionProfileReadiness readiness, OpenFarm.Application.Production.ProductionJobRequirements jobRequirements)
{
    /// <summary>
    /// Only nozzle telemetry confirmed on the current connection is usable for dispatch.
    /// </summary>
    public async Task<decimal?> ResolvePrinterNozzleDiameterMmAsync(Printer printer, CancellationToken cancellationToken)
    {
        // Read persisted connection state so callers cannot dispatch with an older tracked instance.
        var current = await dbContext.Printers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == printer.Id, cancellationToken);
        return current?.NozzleConfirmed == true ? current.ReportedNozzleDiameterMm : null;
    }

    /// <summary>
    /// Operator-tracked, actively loaded spools provide production material facts.
    /// </summary>
    public async Task<IReadOnlyList<(string? MaterialType, string? ColorHex)>> ResolveLoadedMaterialsAsync(
        Printer printer, CancellationToken cancellationToken) =>
        (await ResolveLoadedFilamentsAsync(printer, cancellationToken)).Select(f => ((string?)f.MaterialType, (string?)f.ColorHex)).ToList();

    public async Task<IReadOnlyList<OpenFarm.Application.Production.LoadedProductionFilament>> ResolveLoadedFilamentsAsync(
        Printer printer, CancellationToken cancellationToken)
    {
        var units = await dbContext.FeedUnits.Include(u => u.FeedSlots)
            .Where(u => u.PrinterId == printer.Id).ToListAsync(cancellationToken);
        var feedSlotIds = ProductionFeedPath.ActiveSlotIds(units).ToList();
        return await (from spool in dbContext.Spools
                      where spool.State == SpoolState.Using && spool.FeedSlotId != null && feedSlotIds.Contains(spool.FeedSlotId.Value)
                      join variant in dbContext.FilamentVariants on spool.FilamentVariantId equals variant.Id
                      join filament in dbContext.Filaments on variant.FilamentId equals filament.Id
                      select new OpenFarm.Application.Production.LoadedProductionFilament(variant.Id, filament.Id,
                          filament.MaterialType, variant.ColorHex, variant.ColorName, spool.FeedSlotId!.Value)).ToListAsync(cancellationToken);
    }

    public async Task<string?> GetReadinessIssueAsync(Guid printerId, Guid partId,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, Guid? jobId = null)
    {
        var job = jobId is null ? null : await dbContext.PrintJobs.Include(j => j.FilamentConsumption).FirstAsync(j => j.Id == jobId, ct);
        var requirement = job is null ? null : await dbContext.PartRequirements.FirstAsync(r => r.Id == job.PartRequirementId, ct);
        var libraryPart = job is null || requirement?.Quantity is null && job.DispatchedAtUtc is null
            ? await dbContext.Parts.Include(p => p.FilamentRequirements).FirstOrDefaultAsync(p => p.Id == partId, ct) : null;
        var part = job is null ? libraryPart : OpenFarm.Application.Production.ProductionJobRequirements.ResolvePart(job, requirement, libraryPart);
        if (job is not null && part is null) return job.SnapshotRecoveryReason ?? "Production snapshot needs recovery.";
        if (part is null) return "Part not found.";
        if (job is null) return await readiness.GetIssueAsync(printerId, part,
            OpenFarm.Application.Production.ProductionJobRequirements.FromPart(part), ct);
        var effective = await jobRequirements.ResolveAsync(job, part, ct);
        return await readiness.GetIssueAsync(printerId, part, effective.Requirements, ct, effective.RevisionHash);
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
    /// `(OrganizationId, Status, AssignmentMode, QueuePosition)` index (<c>PrintJobConfiguration</c>)
    /// — an indexed SQL query, not an in-memory scan (research.md R4's scale note). The
    /// <see cref="PrintJob.DispatchedPrinterId"/> exclusion closes the race a naive Status/AssignmentMode-only
    /// filter would leave open: a job stays <c>Queued</c>/<c>Unassigned</c> during the dispatch-in-flight
    /// window between <see cref="PrintJob.Dispatch(Guid)"/> and <c>IJobTelemetrySync</c>'s eventual
    /// <c>Printing</c> confirmation, so without this it could be matched a second time to a different
    /// idle printer during that window. Tenant scoping is Finbuckle's global query filter alone, already
    /// applied to this scoped <see cref="OpenFarmDbContext"/> by the caller's tenant-scope setup.
    /// </summary>
    public Task<IReadOnlyList<DispatchCandidate>> GetUnassignedCandidatesAsync(CancellationToken cancellationToken) =>
        GetCandidatesAsync(job => job.Status == PrintJobStatus.Queued && job.AssignmentMode == AssignmentMode.Unassigned && job.DispatchedPrinterId == null, cancellationToken);

    /// <summary>FR-007c — every Queued job pinned to this exact printer, never matched against any other (research.md R4 step 4).</summary>
    public Task<IReadOnlyList<DispatchCandidate>> GetPinnedCandidatesAsync(Guid printerId, CancellationToken cancellationToken) =>
        GetCandidatesAsync(
            job => job.Status == PrintJobStatus.Queued && job.AssignmentMode == AssignmentMode.Pinned
                && job.PinnedPrinterId == printerId && job.DispatchedPrinterId == null,
            cancellationToken);

    private async Task<IReadOnlyList<DispatchCandidate>> GetCandidatesAsync(
        System.Linq.Expressions.Expression<Func<PrintJob, bool>> predicate, CancellationToken cancellationToken)
    {
        var jobs = await dbContext.PrintJobs.Where(predicate).Include(j => j.FilamentConsumption)
            .OrderBy(j => j.QueuePosition).ToListAsync(cancellationToken);
        var candidates = new List<DispatchCandidate>();
        foreach (var job in jobs)
        {
            var requirement = await dbContext.PartRequirements.FirstAsync(r => r.Id == job.PartRequirementId, cancellationToken);
            var run = await (from line in dbContext.RunLines
                             join productionRun in dbContext.ProductionRuns on line.ProductionRunId equals productionRun.Id
                             where line.Id == requirement.RunLineId
                             select productionRun).FirstAsync(cancellationToken);
            if (run.Status is ProductionRunStatus.Cancelled or ProductionRunStatus.Closed) continue;
            var libraryPart = requirement.Quantity is null && job.DispatchedAtUtc is null
                ? await dbContext.Parts.Include(p => p.FilamentRequirements).FirstOrDefaultAsync(p => p.Id == requirement.PartId, cancellationToken) : null;
            var part = OpenFarm.Application.Production.ProductionJobRequirements.ResolvePart(job, requirement, libraryPart);
            if (part?.PrintRequirementsStatus != PrintRequirementsStatus.Complete) continue;
            var effective = await jobRequirements.ResolveAsync(job, part, cancellationToken);
            candidates.Add(new DispatchCandidate(job.Id, job.QueuePosition, part.NozzleDiameterMm!.Value,
                effective.Requirements.Select(r => r.MatchingValues).ToList()));
        }
        return candidates;
    }

    /// <summary>
    /// FR-007b/research.md R11: every other currently-<c>Idle</c> printer that also qualifies for the
    /// exact nozzle/filament requirement a match was just found for, paired with its own capability
    /// score (<c>AmsUnits.Sum(trays)</c>) — the input <see cref="ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters"/>
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
            if (await dbContext.PrintJobs.AnyAsync(j => j.DispatchedPrinterId == printer.Id && (j.Status == PrintJobStatus.Queued || j.Status == PrintJobStatus.Printing || j.Status == PrintJobStatus.Paused), cancellationToken)) continue;
            var nozzleDiameterMm = await ResolvePrinterNozzleDiameterMmAsync(printer, cancellationToken);
            if (nozzleDiameterMm != requiredNozzleDiameterMm)
            {
                continue;
            }

            var loadedMaterials = await ResolveLoadedMaterialsAsync(printer, cancellationToken);
            var satisfiesRequirement = filamentRequirement.All(required =>
                loadedMaterials.Any(loaded =>
                    (required.MaterialType is null || required.MaterialType == loaded.MaterialType) &&
                    (required.ColorHex is null || string.Equals(required.ColorHex, loaded.ColorHex, StringComparison.OrdinalIgnoreCase))));

            if (!satisfiesRequirement)
            {
                continue;
            }

            var partId = await (from job in dbContext.PrintJobs
                                where job.Id == jobId
                                join requirement in dbContext.PartRequirements on job.PartRequirementId equals requirement.Id
                                select requirement.PartId).FirstAsync(cancellationToken);
            if (await GetReadinessIssueAsync(printer.Id, partId, filamentRequirement, cancellationToken, jobId) is not null) continue;
            var capabilityScore = printer.AmsUnits.Sum(u => u.Trays.Count);
            qualifying.Add((printer.Id, capabilityScore));
        }

        return qualifying;
    }

    /// <summary>
    /// research.md R4 step 5: every (RunLine, PartRequirement) pair belonging to an unlimited
    /// (<c>Quantity IS NULL</c>) line, with the owning line's own occupancy
    /// (<c>COUNT(DISTINCT PrinterId)</c> over its <c>Printing</c>/dispatched-but-unconfirmed/
    /// Pinned-and-queued jobs) and cumulative units (<c>SUM(Part.UnitsPerPrint)</c> over its
    /// <c>Completed</c> jobs) — both aggregated across every requirement on the line, per
    /// data-model.md's "one cap, counted together across every distinct printed part" note for an
    /// Assembly-targeted backfill line.
    /// </summary>
    public async Task<IReadOnlyList<BackfillLineCandidate>> GetBackfillCandidatesAsync(CancellationToken cancellationToken, Guid? printerId = null)
    {
        var lines = await (from line in dbContext.RunLines
                           join run in dbContext.ProductionRuns on line.ProductionRunId equals run.Id
                           where line.Quantity == null && (run.Status == ProductionRunStatus.Active || run.Status == ProductionRunStatus.Complete)
                               && (printerId == null || line.DefaultAssignmentMode != AssignmentMode.Pinned || line.DefaultPinnedPrinterId == printerId)
                           select line).ToListAsync(cancellationToken);
        if (lines.Count == 0)
        {
            return [];
        }

        var lineIds = lines.Select(l => l.Id).ToList();
        var requirements = await dbContext.PartRequirements.Where(r => lineIds.Contains(r.RunLineId)).ToListAsync(cancellationToken);
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

        foreach (var line in lines)
        {
            var requirementIdsForLine = requirements.Where(r => r.RunLineId == line.Id).Select(r => r.Id).ToList();
            if (requirementIdsForLine.Count == 0)
            {
                occupancyByLine[line.Id] = 0;
                cumulativeUnitsByLine[line.Id] = 0;
                continue;
            }

            var occupiedPrinterIds = await dbContext.PrintJobs
                .Where(j => requirementIdsForLine.Contains(j.PartRequirementId) &&
                    (j.Status == PrintJobStatus.Printing || j.Status == PrintJobStatus.Paused ||
                     (j.Status == PrintJobStatus.Queued && j.DispatchedAtUtc != null) ||
                     (j.Status == PrintJobStatus.Queued && j.AssignmentMode == AssignmentMode.Pinned)))
                .Select(j => j.DispatchedPrinterId ?? j.PinnedPrinterId)
                .Where(id => id != null)
                .Distinct()
                .ToListAsync(cancellationToken);
            occupancyByLine[line.Id] = occupiedPrinterIds.Count;

            var completedUnits = await (
                from job in dbContext.PrintJobs
                join requirement in dbContext.PartRequirements on job.PartRequirementId equals requirement.Id
                where requirementIdsForLine.Contains(job.PartRequirementId) && job.Status == PrintJobStatus.Completed
                select job.PlateQuantity)
                .ToListAsync(cancellationToken);
            cumulativeUnitsByLine[line.Id] = completedUnits.Sum();
        }

        var candidates = new List<BackfillLineCandidate>();
        foreach (var requirement in requirements)
        {
            if (!partsById.TryGetValue(requirement.PartId, out var part) || part.PrintRequirementsStatus != PrintRequirementsStatus.Complete)
            {
                continue;
            }

            var line = lines.First(l => l.Id == requirement.RunLineId);
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
