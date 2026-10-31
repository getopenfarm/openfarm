using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Profiles;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// What production uses for one job on one printer: the loaded spool and filament profile of each requirement, and —
/// when the file needs slicing — the machine, process and filament presets resolved to the part's pinned slicer
/// (<see cref="Profiles"/>, with its provenance). The file generator slices with exactly <see cref="Profiles"/>, so
/// what was checked is what runs.
/// </summary>
public sealed record ProductionResolution(IReadOnlyList<ProductionFilamentSelection> Selections, ResolvedSlicingProfiles? Profiles);

/// <summary>Why a job cannot go to a printer. <see cref="Shortfall"/> is set when the reason is too little loaded filament.</summary>
public sealed record ReadinessIssue(string Message, FilamentShortfall? Shortfall = null);

/// <summary>What the job takes of one filament against what the loaded spool, with continuation, can give.</summary>
public sealed record FilamentShortfall(Guid SpoolId, string Label, decimal EstimatedGrams, decimal NeededGrams, decimal AvailableGrams)
{
    /// <summary>The need rounds up and the stock down, so a shortfall never reads "needs 120 g, has 120 g".</summary>
    public static FilamentShortfall Of(ProductionFilamentSelection selection, decimal estimatedGrams, decimal neededGrams, decimal availableGrams) =>
        new(selection.SpoolId, selection.Label, decimal.Round(estimatedGrams), decimal.Ceiling(neededGrams), decimal.Floor(availableGrams));

    public string Describe(string printerName) =>
        $"Needs {NeededGrams} g {Label} ({EstimatedGrams} g + 10%) · {printerName} has {AvailableGrams} g. Load more, or correct the weight in /filament/spools/{SpoolId}.";
}

/// <summary>The loaded filament cannot cover a job: the printer is skipped for it, and a job pinned to it waits.</summary>
public sealed class InsufficientFilamentException(FilamentShortfall shortfall, string message)
    : SlicingProfileUnconfiguredException(message, "InsufficientFilament")
{
    public FilamentShortfall Shortfall { get; } = shortfall;
}

public sealed partial class ProductionProfileReadiness(RepresentativeFilamentResolver resolver, IPrinterRepository printers,
    ResolveSlicingProfileSelection profileSelection, ProfileCompatibility compatibility,
    ResolveSlicerSelection slicerSelection, IPartFileStorage storage, IPartFileClassifier classifier,
    FeedPlacementResolver placements, IAlreadySlicedFileMetadataParser slicedFiles, ILogger<ProductionProfileReadiness> logger, IObjectSkippingPolicy? objectSkipping = null)
{
    // ponytail: unbounded memo of each retained file's declared printer model; revisions are immutable and few.
    private static readonly ConcurrentDictionary<string, string?> SlicedModels = new(StringComparer.Ordinal);

    public async Task<ProductionResolution> ResolveAsync(Guid printerId, Part part,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, string? revisionHash = null)
    {
        var specs = requirements.Select(r =>
        {
            var narrowing = part.FilamentRequirements.FirstOrDefault(n => n.MaterialType == r.MaterialType && n.DetectedColorHex == r.ColorHex);
            return new JobFilamentRequirementSpec(r.MaterialType, r.ColorHex, narrowing?.NarrowFilamentVariantId, narrowing?.NarrowFilamentProfileId);
        }).ToList();
        return await ResolveAsync(printerId, part, specs, ct, revisionHash);
    }

    /// <param name="job">The job being placed: grams it was already sliced to for this revision take precedence over the part's estimate.</param>
    public async Task<ProductionResolution> ResolveAsync(Guid printerId, Part part,
        IReadOnlyList<JobFilamentRequirementSpec> requirements, CancellationToken ct, string? revisionHash = null, PrintJob? job = null)
    {
        if (job?.ObjectExecution is { } execution)
        {
            var target = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
            PrinterDriver? driver = PrinterModelCatalog.SupportsLiveIntegration(target.Brand) ? PrinterModelCatalog.Resolve(target.Brand, target.ModelCode).Driver : null;
            if (execution.PlannedExclusions.Count > 0 && driver is not (PrinterDriver.BambuLan or PrinterDriver.SnapmakerMoonraker))
                throw new SlicingProfileUnconfiguredException("Exact partial plates require a printer with verified object-skipping support.");
            if ((driver == PrinterDriver.BambuLan && execution.Manifest.Dialect != "Bambu")
                || (driver == PrinterDriver.SnapmakerMoonraker && execution.Manifest.Dialect != "Klipper"))
                throw new SlicingProfileUnconfiguredException("The object's exclusion format does not match this printer. Supply a compatible sliced file.");
            if (execution.PlannedExclusions.Count > 0)
            {
                if (objectSkipping is null) throw new SlicingProfileUnconfiguredException("Object-skipping support is not configured.");
                try { objectSkipping.EnsureVerified(new(target.Brand, target.ModelCode, target.IpAddress, target.SerialNumber, ""), false); }
                catch (InvalidProductionDemandException ex) { throw new SlicingProfileUnconfiguredException(ex.Message); }
            }
        }
        var requiresSlicing = await RequiresSlicingAsync(part, revisionHash, ct);
        if (!requiresSlicing) await EnsureSlicedForPrinterAsync(printerId, part, revisionHash, ct);
        var estimates = requirements.Select(r => ProductionJobRequirements.EstimatedGrams(part, r, requirements.Count, job, revisionHash)).ToList();
        var selections = new List<ProductionFilamentSelection>();
        for (var i = 0; i < requirements.Count; i++)
            selections.Add(await resolver.ResolveLoadedAsync(printerId, requirements[i].MaterialType,
                requirements[i].NarrowFilamentVariantId is null ? requirements[i].DetectedColorHex : null,
                requirements[i].NarrowFilamentVariantId, requirements[i].NarrowFilamentProfileId, ct, estimates[i], part.NozzleDiameterMm));
        if (selections.Count == 0) throw new SlicingProfileUnconfiguredException("No production filament requirements are available.");
        var feeds = new List<FeedSlotRef>();
        foreach (var selection in selections)
            if (await placements.ResolveAsync(selection.SpoolId, ct) is { } placement) feeds.Add(placement);
        if (PrintFeedMappingBuilder.MixesAmsWithExternal(feeds))
            throw new SlicingProfileUnconfiguredException("One job can't draw from the AMS and the reloader at once. Load all colours in the AMS, or all in the reloader.");
        var profiles = requiresSlicing ? await ResolveSettingsAsync(printerId, part, selections, ct) : null;
        // Last, so a printer short of filament has everything else in order: loading or re-weighing a spool is all it needs.
        await EnsureEnoughFilamentAsync(printerId, selections, estimates, ct);
        return new(selections, profiles);
    }

    private async Task EnsureEnoughFilamentAsync(Guid printerId, List<ProductionFilamentSelection> selections,
        List<decimal?> estimates, CancellationToken ct)
    {
        if (FilamentCover.FirstShortfall(selections.Select((s, i) => (s.FeedUnitId, s.FilamentVariantId, s.AvailableGrams, estimates[i])).ToList())
            is not { } shortfall) return;
        var details = FilamentShortfall.Of(selections[shortfall.Line], shortfall.Estimated, shortfall.Needed, shortfall.Available);
        var printer = await printers.GetByIdAsync(printerId, ct);
        throw new InsufficientFilamentException(details, details.Describe(printer?.Name ?? "Printer"));
    }

    /// <summary>
    /// A pre-sliced file runs exactly as it is, so it must have been sliced for this printer's model; a file that still
    /// needs slicing is sliced for whichever printer takes it. The simulator stands in for every model, and a printer the
    /// catalog doesn't know (only test doubles: registration requires a catalogued model) has no model to compare.
    /// </summary>
    private async Task EnsureSlicedForPrinterAsync(Guid printerId, Part part, string? revisionHash, CancellationToken ct)
    {
        var printer = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
        if (PrinterModelCatalog.Find(printer.Brand, printer.ModelCode) is not { } printerModel || printerModel.Driver == PrinterDriver.Simulator)
            return;
        var declared = await SlicedModelAsync(part, revisionHash, ct);
        var model = PrinterModelCatalog.TryResolveSlicedModel(declared);
        if (model == printerModel) return;
        throw new SlicingProfileUnconfiguredException(declared is null
            ? $"The file names no printer model. Slice it for {printer.Brand} {printer.ModelCode}."
            : $"Sliced for {(model is null ? declared : $"{model.Brand} {model.ModelCode}")}, not {printer.Brand} {printer.ModelCode}.");
    }

    private async Task<string?> SlicedModelAsync(Part part, string? revisionHash, CancellationToken ct)
    {
        var hash = revisionHash ?? part.RevisionHash;
        if (SlicedModels.TryGetValue(hash, out var cached)) return cached;
        var path = revisionHash is null || revisionHash == part.RevisionHash
            ? part.StoragePath
            : await storage.GetRevisionStoragePathAsync(revisionHash, part.OriginalFileName, ct);
        await using var file = await storage.OpenReadAsync(path, ct);
        var format = classifier.Classify(file, Path.GetFileName(path)).Format;
        file.Position = 0;
        string? declared;
        try
        {
            declared = (await slicedFiles.ParseAsync(file, format, ct)).Estimation?.MachineModel;
        }
        catch (PartFileParsingFailedException)
        {
            declared = null;
        }

        return SlicedModels.GetOrAdd(hash, declared);
    }

    private async Task<bool> RequiresSlicingAsync(Part part, string? revisionHash, CancellationToken ct)
    {
        if (revisionHash is null || revisionHash == part.RevisionHash) return part.RequiresSlicing;
        var path = await storage.GetRevisionStoragePathAsync(revisionHash, part.OriginalFileName, ct);
        await using var file = await storage.OpenReadAsync(path, ct);
        return classifier.Classify(file, Path.GetFileName(path)).RequiresSlicing;
    }

    // The part's own pin, never a fallback: production slices with the slicer the part was pinned to, or not at all. Each
    // line's filament is resolved and checked on its own; the first line's set carries the machine and process.
    private async Task<ResolvedSlicingProfiles> ResolveSettingsAsync(Guid printerId, Part part,
        IReadOnlyList<ProductionFilamentSelection> selections, CancellationToken ct)
    {
        var printer = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
        var pin = slicerSelection.ResolvePinned(part.SlicerEngineOverride, part.SlicerVersionUsed);
        var printerLink = $"Configure in /printers/{printer.Id}/edit.";
        var machineProfileId = printer.MachineProfileId ?? throw new SlicingProfileUnconfiguredException($"Select a machine profile. {printerLink}");
        var processProfileId = printer.ProcessProfileId ?? throw new SlicingProfileUnconfiguredException($"Select a process profile. {printerLink}");
        var buildPlate = printer.BuildPlateType ?? throw new SlicingProfileUnconfiguredException($"Select a build plate. {printerLink}");
        var resolved = new List<ResolvedSlicingProfiles>();
        var issues = new List<ProfileIssue>();
        foreach (var selection in selections)
        {
            var use = new ProfileUseContext(printer.Brand, printer.ModelCode, selection.NozzleMm, selection.MaterialType);
            try
            {
                resolved.Add(await profileSelection.ResolveProfilesAsync(machineProfileId, processProfileId, selection.Profile.Id, buildPlate, pin, use, ct));
            }
            catch (ProfileSetBlockedException blocked)
            {
                // Say where each problem is fixed: the printer's machine/process, or this line's filament assignment.
                issues.AddRange(blocked.Issues.Select(issue => issue.Code is ProfileResolutionOutcome.CatalogNotLoaded or ProfileResolutionOutcome.EngineWithoutProfiles
                    ? issue
                    : issue with { Message = $"{issue.Message} Configure in {(issue.Kind == ProfileKind.Filament ? $"/filament/{selection.FilamentId}/edit" : $"/printers/{printer.Id}/edit")}." }));
            }
        }

        // Machine and process problems repeat for every line; each is reported once.
        if (issues.Count > 0) throw new ProfileSetBlockedException(issues.Distinct().ToList());
        return resolved[0] with { Filaments = resolved.Select(r => new ResolvedFilament(r.FilamentSettingsJson, r.Filament)).ToList() };
    }

    public Task<string> GetConfigurationStampAsync(Guid printerId, Part part,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, string? revisionHash = null) =>
        GetConfigurationStampAsync(printerId, part, requirements.Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.ColorHex,
            part.FilamentRequirements.FirstOrDefault(n => n.MaterialType == r.MaterialType && n.DetectedColorHex == r.ColorHex)?.NarrowFilamentVariantId,
            part.FilamentRequirements.FirstOrDefault(n => n.MaterialType == r.MaterialType && n.DetectedColorHex == r.ColorHex)?.NarrowFilamentProfileId)).ToList(), ct, revisionHash);

    public async Task<string> GetConfigurationStampAsync(Guid printerId, Part part,
        IReadOnlyList<JobFilamentRequirementSpec> requirements, CancellationToken ct, string? revisionHash = null, PrintJob? job = null) =>
        await StampAsync(printerId, await ResolveAsync(printerId, part, requirements, ct, revisionHash, job), ct);

    /// <summary>
    /// A fingerprint of everything a production file depends on: the printer's confirmed configuration, the loaded
    /// spools and their filament presets, and the resolved slicing presets with their provenance (pin, selected and
    /// resolved ids, names, <c>setting_id</c>s, versions, outcomes). Two equal stamps mean the same file would be produced.
    /// </summary>
    public async Task<string> StampAsync(Guid printerId, ProductionResolution resolution, CancellationToken ct)
    {
        var printer = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
        var filamentSettings = new List<string>();
        foreach (var selection in resolution.Selections) filamentSettings.Add(await compatibility.FilamentJsonAsync(selection.Profile, ct));
        var json = JsonSerializer.Serialize(new
        {
            // Each extruder's nozzle, never its temperature or active slot.
            Nozzles = printer.Extruders.Select(e => new { e.Index, e.NozzleDiameterMm }),
            printer.MachineProfileId,
            printer.ProcessProfileId,
            printer.BuildPlateType,
            settings = resolution.Profiles,
            slicer = resolution.Profiles?.Pin,
            filamentSettings,
            Selections = resolution.Selections.Select(s => new { s.SpoolId, s.FilamentVariantId, s.Profile.Id })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public Task<ReadinessIssue?> GetIssueAsync(Guid printerId, Part part,
        IReadOnlyList<JobFilamentRequirementSpec> requirements, CancellationToken ct, string? revisionHash = null, PrintJob? job = null) =>
        IssueAsync(printerId, () => ResolveAsync(printerId, part, requirements, ct, revisionHash, job));

    public Task<ReadinessIssue?> GetIssueAsync(Guid printerId, Part part,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, string? revisionHash = null) =>
        IssueAsync(printerId, () => ResolveAsync(printerId, part, requirements, ct, revisionHash));

    // Whatever goes wrong while checking is a reason this job cannot go to this printer — never an unhandled error that
    // takes the queue page down or stops the dispatcher from looking at the jobs behind this one.
    private async Task<ReadinessIssue?> IssueAsync(Guid printerId, Func<Task<ProductionResolution>> resolve)
    {
        try { await resolve(); return null; }
        catch (InsufficientFilamentException ex) { return new(ex.Message, ex.Shortfall); }
        catch (SlicingProfileUnconfiguredException ex) { return new(ex.Message); }
        catch (SlicerUnavailableException ex) { return new(ex.Message); }
        catch (UnsupportedPartFileFormatException ex) { return new(ex.Message); }
        catch (IOException) { return new("The retained job file is unavailable. Review or replace the part file."); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReadinessCheckFailed(logger, printerId, ex);
            return new("Production settings check failed. See the server log.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking production settings for printer {PrinterId} failed unexpectedly.")]
    private static partial void LogReadinessCheckFailed(ILogger logger, Guid printerId, Exception exception);
}
