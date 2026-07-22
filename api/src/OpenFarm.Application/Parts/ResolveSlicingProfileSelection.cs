using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Parts;

/// <summary>The three real, flattened (ancestor-chain-merged) preset JSON blobs OrcaSlicer/Bambu Studio need to slice.</summary>
public sealed record ResolvedSlicingProfiles(string MachineSettingsJson, string ProcessSettingsJson, string FilamentSettingsJson);

/// <summary>
/// Resolves a slice target's machine, process, and filament profiles into complete, standalone
/// preset JSON (research.md §5) — either a specific printer's own selections, or, when no printer is
/// targeted (the pre-print estimate case, FR-015), a deterministic fleet-representative printer's
/// selections. Filament resolves from an explicit narrowed profile when supplied (FR-012, US4) or
/// else <see cref="RepresentativeFilamentResolver"/>'s deterministic per-material-type pick
/// (FR-018/FR-019). Organization-level defaults (machine/process/filament) no longer exist anywhere
/// in this resolution path — removed entirely per FR-009/FR-017. OrcaSlicer/Bambu Studio reject a
/// bare scalar-derived settings file outright (it must be a real preset with "type"/"name"/"inherits"/
/// "from" etc.) and only ever accept one machine paired to the exact process it was authored/shipped
/// for. Every unconfigured piece — no printer selection, no printers in the fleet, no representative
/// filament for the material, or an unresolved ancestor chain in any of the three — throws
/// <see cref="SlicingProfileUnconfiguredException"/> naming exactly what's missing, rather than
/// silently attempting a slice.
/// </summary>
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
        var (machineProfileId, processProfileId) = printerId is { } targetPrinterId
            ? await ResolveForPrinterAsync(targetPrinterId, cancellationToken)
            : await ResolveForFleetRepresentativeAsync(cancellationToken);

        var machineProfile = await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The selected machine profile no longer exists.");
        var processProfile = await processProfileRepository.GetByIdAsync(processProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The selected process profile no longer exists.");

        var filamentProfile = narrowedFilamentProfileId is { } narrowedId
            ? await filamentProfileRepository.GetByIdAsync(narrowedId, cancellationToken)
                ?? throw new SlicingProfileUnconfiguredException("The part's narrowed filament profile no longer exists.")
            : await representativeFilamentResolver.ResolveAsync(materialType, cancellationToken);

        var machineJson = await MergeMachineChainWithIncludesOrThrowAsync(machineProfile, cancellationToken);
        var processJson = await MergeChainOrThrowAsync(processProfile.Id, "process", FetchProcessNode, cancellationToken);
        var filamentJson = await MergeFilamentChainWithIncludesOrThrowAsync(filamentProfile, cancellationToken);

        return new ResolvedSlicingProfiles(machineJson, processJson, filamentJson);
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

    private async Task<(Guid MachineProfileId, Guid ProcessProfileId)> ResolveForPrinterAsync(Guid printerId, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(printerId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException($"Printer '{printerId}' was not found.");

        var machineProfileId = printer.MachineProfileId
            ?? throw new SlicingProfileUnconfiguredException($"Printer '{printer.Name}' has no machine profile selected.");
        var processProfileId = printer.ProcessProfileId
            ?? throw new SlicingProfileUnconfiguredException($"Printer '{printer.Name}' has no process profile selected.");

        return (machineProfileId, processProfileId);
    }

    /// <summary>
    /// FR-015 — no target printer (the pre-print, estimate-only case). Picks the fleet printer with
    /// the smallest <see cref="Guid"/> among those with both selections set — arbitrary but stable,
    /// so a repeated resolution against an unchanged fleet always picks the same printer
    /// (research.md §5, mirroring §2's representative-filament determinism requirement).
    /// </summary>
    private async Task<(Guid MachineProfileId, Guid ProcessProfileId)> ResolveForFleetRepresentativeAsync(CancellationToken cancellationToken)
    {
        var printers = await printerRepository.ListAsync(cancellationToken);
        var representative = printers
            .Where(p => p.MachineProfileId is not null && p.ProcessProfileId is not null)
            .OrderBy(p => p.Id)
            .FirstOrDefault()
            ?? throw new SlicingProfileUnconfiguredException("No printers configured with both a machine and process profile selected.");

        return (representative.MachineProfileId!.Value, representative.ProcessProfileId!.Value);
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
