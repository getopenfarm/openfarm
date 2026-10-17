using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Organizations;
using OpenFarm.Domain.Profiles;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class ProductionProfileReadiness(RepresentativeFilamentResolver resolver, IPrinterRepository printers,
    ResolveSlicingProfileSelection profileSelection, ProfileCompatibility compatibility, IOrganizationRepository organizations,
    ResolveSlicerSelection slicerSelection, IPartFileStorage storage, IPartFileClassifier classifier, IProfileEngineValidator engineValidator,
    FeedPlacementResolver placements)
{
    public async Task<IReadOnlyList<ProductionFilamentSelection>> ResolveAsync(Guid printerId, Part part,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, string? revisionHash = null)
    {
        var specs = requirements.Select(r =>
        {
            var narrowing = part.FilamentRequirements.FirstOrDefault(n => n.MaterialType == r.MaterialType && n.DetectedColorHex == r.ColorHex);
            return new JobFilamentRequirementSpec(r.MaterialType, r.ColorHex, narrowing?.NarrowFilamentVariantId, narrowing?.NarrowFilamentProfileId);
        }).ToList();
        return await ResolveAsync(printerId, part, specs, ct, revisionHash);
    }

    public async Task<IReadOnlyList<ProductionFilamentSelection>> ResolveAsync(Guid printerId, Part part,
        IReadOnlyList<JobFilamentRequirementSpec> requirements, CancellationToken ct, string? revisionHash = null)
    {
        var requiresSlicing = await RequiresSlicingAsync(part, revisionHash, ct);
        var selections = new List<ProductionFilamentSelection>();
        foreach (var requirement in requirements)
            selections.Add(await resolver.ResolveLoadedAsync(printerId, requirement.MaterialType,
                requirement.NarrowFilamentVariantId is null ? requirement.DetectedColorHex : null,
                requirement.NarrowFilamentVariantId, requirement.NarrowFilamentProfileId, ct));
        if (selections.Count == 0) throw new SlicingProfileUnconfiguredException("No production filament requirements are available.");
        var feeds = new List<FeedPlacement>();
        foreach (var selection in selections)
            if (await placements.ResolveAsync(selection.SpoolId, ct) is { } placement) feeds.Add(placement);
        if (PrintFeedMappingBuilder.MixesAmsWithExternal(feeds))
            throw new SlicingProfileUnconfiguredException("One job can't draw from the AMS and the reloader at once. Load all colours in the AMS, or all in the reloader.");
        if (requiresSlicing && selections.Select(s => s.Profile.Id).Distinct().Count() > 1)
            throw new SlicingProfileUnconfiguredException("Production slicing with multiple filament profiles is not supported. Use a pre-sliced file.");
        if (requiresSlicing) await ResolveSettingsAsync(printerId, part, selections[0], ct);
        return selections;
    }

    private async Task<bool> RequiresSlicingAsync(Part part, string? revisionHash, CancellationToken ct)
    {
        if (revisionHash is null || revisionHash == part.RevisionHash) return part.RequiresSlicing;
        var path = await storage.GetRevisionStoragePathAsync(part.OrganizationId, revisionHash, part.OriginalFileName, ct);
        await using var file = await storage.OpenReadAsync(path, ct);
        return classifier.Classify(file, Path.GetFileName(path)).RequiresSlicing;
    }

    private async Task<(ResolvedSlicingProfiles Profiles, SlicerRef Slicer)> ResolveSettingsAsync(Guid printerId, Part part,
        ProductionFilamentSelection selection, CancellationToken ct)
    {
        var printer = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
        var organization = await organizations.GetAsync(ct);
        var slicer = slicerSelection.Resolve(part.SlicerEngineOverride, part.SlicerVersionUsed, organization.DefaultSlicer);
        var profiles = await profileSelection.ResolveProfilesAsync(
            printer.MachineProfileId ?? throw new SlicingProfileUnconfiguredException("Select a machine profile."),
            printer.ProcessProfileId ?? throw new SlicingProfileUnconfiguredException("Select a process profile."), selection.Profile.Id,
            printer.BuildPlateType ?? throw new SlicingProfileUnconfiguredException("Select a build plate."), ct, slicer.Engine);
        try { await engineValidator.ValidateAsync(slicer, profiles, selection.MaterialType, printer.ReportedNozzleDiameterMm!.Value, ct); }
        catch (SlicingProfileUnconfiguredException ex)
        { throw new SlicingProfileUnconfiguredException($"{ex.Message} Configure profiles at /filament/{selection.FilamentId}/edit."); }
        return (profiles, slicer);
    }

    public Task<string> GetConfigurationStampAsync(Guid printerId, Part part,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, string? revisionHash = null) =>
        GetConfigurationStampAsync(printerId, part, requirements.Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.ColorHex,
            part.FilamentRequirements.FirstOrDefault(n => n.MaterialType == r.MaterialType && n.DetectedColorHex == r.ColorHex)?.NarrowFilamentVariantId,
            part.FilamentRequirements.FirstOrDefault(n => n.MaterialType == r.MaterialType && n.DetectedColorHex == r.ColorHex)?.NarrowFilamentProfileId)).ToList(), ct, revisionHash);

    public async Task<string> GetConfigurationStampAsync(Guid printerId, Part part,
        IReadOnlyList<JobFilamentRequirementSpec> requirements, CancellationToken ct, string? revisionHash = null)
    {
        var selections = await ResolveAsync(printerId, part, requirements, ct, revisionHash);
        var printer = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
        ResolvedSlicingProfiles? settings = null;
        SlicerRef? slicer = null;
        if (await RequiresSlicingAsync(part, revisionHash, ct)) (settings, slicer) = await ResolveSettingsAsync(printerId, part, selections[0], ct);
        var filamentSettings = new List<string>();
        foreach (var selection in selections) filamentSettings.Add(await compatibility.FilamentJsonAsync(selection.Profile, ct));
        var json = JsonSerializer.Serialize(new
        {
            printer.ReportedNozzleDiameterMm,
            printer.MachineProfileId,
            printer.ProcessProfileId,
            printer.BuildPlateType,
            settings,
            slicer,
            filamentSettings,
            Selections = selections.Select(s => new { s.SpoolId, s.FilamentVariantId, s.Profile.Id })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public async Task<string?> GetIssueAsync(Guid printerId, Part part,
        IReadOnlyList<JobFilamentRequirementSpec> requirements, CancellationToken ct, string? revisionHash = null)
    {
        try { await ResolveAsync(printerId, part, requirements, ct, revisionHash); return null; }
        catch (SlicingProfileUnconfiguredException ex) { return ex.Message; }
        catch (SlicerUnavailableException ex) { return ex.Message; }
        catch (UnsupportedPartFileFormatException ex) { return ex.Message; }
        catch (IOException) { return "The retained job file is unavailable. Review or replace the part file."; }
    }

    public async Task<string?> GetIssueAsync(Guid printerId, Part part,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> requirements, CancellationToken ct, string? revisionHash = null)
    {
        try { await ResolveAsync(printerId, part, requirements, ct, revisionHash); return null; }
        catch (SlicingProfileUnconfiguredException ex) { return ex.Message; }
        catch (SlicerUnavailableException ex) { return ex.Message; }
        catch (UnsupportedPartFileFormatException ex) { return ex.Message; }
        catch (IOException) { return "The retained job file is unavailable. Review or replace the part file."; }
    }
}
