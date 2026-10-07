using System.Text.Json.Nodes;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Parts;

/// <summary>The three real, flattened (ancestor-chain-merged) preset JSON blobs OrcaSlicer/Bambu Studio need to slice, plus the selected profiles' own names (for logging/diagnostics — never sliced into the CLI args).</summary>
public sealed record ResolvedSlicingProfiles(
    string MachineSettingsJson, string ProcessSettingsJson, string FilamentSettingsJson,
    string MachineProfileName, string ProcessProfileName, string FilamentProfileName)
{
    public ProfileSourceFormat MachineSourceFormat { get; init; } = ProfileSourceFormat.OrcaSlicer;
    public ProfileSourceFormat ProcessSourceFormat { get; init; } = ProfileSourceFormat.OrcaSlicer;
    public ProfileSourceFormat FilamentSourceFormat { get; init; } = ProfileSourceFormat.OrcaSlicer;
}

/// <summary>Resolves production settings for an explicitly assigned printer, or flattens explicitly
/// selected profiles for library estimation. Library estimates never select from fleet or inventory.</summary>
public sealed class ResolveSlicingProfileSelection(
    IPrinterRepository printerRepository,
    IMachineProfileRepository machineProfileRepository,
    IProcessProfileRepository processProfileRepository,
    IFilamentProfileRepository filamentProfileRepository,
    RepresentativeFilamentResolver representativeFilamentResolver, ProfileCompatibility compatibility)
{
    public async Task<ResolvedSlicingProfiles> ResolveAsync(
        Guid? printerId, string materialType, Guid? narrowedFilamentProfileId, CancellationToken cancellationToken, SlicerEngine? engine = null)
    {
        if (printerId is not { } targetPrinterId)
            throw new SlicingProfileUnconfiguredException("Production slicing requires an assigned printer; use explicit estimation settings for library estimates.");
        var (machineProfileId, processProfileId, buildPlateType) = await ResolveForPrinterAsync(targetPrinterId, cancellationToken);

        var filamentProfile = (await representativeFilamentResolver.ResolveLoadedAsync(targetPrinterId, materialType,
            null, null, narrowedFilamentProfileId, cancellationToken)).Profile;
        return await ResolveProfilesAsync(machineProfileId, processProfileId, filamentProfile.Id, buildPlateType, cancellationToken, engine);
    }

    public async Task<ResolvedSlicingProfiles> ResolveProfilesAsync(Guid machineProfileId, Guid processProfileId,
        Guid filamentProfileId, BuildPlateType buildPlateType, CancellationToken cancellationToken, SlicerEngine? engine = null)
    {
        var machineProfile = await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The selected machine profile no longer exists.");
        var processProfile = await processProfileRepository.GetByIdAsync(processProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The selected process profile no longer exists.");
        var filamentProfile = await filamentProfileRepository.GetByIdAsync(filamentProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The selected filament profile no longer exists.");
        if (!machineProfile.IsSelectable || !machineProfile.IsCurrentlyOffered || !processProfile.IsSelectable || !processProfile.IsCurrentlyOffered
            || !filamentProfile.IsSelectable || !filamentProfile.IsCurrentlyOffered)
            throw new SlicingProfileUnconfiguredException("Selected slicing profiles must be current and selectable.");
        ProfileEngineCompatibility.EnsureSupported(engine ?? ProfileEngineCompatibility.NativeEngine(machineProfile.SourceFormat),
            machineProfile.SourceFormat, processProfile.SourceFormat, filamentProfile.SourceFormat);
        if (processProfile.CompatibleMachineNames is { Count: > 0 } names && !names.Contains(machineProfile.Name, StringComparer.Ordinal))
            throw new SlicingProfileUnconfiguredException("The selected process profile is incompatible with the machine profile.");
        var machineJson = await MergeMachineChainWithIncludesOrThrowAsync(machineProfile, cancellationToken);
        var processJson = await MergeChainOrThrowAsync(processProfile.Id, "process", FetchProcessNode, cancellationToken);
        var filamentJson = await MergeFilamentChainWithIncludesOrThrowAsync(filamentProfile, cancellationToken);

        // 012 US2 — pre-flight validate the resolved filament preset actually declares a real
        // (non-"0") bed temperature for the printer's own selected plate (research.md R3) before
        // injecting curr_bed_type into the merged process settings, so OrcaSlicer/Bambu Studio's own
        // CLI resolves the correct plate-specific bed temperature at slice time (FR-005).
        var tempKey = BuildPlateTypeTempKeys.Resolve(buildPlateType);
        var tempValue = JsonNode.Parse(filamentJson)!.AsObject()[tempKey] switch
        {
            JsonArray array when array.Count > 0 => array[0]?.GetValue<string>(),
            JsonValue value => value.GetValue<string>(),
            _ => null,
        };

        if (tempValue is null or "0")
        {
            throw new SlicingProfileUnconfiguredException(
                $"Filament profile '{filamentProfile.Name}' has no bed-temperature value for build plate '{BuildPlateTypeVendorNames.Resolve(buildPlateType)}'.");
        }

        var processSettings = JsonNode.Parse(processJson)!.AsObject();
        processSettings["curr_bed_type"] = BuildPlateTypeVendorNames.Resolve(buildPlateType);
        processJson = processSettings.ToJsonString();

        return new ResolvedSlicingProfiles(
            machineJson, processJson, filamentJson,
            machineProfile.Name, processProfile.Name, filamentProfile.Name)
        {
            MachineSourceFormat = machineProfile.SourceFormat,
            ProcessSourceFormat = processProfile.SourceFormat,
            FilamentSourceFormat = filamentProfile.SourceFormat,
        };
    }

    private Task<string> MergeMachineChainWithIncludesOrThrowAsync(MachineProfile target, CancellationToken ct) =>
        MergeWithIncludesAsync(target.Id, target.IncludedTemplateNames, FetchMachineNode, async (name, token) =>
        {
            var rows = await machineProfileRepository.FindByNameAsync(target.SourceFormat, name, token);
            var preferred = rows.Where(row => row.Origin == ProfileOrigin.System && row.VendorName == target.VendorName).ToList();
            var candidates = preferred.Count > 0 ? preferred : rows.ToList();
            return candidates.Count == 1 ? candidates[0].Id : (Guid?)null;
        }, ct);

    private Task<string> MergeFilamentChainWithIncludesOrThrowAsync(FilamentProfile target, CancellationToken ct) =>
        MergeWithIncludesAsync(target.Id, target.IncludedTemplateNames, FetchFilamentNode, async (name, token) =>
        {
            var rows = await filamentProfileRepository.FindByNameAsync(target.SourceFormat, name, token);
            var preferred = rows.Where(row => row.Origin == ProfileOrigin.System && row.VendorName == target.VendorName).ToList();
            var candidates = preferred.Count > 0 ? preferred : rows.ToList();
            return candidates.Count == 1 ? candidates[0].Id : (Guid?)null;
        }, ct);

    // Resolve templates in each inherited layer using that profile's source catalog. A missing,
    // ambiguous or cyclical template must never be silently passed to a different engine.
    private static async Task<string> MergeWithIncludesAsync(Guid id, IReadOnlyList<string>? declaredIncludes,
        ProfileChainWalker.FetchById fetch, Func<string, CancellationToken, Task<Guid?>> findTemplate,
        CancellationToken ct, HashSet<Guid>? visiting = null)
    {
        visiting ??= [];
        if (visiting.Count >= 32 || !visiting.Add(id))
            throw new SlicingProfileUnconfiguredException("The selected profile has a cyclical or excessively deep template chain.");
        try
        {
            var (chain, unresolved) = await ProfileChainWalker.WalkByIdAsync(id, fetch, ct);
            if (unresolved)
                throw new SlicingProfileUnconfiguredException("The selected profile has an unresolved ancestor in its inheritance chain.");
            var layers = new List<string>();
            for (var index = 0; index < chain.Count; index++)
            {
                using var json = System.Text.Json.JsonDocument.Parse(chain[index]);
                var includes = ProfileCompatibility.ReadStrings(json.RootElement, "include");
                if (index == chain.Count - 1 && includes.Count == 0 && declaredIncludes is not null)
                    includes = declaredIncludes;
                foreach (var name in includes)
                {
                    var templateId = await findTemplate(name, ct)
                        ?? throw new SlicingProfileUnconfiguredException($"Included profile template '{name}' is missing or ambiguous in its source catalog.");
                    layers.Add(await MergeWithIncludesAsync(templateId, null, fetch, findTemplate, ct, visiting));
                }
                layers.Add(chain[index]);
            }
            return ProfileRawDataMerger.MergeAncestorChain(layers);
        }
        finally { visiting.Remove(id); }
    }

    private async Task<(Guid MachineProfileId, Guid ProcessProfileId, BuildPlateType BuildPlateType)> ResolveForPrinterAsync(Guid printerId, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(printerId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException($"Printer '{printerId}' was not found.");

        var machineProfileId = printer.MachineProfileId
            ?? throw new SlicingProfileUnconfiguredException($"Printer '{printer.Name}' has no machine profile selected.");
        var processProfileId = printer.ProcessProfileId
            ?? throw new SlicingProfileUnconfiguredException($"Printer '{printer.Name}' has no process profile selected.");
        var buildPlateType = printer.BuildPlateType
            ?? throw new SlicingProfileUnconfiguredException($"Printer '{printer.Name}' has no build plate selected.");

        var machine = await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The selected machine profile no longer exists.");
        await compatibility.ValidateMachineAsync(printer, machine, cancellationToken);
        return (machineProfileId, processProfileId, buildPlateType);
    }

    private static async Task<string> MergeChainOrThrowAsync(
        Guid id, string kindLabel, ProfileChainWalker.FetchById fetch, CancellationToken cancellationToken)
    {
        var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(id, fetch, cancellationToken);
        if (hasUnresolvedBase)
        {
            throw new SlicingProfileUnconfiguredException(
                $"The selected {kindLabel} profile has an unresolved ancestor in its inheritance chain and cannot be used to slice.");
        }

        return ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
    }

    private async Task<ProfileChainNode?> FetchMachineNode(Guid id, CancellationToken cancellationToken)
    {
        var row = await machineProfileRepository.GetByIdAsync(id, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
    }

    private async Task<ProfileChainNode?> FetchProcessNode(Guid id, CancellationToken cancellationToken)
    {
        var row = await processProfileRepository.GetByIdAsync(id, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
    }

    private async Task<ProfileChainNode?> FetchFilamentNode(Guid id, CancellationToken cancellationToken)
    {
        var row = await filamentProfileRepository.GetByIdAsync(id, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
    }
}
