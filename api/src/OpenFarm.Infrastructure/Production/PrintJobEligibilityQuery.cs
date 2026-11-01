using Microsoft.Extensions.Logging;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

public sealed partial class PrintJobEligibilityQuery(IPrinterRepository printers, IPartRequirementRepository requirements,
    IPartRepository parts, ProductionDispatchQueryService dispatchQuery, IPrintJobRepository printJobs,
    ProductionJobRequirements jobRequirements, IFilamentRepository filaments, ILogger<PrintJobEligibilityQuery> logger) : IPrintJobEligibilityQuery
{
    private readonly Dictionary<Guid, JobMatchingDiagnostics> _evaluations = [];
    private readonly Dictionary<Guid, (IReadOnlyList<LoadedProductionFilament> Loaded, IReadOnlyList<decimal> Nozzles, bool Reserved)> _printerFacts = [];
    private IReadOnlyList<Printer>? _fleet;

    private async Task<(IReadOnlyList<LoadedProductionFilament> Loaded, IReadOnlyList<decimal> Nozzles, bool Reserved)> PrinterFactsAsync(Printer printer, CancellationToken ct)
    {
        if (_printerFacts.TryGetValue(printer.Id, out var cached)) return cached;
        var facts = (await dispatchQuery.ResolveLoadedFilamentsAsync(printer, ct),
            await dispatchQuery.ResolvePrinterNozzlesMmAsync(printer, ct),
            await printJobs.GetActiveJobByDispatchedPrinterIdAsync(printer.Id, ct) is not null);
        _printerFacts[printer.Id] = facts;
        return facts;
    }

    public async Task<string?> GetBlockingReasonAsync(PrintJob job, CancellationToken cancellationToken) =>
        (await EvaluateAsync(job, cancellationToken)).Summary;

    public async Task<IReadOnlySet<Guid>> GetEligibleJobIdsAsync(IReadOnlyList<PrintJob> jobs, CancellationToken cancellationToken)
    {
        var result = new HashSet<Guid>();
        foreach (var job in jobs)
            if ((await EvaluateAsync(job, cancellationToken)).Printers.Any(p => p.Eligible)) result.Add(job.Id);
        return result;
    }

    private async Task<JobMatchingDiagnostics> EvaluateAsync(PrintJob job, CancellationToken ct)
    {
        if (_evaluations.TryGetValue(job.Id, out var cached)) return cached;
        var result = await GetDiagnosticsAsync(job, ct, false);
        _evaluations[job.Id] = result;
        return result;
    }

    public async Task<JobMatchingDiagnostics> GetDiagnosticsAsync(PrintJob job, CancellationToken cancellationToken, bool includeScheduling = true)
    {
        try
        {
            return await EvaluateJobAsync(job, includeScheduling, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One job whose data cannot be read is a blocked job, never an error for every job listed beside it.
            LogEvaluationFailed(logger, job.Id, ex);
            return new(job.Id, DateTimeOffset.UtcNow, "Job snapshot", "Blocked",
                "Job data is unreadable. See the server log.", [], [], []);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Evaluating print job {JobId} failed unexpectedly.")]
    private static partial void LogEvaluationFailed(ILogger logger, Guid jobId, Exception exception);

    private async Task<JobMatchingDiagnostics> EvaluateJobAsync(PrintJob job, bool includeScheduling, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var requirement = await requirements.GetByIdAsync(job.PartRequirementId, ct);
        var libraryPart = requirement is not null && requirement.Quantity is null && job.DispatchedAtUtc is null
            ? await parts.GetByIdAsync(requirement.PartId, ct) : null;
        var part = ProductionJobRequirements.ResolvePart(job, requirement, libraryPart);
        var demandAllowed = requirement is not null && await dispatchQuery.CanDispatchDemandAsync(requirement.RunLineId, ct);
        var jobChecks = new List<MatchingCheck>
        {
            Check("demand-state", "Demand is enabled", demandAllowed, "Enabled", demandAllowed ? "Enabled" : "Paused, removed, or its Run is closed"),
            Check("job-state", "Job is waiting for assignment", job.Status == PrintJobStatus.Queued && job.DispatchedAtUtc is null,
                "Queued, not dispatched", job.DispatchedAtUtc is null ? job.Status.ToString() : "Already dispatched"),
            Check("part-requirements", "Production print requirements", part?.PrintRequirementsStatus == OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete,
                "Complete nozzle and material requirements", part?.PrintRequirementsStatus.ToString() ?? "Snapshot needs recovery",
                libraryPart is null ? null : $"/parts/{libraryPart.Id}")
        };
        if (part is null) return new(job.Id, DateTimeOffset.UtcNow, "Job snapshot", "Blocked", job.SnapshotRecoveryReason ?? "Production snapshot needs recovery.", [], jobChecks, []);
        var effective = await jobRequirements.ResolveAsync(job, part, ct);
        var display = new List<MatchingFilamentRequirement>();
        foreach (var spec in effective.Requirements)
        {
            var variant = spec.NarrowFilamentVariantId is { } id ? await filaments.FindVariantByIdAsync(id, ct) : null;
            var filament = variant is null ? null : await filaments.GetByIdAsync(variant.FilamentId, ct);
            display.Add(new(spec.MaterialType, spec.DetectedColorHex, spec.NarrowFilamentVariantId,
                variant is null ? null : $"{filament?.Material} · {variant.ColorName}", variant?.ColorHex, spec.NarrowFilamentProfileId));
        }
        var results = new List<PrinterMatchingDiagnostics>();
        var fleet = _fleet ??= await printers.ListAsync(ct);
        foreach (var printer in fleet)
        {
            var facts = await PrinterFactsAsync(printer, ct);
            var loaded = facts.Loaded;
            var nozzles = facts.Nozzles;
            var nozzleMatches = part.NozzleDiameterMm is { } needed && nozzles.Contains(needed);
            var model = PrinterModelCatalog.Find(printer.Brand, printer.ModelCode);
            var checks = new List<MatchingCheck>
            {
                Check("pin", "Assignment target", job.AssignmentMode != AssignmentMode.Pinned || job.PinnedPrinterId == printer.Id,
                    job.AssignmentMode == AssignmentMode.Pinned ? $"Pinned to {job.PinnedPrinterId}" : "Any printer", printer.Name),
                Check("availability", "Printer availability", printer.Status == PrinterStatus.Idle, "Idle", printer.Status.ToString(), $"/printers/{printer.Id}"),
                Check("reachability", "Connection", !PrinterModelCatalog.SupportsLiveIntegration(printer.Brand) || printer.IsReachable,
                    "Reachable (live printers)", printer.IsReachable ? "Reachable" : "Not reachable", $"/printers/{printer.Id}"),
                Check("reservation", "Active job reservation", !facts.Reserved,
                    "No active assigned job", facts.Reserved ? "Printer has an active assigned job" : "No active assigned job", $"/printers/{printer.Id}"),
                Check("nozzle-confirmed", "Current connection nozzle telemetry", nozzles.Count > 0, "Confirmed",
                    nozzles.Count == 0 ? "Waiting for nozzle telemetry" : NozzleText(printer, model, nozzles), $"/printers/{printer.Id}"),
                Check("nozzle-match", "Nozzle diameter", nozzleMatches,
                    $"{part.NozzleDiameterMm} mm", nozzles.Count == 0 ? "Unknown" : NozzleText(printer, model, nozzles), $"/printers/{printer.Id}")
            };
            static bool Fits(JobFilamentRequirementSpec spec, LoadedProductionFilament f) =>
                (spec.MaterialType is null || f.MaterialType == spec.MaterialType)
                && (spec.NarrowFilamentVariantId is { } variantId ? f.VariantId == variantId :
                    spec.DetectedColorHex is null || string.Equals(f.ColorHex, spec.DetectedColorHex, StringComparison.OrdinalIgnoreCase));
            foreach (var spec in effective.Requirements)
            {
                var matches = loaded.Any(f => Fits(spec, f));
                var selected = display[checks.Count(c => c.Code == "filament")];
                checks.Add(Check("filament", "Active loaded filament", matches,
                    spec.NarrowFilamentVariantId is null ? $"{spec.MaterialType ?? "Any material"} · {spec.DetectedColorHex ?? "Any colour"}" :
                        selected.SelectedVariantName ?? $"Missing selected variant {spec.NarrowFilamentVariantId}",
                    loaded.Count == 0 ? "No tracked spool on an active feed path" :
                        string.Join(", ", loaded.Select(f => $"{f.MaterialType} · {f.ColorName} ({f.ColorHex})")),
                    $"/printers/{printer.Id}"));
            }
            if (model is { Extruders.Count: > 1 } && part.NozzleDiameterMm is { } jobNozzle)
            {
                // A colour loaded only on extruders with another nozzle cannot feed this job.
                var wrong = effective.Requirements.Where(spec => !loaded.Any(f => f.NozzleMm == jobNozzle && Fits(spec, f)))
                    .SelectMany(spec => loaded.Where(f => Fits(spec, f)))
                    .Select(f => $"{ExtruderLabel(model, f.ExtruderIndex)}: {(f.NozzleMm is { } n ? $"{n} mm" : "unconfirmed")}")
                    .Distinct().ToList();
                checks.Add(new("extruder-nozzle", "Nozzle per extruder", wrong.Count == 0 ? "Passed" : "Failed", $"{jobNozzle} mm",
                    wrong.Count == 0 ? $"{jobNozzle} mm" : string.Join(" · ", wrong),
                    wrong.Count == 0 ? null : $"{string.Join(", ", wrong)}, job needs {jobNozzle} mm.", $"/printers/{printer.Id}"));
            }
            if (nozzleMatches && part.PrintRequirementsStatus == OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete)
            {
                var issue = await dispatchQuery.GetReadinessIssueAsync(printer.Id, part.Id,
                    effective.Requirements.Select(r => r.MatchingValues).ToList(), ct, job.Id);
                // A shortfall is checked last, so the profiles behind it passed.
                var configIssue = issue is { Shortfall: null } ? issue.Message : null;
                var url = configIssue is null ? null : System.Text.RegularExpressions.Regex.Match(configIssue, @"/(?:filament|printers)/[a-fA-F0-9-]+/edit").Value;
                checks.Add(new("profiles", "Production profile readiness", configIssue is null ? "Passed" : "Failed",
                    "Compatible machine and filament assignments; slicing settings when needed",
                    configIssue is null ? "Ready" : "Needs configuration", configIssue, string.IsNullOrEmpty(url) ? null : url));
                if (issue?.Shortfall is { } shortfall)
                    checks.Add(new("filament-stock", "Loaded filament weight", "Failed",
                        $"{shortfall.NeededGrams} g {shortfall.Label} ({shortfall.EstimatedGrams} g + 10%)",
                        $"{shortfall.AvailableGrams} g", issue.Message, $"/filament/spools/{shortfall.SpoolId}"));
            }
            else checks.Add(new("profiles", "Production profile readiness", "NotEvaluated", null, null,
                "Requires complete part requirements and a matching confirmed nozzle."));
            var eligible = jobChecks.All(c => c.Status == "Passed") && checks.All(c => c.Status == "Passed");
            string? scheduling = null;
            if (includeScheduling && eligible && requirement?.Quantity is null)
                scheduling = "Backfill waits for compatible idle capacity after finite work.";
            if (includeScheduling && eligible && requirement?.Quantity is not null)
            {
                var candidates = await dispatchQuery.FilterReadyAsync(printer.Id, await dispatchQuery.GetUnassignedCandidatesAsync(ct), ct);
                var pinned = await dispatchQuery.FilterReadyAsync(printer.Id, await dispatchQuery.GetPinnedCandidatesAsync(printer.Id, ct), ct);
                var next = ProductionDispatchMatcher.SelectNextJob(nozzles, loaded.Select(f => ((string?)f.MaterialType, (string?)f.ColorHex, f.NozzleMm)).ToList(), candidates, pinned);
                if (next != job.Id) scheduling = $"An earlier qualifying job ({next}) is ahead of this job.";
                else if (job.AssignmentMode == AssignmentMode.Unassigned)
                {
                    var others = await dispatchQuery.GetOtherIdleQualifyingPrintersAsync(printer.Id, job.Id, part.NozzleDiameterMm!.Value,
                        effective.Requirements.Select(r => r.MatchingValues).ToList(), ct);
                    if (!ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters(printer.Id, printer.FeedReadings.Where(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit).Sum(u => u.Slots.Count), others))
                        scheduling = "Another qualifying idle printer is preferred (fewer AMS slots, then printer ID).";
                }
                scheduling ??= "Eligible and next for this printer. Dispatch rechecks live configuration before sending.";
            }
            results.Add(new(printer.Id, printer.Name, eligible, checks, scheduling));
        }
        var eligibleAny = results.Any(p => p.Eligible);
        var summary = !jobChecks.All(c => c.Status == "Passed") ? jobChecks.First(c => c.Status != "Passed").Label + ": " + jobChecks.First(c => c.Status != "Passed").Actual :
            eligibleAny ? "A printer matches. Queue order and final dispatch checks determine when it is sent." :
            results.Count == 0 ? "No printers are registered." :
            string.Join(" ", results.Where(p => job.AssignmentMode != AssignmentMode.Pinned || p.PrinterId == job.PinnedPrinterId)
                .Select(p => $"{p.PrinterName}: {string.Join("; ", p.Checks.Where(c => c.Status == "Failed").Select(c => c.Message ?? $"{c.Label}: requires {c.Required}, actual {c.Actual}"))}."));
        if (string.IsNullOrWhiteSpace(summary)) summary = "The pinned printer is no longer available.";
        return new(job.Id, DateTimeOffset.UtcNow, effective.Source, eligibleAny ? "Eligible" : "Blocked", summary, display, jobChecks, results);
    }

    private static MatchingCheck Check(string code, string label, bool passed, string? required, string? actual, string? url = null) =>
        new(code, label, passed ? "Passed" : "Failed", required, actual, null, url);

    // "0.4 mm", or each confirmed extruder's when they differ: "T1 0.4 mm · T2 0.6 mm".
    private static string NozzleText(Printer printer, PrinterModel? model, IReadOnlyList<decimal> nozzles) =>
        nozzles.Count == 1 ? $"{nozzles[0]} mm" : string.Join(" · ", printer.Extruders.Where(e => e.NozzleConfirmed)
            .Select(e => $"{ExtruderLabel(model, e.Index)} {e.NozzleDiameterMm} mm"));

    private static string ExtruderLabel(PrinterModel? model, int index) =>
        model is not null && index < model.Extruders.Count ? model.Extruders[index] : $"Extruder {index + 1}";
}
