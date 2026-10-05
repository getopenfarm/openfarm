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
    string MachineProfileName, string ProcessProfileName, string FilamentProfileName);

/// <summary>Resolves production settings for an explicitly assigned printer, or flattens explicitly
/// selected profiles for library estimation. Library estimates never select from fleet or inventory.</summary>
public sealed class ResolveSlicingProfileSelection(
    IPrinterRepository printerRepository,
    IMachineProfileRepository machineProfileRepository,
    IProcessProfileRepository processProfileRepository,
    IFilamentProfileRepository filamentProfileRepository,
    RepresentativeFilamentResolver representativeFilamentResolver)
{
    public async Task<ResolvedSlicingProfiles> ResolveAsync(
        Guid? printerId, string materialType, Guid? narrowedFilamentProfileId, CancellationToken cancellationToken)
    {
        if (printerId is not { } targetPrinterId)
            throw new SlicingProfileUnconfiguredException("Production slicing requires an assigned printer; use explicit estimation settings for library estimates.");
        var (machineProfileId, processProfileId, buildPlateType) = await ResolveForPrinterAsync(targetPrinterId, cancellationToken);

        var filamentProfile = narrowedFilamentProfileId is { } narrowedId
            ? await filamentProfileRepository.GetByIdAsync(narrowedId, cancellationToken)
                ?? throw new SlicingProfileUnconfiguredException("The part's narrowed filament profile no longer exists.")
            : (await representativeFilamentResolver.ResolveAsync(printerId, materialType, cancellationToken)).Profile;
        return await ResolveProfilesAsync(machineProfileId, processProfileId, filamentProfile.Id, buildPlateType, cancellationToken);
    }

    public async Task<ResolvedSlicingProfiles> ResolveProfilesAsync(Guid machineProfileId, Guid processProfileId,
        Guid filamentProfileId, BuildPlateType buildPlateType, CancellationToken cancellationToken)
    {
        var machineProfile = await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The selected machine profile no longer exists.");
        var processProfile = await processProfileRepository.GetByIdAsync(processProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The selected process profile no longer exists.");
        var filamentProfile = await filamentProfileRepository.GetByIdAsync(filamentProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The selected filament profile no longer exists.");
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
            machineProfile.Name, processProfile.Name, filamentProfile.Name);
    }

    // research.md §0a/§5 — Bambu Studio's `include` gcode-template fold-in, folded in after the
    // ancestor chain merge but before the target's own final layer (ProfileRawDataMerger.
    // MergeAncestorChainWithIncludes' own ordering guarantee). OrcaSlicer never sets
    // IncludedTemplateNames, so this is a no-op there.
    private async Task<string> MergeMachineChainWithIncludesOrThrowAsync(
        MachineProfile target, CancellationToken cancellationToken)
    {
        var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(target.Id, FetchMachineNode, cancellationToken);
        if (hasUnresolvedBase)
        {
            throw new SlicingProfileUnconfiguredException(
                "The selected machine profile has an unresolved ancestor in its inheritance chain and cannot be used to slice.");
        }

        var includedTemplateNames = target.IncludedTemplateNames;
        if (includedTemplateNames is null or { Count: 0 } || target.VendorName is null)
        {
            return ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
        }

        var resolvedFragments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in includedTemplateNames)
        {
            if (resolvedFragments.ContainsKey(name))
            {
                continue;
            }

            var fragment = await machineProfileRepository.FindSystemByNameAsync(target.VendorName, name, cancellationToken);
            if (fragment is not null)
            {
                resolvedFragments[name] = fragment.RawDataJson;
            }
        }

        return ProfileRawDataMerger.MergeAncestorChainWithIncludes(
            ancestorsOldestFirst, includedTemplateNames, name => resolvedFragments.GetValueOrDefault(name));
    }

    private async Task<string> MergeFilamentChainWithIncludesOrThrowAsync(
        FilamentProfile target, CancellationToken cancellationToken)
    {
        var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(target.Id, FetchFilamentNode, cancellationToken);
        if (hasUnresolvedBase)
        {
            throw new SlicingProfileUnconfiguredException(
                "The selected filament profile has an unresolved ancestor in its inheritance chain and cannot be used to slice.");
        }

        var includedTemplateNames = target.IncludedTemplateNames;
        if (includedTemplateNames is null or { Count: 0 } || target.VendorName is null)
        {
            return ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
        }

        var resolvedFragments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in includedTemplateNames)
        {
            if (resolvedFragments.ContainsKey(name))
            {
                continue;
            }

            var fragment = await filamentProfileRepository.FindSystemByNameAsync(target.VendorName, name, cancellationToken);
            if (fragment is not null)
            {
                resolvedFragments[name] = fragment.RawDataJson;
            }
        }

        return ProfileRawDataMerger.MergeAncestorChainWithIncludes(
            ancestorsOldestFirst, includedTemplateNames, name => resolvedFragments.GetValueOrDefault(name));
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
