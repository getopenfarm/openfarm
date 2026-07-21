using OpenFarm.Application.Organizations;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>The three real, flattened (ancestor-chain-merged) preset JSON blobs OrcaSlicer/Bambu Studio need to slice.</summary>
public sealed record ResolvedSlicingProfiles(string MachineSettingsJson, string ProcessSettingsJson, string FilamentSettingsJson);

/// <summary>
/// Resolves an organization's configured default machine profile, that machine's paired process
/// profile, and the material's default filament profile into complete, standalone preset JSON —
/// mirroring <see cref="ResolveSlicerSelection"/>'s role as the single gate fronting both
/// "set as default" and "slice this part". OrcaSlicer/Bambu Studio reject a bare scalar-derived
/// settings file outright (it must be a real preset with "type"/"name"/"inherits"/"from" etc.) and
/// only ever accept one machine paired to the exact process it was authored/shipped for — never
/// bypassable via a fixed generic process (confirmed empirically against the real bundled binary:
/// no combination of --no-check, an absent process file, or a stripped compatible_printers list ever
/// satisfied Orca's machine/process compatibility gate for a mismatched pair). Every unconfigured
/// piece — no org default machine, no paired process, no default filament for the material, or an
/// unresolved ancestor chain in any of the three — throws <see cref="SlicingProfileUnconfiguredException"/>
/// naming exactly what's missing, rather than silently attempting a slice. Real-CLI compatibility of
/// whatever the operator actually configures (e.g. the "G92 E0" gcode-generation validator possibly
/// firing on some machine profiles) is a manual-validation carve-out, same as the rest of this
/// CLI-invocation layer — this service's job ends at "handed the CLI a compatible, complete triple".
/// </summary>
public sealed class ResolveSlicingProfileSelection(
    IOrganizationRepository organizationRepository,
    IMachineProfileRepository machineProfileRepository,
    IProcessProfileRepository processProfileRepository,
    IFilamentProfileRepository filamentProfileRepository,
    IOrganizationFilamentDefaultRepository filamentDefaultRepository)
{
    public async Task<ResolvedSlicingProfiles> ResolveAsync(string materialType, CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetAsync(cancellationToken);

        var machineProfileId = organization.DefaultMachineProfileId
            ?? throw new SlicingProfileUnconfiguredException("No default machine profile is configured for this organization.");
        var machineProfile = await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The organization's default machine profile no longer exists.");

        var processProfileId = machineProfile.DefaultProcessProfileId
            ?? throw new SlicingProfileUnconfiguredException($"Machine profile '{machineProfile.Name}' has no paired default process profile.");
        var processProfile = await processProfileRepository.GetByIdAsync(processProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException($"Machine profile '{machineProfile.Name}''s paired process profile no longer exists.");

        var filamentDefault = await filamentDefaultRepository.FindByMaterialTypeAsync(materialType, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException($"No default filament profile is configured for material '{materialType}'.");
        var filamentProfile = await filamentProfileRepository.GetByIdAsync(filamentDefault.FilamentProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException($"The organization's default filament profile for material '{materialType}' no longer exists.");

        var machineJson = await MergeChainOrThrowAsync(machineProfile.Id, "machine", FetchMachineNode, cancellationToken);
        var processJson = await MergeChainOrThrowAsync(processProfile.Id, "process", FetchProcessNode, cancellationToken);
        var filamentJson = await MergeChainOrThrowAsync(filamentProfile.Id, "filament", FetchFilamentNode, cancellationToken);

        return new ResolvedSlicingProfiles(machineJson, processJson, filamentJson);
    }

    private static async Task<string> MergeChainOrThrowAsync(
        Guid id, string kindLabel, ProfileChainWalker.FetchById fetch, CancellationToken cancellationToken)
    {
        var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(id, fetch, cancellationToken);
        if (hasUnresolvedBase)
        {
            throw new SlicingProfileUnconfiguredException(
                $"The default {kindLabel} profile has an unresolved ancestor in its inheritance chain and cannot be used to slice.");
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
