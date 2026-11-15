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
/// when the file needs slicing — the filament presets resolved to the part's pinned slicer (<see cref="Profiles"/>,
/// with its provenance). The project keeps its own machine and process. The file generator slices with exactly
/// <see cref="Profiles"/>, so what was checked is what runs.
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
    FeedPlacementResolver placements, IAlreadySlicedFileMetadataParser slicedFiles, IUnslicedProjectMaterialResolver projects,
    ILogger<ProductionProfileReadiness> logger)
{
    // ponytail: unbounded memo of each retained file's declared printer model and plate; revisions are immutable and few.
    private static readonly ConcurrentDictionary<string, EstimationDetails?> DeclaredFiles = new(StringComparer.Ordinal);

    public async Task<ProductionResolution> ResolveAsync(Guid printerId, Part part,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, string? revisionHash = null)
    {
        var specs = requirements.Select(r =>
        {
            var narrowing = part.FilamentRequirements.FirstOrDefault(n => n.MaterialType == r.MaterialType && n.DetectedColorHex == r.ColorHex);
            return new JobFilamentRequirementSpec(r.MaterialType, r.ColorHex, narrowing?.NarrowFilamentVariantId, narrowing?.NarrowFilamentProfileId,
                narrowing?.NarrowFilamentId, narrowing?.AnyColor ?? false);
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
                throw new SlicingProfileUnconfiguredException("Exact partial plates require a printer with object-skipping support.");
            if ((driver == PrinterDriver.BambuLan && execution.Manifest.Dialect != "Bambu")
                || (driver == PrinterDriver.SnapmakerMoonraker && execution.Manifest.Dialect != "Klipper"))
                throw new SlicingProfileUnconfiguredException("The object's exclusion format does not match this printer. Supply a compatible sliced file.");
        }
        var requiresSlicing = await RequiresSlicingAsync(part, revisionHash, ct);
        var declared = await EnsureFileFitsPrinterAsync(printerId, part, requiresSlicing, revisionHash, ct);
        var estimates = requirements.Select(r => ProductionJobRequirements.EstimatedGrams(part, r, requirements.Count, job, revisionHash)).ToList();
        // Narrowest choice first, each line preferring a spool no other line took: two "any colour" lines take two spools,
        // and neither takes the one spool a fixed colour needs. A spool is shared only when no other one fits.
        // ponytail: greedy by narrowness, not a full matching; enough for the few lines one print has
        var resolved = new ProductionFilamentSelection?[requirements.Count];
        foreach (var i in Enumerable.Range(0, requirements.Count).OrderBy(i => Breadth(requirements[i])))
            resolved[i] = await resolver.ResolveLoadedAsync(printerId, requirements[i].MaterialType, requirements[i].MatchingValues.ColorHex,
                requirements[i].NarrowFilamentVariantId, requirements[i].NarrowFilamentProfileId, ct, estimates[i], part.NozzleDiameterMm,
                requirements[i].NarrowFilamentId, resolved.OfType<ProductionFilamentSelection>().Select(s => s.SpoolId).ToHashSet());
        var selections = resolved.OfType<ProductionFilamentSelection>().ToList();
        if (selections.Count == 0) throw new SlicingProfileUnconfiguredException("No production filament requirements are available.");
        var feeds = new List<FeedSlotRef>();
        foreach (var selection in selections)
            if (await placements.ResolveAsync(selection.SpoolId, ct) is { } placement) feeds.Add(placement);
        if (PrintFeedMappingBuilder.MixesAmsWithExternal(feeds))
            throw new SlicingProfileUnconfiguredException("One job can't draw from the AMS and the reloader at once. Load all colours in the AMS, or all in the reloader.");
        var profiles = requiresSlicing ? await ResolveSettingsAsync(printerId, part, declared, selections, ct) : null;
        // Last, so a printer short of filament has everything else in order: loading or re-weighing a spool is all it needs.
        await EnsureEnoughFilamentAsync(printerId, selections, estimates, ct);
        return new(selections, profiles);
    }

    /// <summary>How many loaded colours a line accepts: one colour, any colour of one filament, any colour of the material.</summary>
    private static int Breadth(JobFilamentRequirementSpec line) =>
        line.NarrowFilamentVariantId is not null ? 0
        : line.NarrowFilamentId is not null ? 1
        : line.AnyColor || line.DetectedColorHex is null ? 2 : 0;

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
    /// The file is the one source of how a part prints — a pre-sliced file runs as it is, a project is sliced with its own
    /// machine and process — so it only goes to a printer it was made for: the same model, and the plate that is on the
    /// printer's bed. The simulator stands in for every model and plate, and a printer the catalog doesn't know (only test
    /// doubles: registration requires a catalogued model) has none to compare.
    /// </summary>
    /// <returns>What the file declares, for the slice that follows.</returns>
    private async Task<EstimationDetails?> EnsureFileFitsPrinterAsync(Guid printerId, Part part, bool requiresSlicing, string? revisionHash, CancellationToken ct)
    {
        var printer = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
        var declared = await DeclaredAsync(part, requiresSlicing, revisionHash, ct);
        if (PrinterModelCatalog.Find(printer.Brand, printer.ModelCode) is not { } printerModel || printerModel.Driver == PrinterDriver.Simulator)
            return declared;
        var model = PrinterModelCatalog.TryResolveSlicedModel(declared?.MachineModel);
        if (model != printerModel)
        {
            throw new SlicingProfileUnconfiguredException(declared?.MachineModel is not { } fileModel
                ? $"The file names no printer model. Re-save it for {printer.Brand} {printer.ModelCode}."
                : $"File is for {(model is null ? fileModel : $"{model.Brand} {model.ModelCode}")}, not {printer.Brand} {printer.ModelCode}.");
        }

        var plate = printer.BuildPlateType
            ?? throw new SlicingProfileUnconfiguredException($"Select a build plate. Configure in /printers/{printer.Id}?settings=slicer.");
        var wanted = BuildPlateTypeVendorNames.Resolve(plate);
        if (declared?.BuildPlate is not { } filePlate)
            throw new SlicingProfileUnconfiguredException($"The file names no build plate. Re-save it for {wanted}.");
        if (BuildPlateTypeVendorNames.Parse(filePlate) != plate)
            throw new SlicingProfileUnconfiguredException(
                $"File is for {filePlate}, not the printer's {wanted}. Change the plate in /printers/{printer.Id}?settings=slicer, or re-save the file for it.");
        return declared;
    }

    /// <summary>The machine model, machine preset and plate a retained file was made for; null when it cannot be read.</summary>
    private async Task<EstimationDetails?> DeclaredAsync(Part part, bool requiresSlicing, string? revisionHash, CancellationToken ct)
    {
        var hash = revisionHash ?? part.RevisionHash;
        if (DeclaredFiles.TryGetValue(hash, out var cached)) return cached;
        var path = revisionHash is null || revisionHash == part.RevisionHash
            ? part.StoragePath
            : await storage.GetRevisionStoragePathAsync(revisionHash, part.OriginalFileName, ct);
        await using var file = await storage.OpenReadAsync(path, ct);
        EstimationDetails? declared;
        try
        {
            if (requiresSlicing)
            {
                declared = (await projects.ResolveAsync(file, ct)).Estimation;
            }
            else
            {
                var format = classifier.Classify(file, Path.GetFileName(path)).Format;
                file.Position = 0;
                declared = (await slicedFiles.ParseAsync(file, format, ct)).Estimation;
            }
        }
        catch (Exception ex) when (ex is PartFileParsingFailedException or UnslicedProjectMaterialParsingFailedException)
        {
            declared = null;
        }

        return DeclaredFiles.GetOrAdd(hash, declared);
    }

    private async Task<bool> RequiresSlicingAsync(Part part, string? revisionHash, CancellationToken ct)
    {
        if (revisionHash is null || revisionHash == part.RevisionHash) return part.RequiresSlicing;
        var path = await storage.GetRevisionStoragePathAsync(revisionHash, part.OriginalFileName, ct);
        await using var file = await storage.OpenReadAsync(path, ct);
        return classifier.Classify(file, Path.GetFileName(path)).RequiresSlicing;
    }

    // The part's own pin, never a fallback: production slices with the slicer the part was pinned to, or not at all. The
    // project keeps its own machine, process and plate; only each line's filament is resolved and checked, on its own.
    private async Task<ResolvedSlicingProfiles> ResolveSettingsAsync(Guid printerId, Part part, EstimationDetails? declared,
        IReadOnlyList<ProductionFilamentSelection> selections, CancellationToken ct)
    {
        var printer = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
        var pin = slicerSelection.ResolvePinned(part.SlicerEngineOverride, part.SlicerVersionUsed);
        // The plate the file was made for; for a real printer the gate has already made it the printer's own.
        var plate = BuildPlateTypeVendorNames.Parse(declared?.BuildPlate)
            ?? throw new SlicingProfileUnconfiguredException(declared?.BuildPlate is { } name
                ? $"The file's build plate '{name}' is not known." : "The file names no build plate.");
        var resolved = new List<ResolvedSlicingProfiles>();
        var issues = new List<ProfileIssue>();
        foreach (var selection in selections)
        {
            var use = new ProfileUseContext(printer.Brand, printer.ModelCode, selection.NozzleMm, selection.MaterialType);
            try
            {
                resolved.Add(await profileSelection.ResolveFilamentAsync(selection.Profile.Id, plate, declared?.MachineProfileName, pin, use, ct));
            }
            catch (ProfileSetBlockedException blocked)
            {
                issues.AddRange(blocked.Issues.Select(issue => issue.Code is ProfileResolutionOutcome.CatalogNotLoaded or ProfileResolutionOutcome.EngineWithoutProfiles
                    ? issue
                    : issue with { Message = $"{issue.Message} Configure in /filament/{selection.FilamentId}/edit." }));
            }
        }

        if (issues.Count > 0) throw new ProfileSetBlockedException(issues.Distinct().ToList());
        return resolved[0] with { Filaments = resolved.Select(r => new ResolvedFilament(r.FilamentSettingsJson, r.Filament)).ToList() };
    }

    public Task<string> GetConfigurationStampAsync(Guid printerId, Part part,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, string? revisionHash = null) =>
        GetConfigurationStampAsync(printerId, part, requirements.Select(r =>
        {
            var narrowing = part.FilamentRequirements.FirstOrDefault(n => n.MaterialType == r.MaterialType && n.DetectedColorHex == r.ColorHex);
            return new JobFilamentRequirementSpec(r.MaterialType, r.ColorHex, narrowing?.NarrowFilamentVariantId, narrowing?.NarrowFilamentProfileId,
                narrowing?.NarrowFilamentId, narrowing?.AnyColor ?? false);
        }).ToList(), ct, revisionHash);

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
