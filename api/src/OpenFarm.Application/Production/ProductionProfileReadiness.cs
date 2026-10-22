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
        $"Needs {NeededGrams} g {Label} ({EstimatedGrams} g + 10%) · {printerName} has {AvailableGrams} g. Load more, or correct the weight in /spools/{SpoolId}.";
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
    FeedPlacementResolver placements, ILogger<ProductionProfileReadiness> logger)
{
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
        var requiresSlicing = await RequiresSlicingAsync(part, revisionHash, ct);
        var estimates = requirements.Select(r => ProductionJobRequirements.EstimatedGrams(part, r, requirements.Count, job, revisionHash)).ToList();
        var selections = new List<ProductionFilamentSelection>();
        for (var i = 0; i < requirements.Count; i++)
            selections.Add(await resolver.ResolveLoadedAsync(printerId, requirements[i].MaterialType,
                requirements[i].NarrowFilamentVariantId is null ? requirements[i].DetectedColorHex : null,
                requirements[i].NarrowFilamentVariantId, requirements[i].NarrowFilamentProfileId, ct, estimates[i]));
        if (selections.Count == 0) throw new SlicingProfileUnconfiguredException("No production filament requirements are available.");
        var feeds = new List<FeedPlacement>();
        foreach (var selection in selections)
            if (await placements.ResolveAsync(selection.SpoolId, ct) is { } placement) feeds.Add(placement);
        if (PrintFeedMappingBuilder.MixesAmsWithExternal(feeds))
            throw new SlicingProfileUnconfiguredException("One job can't draw from the AMS and the reloader at once. Load all colours in the AMS, or all in the reloader.");
        if (requiresSlicing && selections.Select(s => ProfileIdentity.Key(s.Profile)).Distinct().Count() > 1)
            throw new SlicingProfileUnconfiguredException("Production slicing with multiple filament profiles is not supported. Use a pre-sliced file.");
        var profiles = requiresSlicing ? await ResolveSettingsAsync(printerId, part, selections[0], ct) : null;
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

    private async Task<bool> RequiresSlicingAsync(Part part, string? revisionHash, CancellationToken ct)
    {
        if (revisionHash is null || revisionHash == part.RevisionHash) return part.RequiresSlicing;
        var path = await storage.GetRevisionStoragePathAsync(revisionHash, part.OriginalFileName, ct);
        await using var file = await storage.OpenReadAsync(path, ct);
        return classifier.Classify(file, Path.GetFileName(path)).RequiresSlicing;
    }

    // The part's own pin, never a fallback: production slices with the slicer the part was pinned to, or not at all.
    private async Task<ResolvedSlicingProfiles> ResolveSettingsAsync(Guid printerId, Part part, ProductionFilamentSelection selection, CancellationToken ct)
    {
        var printer = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
        var pin = slicerSelection.ResolvePinned(part.SlicerEngineOverride, part.SlicerVersionUsed);
        var printerLink = $"Configure in /printers/{printer.Id}/edit.";
        var use = new ProfileUseContext(printer.Brand, printer.ModelCode, printer.ReportedNozzleDiameterMm!.Value, selection.MaterialType);
        try
        {
            return await profileSelection.ResolveProfilesAsync(
                printer.MachineProfileId ?? throw new SlicingProfileUnconfiguredException($"Select a machine profile. {printerLink}"),
                printer.ProcessProfileId ?? throw new SlicingProfileUnconfiguredException($"Select a process profile. {printerLink}"), selection.Profile.Id,
                printer.BuildPlateType ?? throw new SlicingProfileUnconfiguredException($"Select a build plate. {printerLink}"), pin, use, ct);
        }
        catch (ProfileSetBlockedException blocked)
        {
            // Say where each problem is fixed: the printer's machine/process, or the filament's assignment.
            throw new ProfileSetBlockedException(blocked.Issues.Select(issue => issue.Code is ProfileResolutionOutcome.CatalogNotLoaded or ProfileResolutionOutcome.EngineWithoutProfiles
                ? issue
                : issue with { Message = $"{issue.Message} Configure in {(issue.Kind == ProfileKind.Filament ? $"/filament/{selection.FilamentId}/edit" : $"/printers/{printer.Id}/edit")}." }).ToList());
        }
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
            printer.ReportedNozzleDiameterMm,
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
