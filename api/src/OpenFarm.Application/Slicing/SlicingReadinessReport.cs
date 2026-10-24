using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Slicing;

public sealed record GetSlicingReadinessQuery(SlicerEngine Engine, string Version);

/// <param name="Kind">"Machine", "Process" or "Filament"; <c>null</c> for a problem of the whole selection.</param>
/// <param name="Code">A <see cref="ProfileResolutionOutcome"/> name, or "NotSet" for a missing selection.</param>
/// <param name="Link">The page that fixes it.</param>
public sealed record ReadinessIssue(string? Kind, string Code, string Message, string Link);

public sealed record PrinterReadiness(Guid PrinterId, string PrinterName, IReadOnlyList<ReadinessIssue> Issues);

/// <param name="PrinterIds">The printers this assignment applies to (same brand, model and nozzle).</param>
public sealed record FilamentReadiness(
    Guid FilamentId, string FilamentName, string Brand, string ModelCode, decimal NozzleDiameterMm, string? ColorHex,
    string ProfileName, IReadOnlyList<Guid> PrinterIds, IReadOnlyList<ReadinessIssue> Issues);

public sealed record SlicingReadinessReport(SlicerRef Pin, IReadOnlyList<PrinterReadiness> Printers, IReadOnlyList<FilamentReadiness> Filaments)
{
    public bool IsReady => Printers.All(p => p.Issues.Count == 0) && Filaments.All(f => f.Issues.Count == 0);
}

/// <summary>
/// What a part pinned to a slicer could not be sliced with on this farm: for every printer, whether its machine and
/// process presets resolve to that slicer's own catalog, and for every filament assignment, whether its preset does and
/// fits. The filament checks are the very ones production enforces (<see cref="ResolveSlicingProfileSelection.CheckAsync"/>),
/// so a warning here can never disagree with what dispatch does. Read-only.
/// </summary>
public sealed class GetSlicingReadinessQueryHandler(
    ResolveSlicerSelection slicerSelection, IPrinterRepository printers, IFilamentRepository filaments,
    IMachineProfileRepository machines, IProcessProfileRepository processes, IFilamentProfileRepository filamentProfiles,
    ProfileResolver resolver, ProfileCompatibility compatibility, ResolveSlicingProfileSelection selection)
    : IQueryHandler<GetSlicingReadinessQuery, SlicingReadinessReport>
{
    public async Task<SlicingReadinessReport> HandleAsync(GetSlicingReadinessQuery query, CancellationToken cancellationToken)
    {
        var pin = slicerSelection.EnsureAvailable(new SlicerRef(query.Engine, query.Version));
        var fleet = (await printers.ListAsync(cancellationToken)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var allFilaments = await filaments.ListAsync(cancellationToken);
        var printerReports = new List<PrinterReadiness>();
        var assignments = new Dictionary<(Guid FilamentId, Guid AssignmentId), (FilamentProfileAssignment Assignment, Filament Filament, string ProfileName, List<Guid> PrinterIds, List<ReadinessIssue> Issues)>();
        // Identical printers (same selection, plate and hardware) get the same answer: check each combination once, not once per printer.
        var checks = new Dictionary<(Guid Machine, Guid Process, Guid FilamentProfile, BuildPlateType Plate, ProfileUseContext Use), IReadOnlyList<ProfileIssue>>();

        foreach (var printer in fleet)
        {
            printerReports.Add(new(printer.Id, printer.Name, await CheckPrinterAsync(printer, pin, cancellationToken)));
            var nozzles = printer.ReportedNozzlesMm();
            if (printer.MachineProfileId is not { } machineId || printer.ProcessProfileId is not { } processId
                || printer.BuildPlateType is not { } plate || nozzles.Count == 0)
            {
                continue;
            }

            foreach (var filament in allFilaments)
            {
                // ponytail: every assignment at any of the printer's nozzles is checked against its one machine preset
                foreach (var assignment in filament.ProfileAssignments.Where(a =>
                             a.Brand == printer.Brand && a.ModelCode == printer.ModelCode && nozzles.Contains(a.NozzleDiameterMm)))
                {
                    var nozzle = assignment.NozzleDiameterMm;
                    var key = (filament.Id, assignment.Id);
                    if (!assignments.TryGetValue(key, out var entry))
                    {
                        var profile = await filamentProfiles.GetByIdAsync(assignment.FilamentProfileId, cancellationToken);
                        entry = (assignment, filament, profile?.Name ?? "Missing profile", [], []);
                        assignments[key] = entry;
                    }

                    entry.PrinterIds.Add(printer.Id);

                    // The printer's own machine/process problems are listed under the printer; this row keeps what concerns the filament.
                    var use = new ProfileUseContext(printer.Brand, printer.ModelCode, nozzle, filament.MaterialType);
                    var checkKey = (machineId, processId, assignment.FilamentProfileId, plate, use);
                    if (!checks.TryGetValue(checkKey, out var found))
                    {
                        checks[checkKey] = found = await selection.CheckAsync(machineId, processId, assignment.FilamentProfileId, plate, pin, use, cancellationToken);
                    }

                    foreach (var issue in found.Where(i => i.Kind is null or ProfileKind.Filament))
                    {
                        var mapped = new ReadinessIssue(issue.Kind?.ToString(), issue.Code.ToString(), issue.Message, $"/filament/{filament.Id}/edit");
                        if (!entry.Issues.Any(existing => existing.Message == mapped.Message)) entry.Issues.Add(mapped);
                    }
                }
            }
        }

        var filamentReports = assignments.Values
            .OrderBy(e => e.Filament.Material, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Assignment.ModelCode).ThenBy(e => e.Assignment.NozzleDiameterMm)
            .Select(e => new FilamentReadiness(e.Filament.Id, e.Filament.Material, e.Assignment.Brand, e.Assignment.ModelCode,
                e.Assignment.NozzleDiameterMm, e.Assignment.FilamentVariantId is { } variantId
                    ? e.Filament.Variants.FirstOrDefault(v => v.Id == variantId)?.ColorHex : null, e.ProfileName, e.PrinterIds, e.Issues))
            .ToList();
        return new SlicingReadinessReport(pin, printerReports, filamentReports);
    }

    private async Task<IReadOnlyList<ReadinessIssue>> CheckPrinterAsync(Printer printer, SlicerRef pin, CancellationToken ct)
    {
        var issues = new List<ReadinessIssue>();
        var link = $"/printers/{printer.Id}/edit";
        ReadinessIssue Issue(string kind, string code, string message) => new(kind, code, message, link);

        var machine = printer.MachineProfileId is { } machineId ? await machines.GetByIdAsync(machineId, ct) : null;
        var process = printer.ProcessProfileId is { } processId ? await processes.GetByIdAsync(processId, ct) : null;
        if (machine is null) issues.Add(Issue("Machine", "NotSet", "Select a machine profile."));
        if (process is null) issues.Add(Issue("Process", "NotSet", "Select a process profile."));
        if (printer.BuildPlateType is null) issues.Add(Issue("Plate", "NotSet", "Select a build plate."));

        var machineResolution = machine is null ? null : await resolver.ResolveAsync(machine, pin, ct);
        var processResolution = process is null ? null : await resolver.ResolveAsync(process, pin, ct);
        if (machineResolution is { Resolved: null }) issues.Add(Issue("Machine", machineResolution.Outcome.ToString(), machineResolution.Message!));
        if (processResolution is { Resolved: null }) issues.Add(Issue("Process", processResolution.Outcome.ToString(), processResolution.Message!));

        if (machineResolution?.Resolved is { } resolvedMachine)
        {
            if (processResolution?.Resolved is { CompatibleMachineNames: { Count: > 0 } names } && !names.Contains(resolvedMachine.Name, StringComparer.Ordinal))
            {
                issues.Add(Issue("Process", nameof(ProfileResolutionOutcome.Incompatible), "The selected process profile is incompatible with the machine profile."));
            }

            if (printer.ConfirmedNozzlesMm() is { Count: > 0 } nozzles)
            {
                try
                {
                    // Fits when it matches some confirmed nozzle; jobs then need that nozzle.
                    var machineJson = await compatibility.MachineJsonAsync(resolvedMachine, ct);
                    if (!nozzles.Any(nozzle => ProfileCompatibility.MachineJsonMatches(machineJson, printer.Brand, printer.ModelCode, nozzle)))
                    {
                        issues.Add(Issue("Machine", nameof(ProfileResolutionOutcome.Incompatible),
                            $"Machine profile '{resolvedMachine.Name}' does not match {printer.ModelCode} with its reported {string.Join("/", nozzles)} mm nozzle."));
                    }
                }
                catch (SlicingProfileUnconfiguredException ex)
                {
                    issues.Add(Issue("Machine", nameof(ProfileResolutionOutcome.Incompatible), ex.Message));
                }
            }
        }

        return issues;
    }
}
