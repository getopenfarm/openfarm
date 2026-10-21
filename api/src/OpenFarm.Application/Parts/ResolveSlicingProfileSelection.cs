using System.Text.Json.Nodes;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Parts;

/// <summary>
/// The three real, flattened (ancestor-chain-merged) preset JSON blobs OrcaSlicer/Bambu Studio need to slice, plus
/// what each was selected as and resolved to under the pin (<see cref="Pin"/> — the engine and exact version they
/// were resolved for). The preset records are the provenance of the slice: they are in the logs and the production
/// configuration stamp. Names and formats are for logging/diagnostics — never sliced into the CLI args.
/// </summary>
public sealed record ResolvedSlicingProfiles(
    SlicerRef Pin, string MachineSettingsJson, string ProcessSettingsJson, string FilamentSettingsJson,
    ResolvedPreset Machine, ResolvedPreset Process, ResolvedPreset Filament)
{
    public string MachineProfileName => Machine.ResolvedName;
    public string ProcessProfileName => Process.ResolvedName;
    public string FilamentProfileName => Filament.ResolvedName;
    public ProfileSourceFormat MachineSourceFormat => Machine.Format;
    public ProfileSourceFormat ProcessSourceFormat => Process.Format;
    public ProfileSourceFormat FilamentSourceFormat => Filament.Format;
}

/// <summary>The three selected presets, each resolved to the pin's own catalog — rows and provenance, before any merging.</summary>
public sealed record ResolvedPresetSet(
    SlicerRef Pin, MachineProfile Machine, ProcessProfile Process, FilamentProfile Filament,
    ResolvedPreset MachinePreset, ResolvedPreset ProcessPreset, ResolvedPreset FilamentPreset);

/// <summary>Either a usable <see cref="Set"/> or every reason there is none.</summary>
public sealed record ProfileSetResolution(ResolvedPresetSet? Set, IReadOnlyList<ProfileIssue> Issues)
{
    /// <exception cref="ProfileSetBlockedException">At least one of the presets cannot be used with the pin.</exception>
    public ResolvedPresetSet Require() => Set ?? throw new ProfileSetBlockedException(Issues);
}

/// <summary>
/// Resolves production settings for explicitly selected presets: every preset is first resolved to the pinned
/// slicer's own catalog (<see cref="ProfileResolver"/> — no fallback to another engine, version or preset), then the
/// RESOLVED rows are checked against each other and the printer, flattened, and returned with their provenance.
/// Library estimates use the same funnel; they never select from fleet or inventory.
/// </summary>
public sealed class ResolveSlicingProfileSelection(
    IPrinterRepository printerRepository,
    IMachineProfileRepository machineProfileRepository,
    IProcessProfileRepository processProfileRepository,
    IFilamentProfileRepository filamentProfileRepository,
    RepresentativeFilamentResolver representativeFilamentResolver, ProfileCompatibility compatibility,
    ProfileResolver resolver)
{
    public async Task<ResolvedSlicingProfiles> ResolveAsync(
        Guid? printerId, string materialType, Guid? narrowedFilamentProfileId, SlicerRef pin, CancellationToken cancellationToken)
    {
        if (printerId is not { } targetPrinterId)
            throw new SlicingProfileUnconfiguredException("Production slicing requires an assigned printer; use explicit estimation settings for library estimates.");
        var (printer, machineProfileId, processProfileId, buildPlateType) = await ResolveForPrinterAsync(targetPrinterId, cancellationToken);

        var filamentProfile = (await representativeFilamentResolver.ResolveLoadedAsync(targetPrinterId, materialType,
            null, null, narrowedFilamentProfileId, cancellationToken)).Profile;
        var use = new ProfileUseContext(printer.Brand, printer.ModelCode, printer.ReportedNozzleDiameterMm!.Value, materialType);
        return await ResolveProfilesAsync(machineProfileId, processProfileId, filamentProfile.Id, buildPlateType, pin, use, cancellationToken);
    }

    /// <param name="use">The printer and material the set is for; <c>null</c> skips the checks that need them (library estimates).</param>
    public async Task<ResolvedSlicingProfiles> ResolveProfilesAsync(Guid machineProfileId, Guid processProfileId,
        Guid filamentProfileId, BuildPlateType buildPlateType, SlicerRef pin, ProfileUseContext? use, CancellationToken cancellationToken)
    {
        var set = (await ResolveSetAsync(machineProfileId, processProfileId, filamentProfileId, pin, cancellationToken)).Require();
        return await ResolveProfilesAsync(set, buildPlateType, use, cancellationToken);
    }

    /// <summary>
    /// Everything that would stop this selection from being used with this pin — the very checks
    /// <see cref="ResolveProfilesAsync(Guid, Guid, Guid, BuildPlateType, SlicerRef, ProfileUseContext?, CancellationToken)"/>
    /// enforces, reported instead of thrown. Warnings built on it cannot disagree with what production does.
    /// </summary>
    public async Task<IReadOnlyList<ProfileIssue>> CheckAsync(Guid machineProfileId, Guid processProfileId, Guid filamentProfileId,
        BuildPlateType buildPlateType, SlicerRef pin, ProfileUseContext? use, CancellationToken cancellationToken)
    {
        try
        {
            await ResolveProfilesAsync(machineProfileId, processProfileId, filamentProfileId, buildPlateType, pin, use, cancellationToken);
            return [];
        }
        catch (ProfileSetBlockedException blocked)
        {
            return blocked.Issues;
        }
        catch (SlicingProfileUnconfiguredException ex)
        {
            return [new ProfileIssue(null, Enum.TryParse<ProfileResolutionOutcome>(ex.Code, out var code) ? code : ProfileResolutionOutcome.Incompatible, string.Empty, ex.Message)];
        }
    }

    /// <summary>Resolves the three selected presets to the pin's catalog and reports every preset that cannot be used, not just the first.</summary>
    public async Task<ProfileSetResolution> ResolveSetAsync(Guid machineProfileId, Guid processProfileId,
        Guid filamentProfileId, SlicerRef pin, CancellationToken cancellationToken)
    {
        var issues = new List<ProfileIssue>();
        var machine = await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken);
        var process = await processProfileRepository.GetByIdAsync(processProfileId, cancellationToken);
        var filament = await filamentProfileRepository.GetByIdAsync(filamentProfileId, cancellationToken);

        var machineResolution = machine is null ? null : await resolver.ResolveAsync(machine, pin, cancellationToken);
        var processResolution = process is null ? null : await resolver.ResolveAsync(process, pin, cancellationToken);
        var filamentResolution = filament is null ? null : await resolver.ResolveAsync(filament, pin, cancellationToken);
        Report(issues, ProfileKind.Machine, machine, machineResolution);
        Report(issues, ProfileKind.Process, process, processResolution);
        Report(issues, ProfileKind.Filament, filament, filamentResolution);
        if (issues.Count > 0)
        {
            return new(null, issues);
        }

        return new(new ResolvedPresetSet(pin, machineResolution!.Resolved!, processResolution!.Resolved!, filamentResolution!.Resolved!,
            ResolvedPreset.From(ProfileKind.Machine, machineResolution), ResolvedPreset.From(ProfileKind.Process, processResolution),
            ResolvedPreset.From(ProfileKind.Filament, filamentResolution)), []);
    }

    private static void Report<TRow>(List<ProfileIssue> issues, ProfileKind kind, TRow? selected, ProfileResolution<TRow>? resolution)
        where TRow : class, ICatalogProfile
    {
        if (selected is null)
        {
            issues.Add(new(kind, ProfileResolutionOutcome.InvalidRow, string.Empty, $"The selected {kind.ToString().ToLowerInvariant()} profile no longer exists."));
        }
        else if (resolution is { Resolved: null })
        {
            issues.Add(new(kind, resolution.Outcome, selected.Name, resolution.Message!));
        }
    }

    /// <summary>Flattens an already resolved set and checks it — the resolved rows, not the selected ones — against each other and the printer.</summary>
    public async Task<ResolvedSlicingProfiles> ResolveProfilesAsync(ResolvedPresetSet set, BuildPlateType buildPlateType,
        ProfileUseContext? use, CancellationToken cancellationToken)
    {
        var (pin, machineProfile, processProfile, filamentProfile) = (set.Pin, set.Machine, set.Process, set.Filament);
        if (processProfile.CompatibleMachineNames is { Count: > 0 } names && !names.Contains(machineProfile.Name, StringComparer.Ordinal))
        {
            throw new ProfileSetBlockedException([new(ProfileKind.Process, ProfileResolutionOutcome.Incompatible, processProfile.Name,
                "The selected process profile is incompatible with the machine profile.")]);
        }

        var machineJson = await MergeMachineChainWithIncludesOrThrowAsync(machineProfile, pin, cancellationToken);
        var processJson = await MergeChainOrThrowAsync(processProfile.Id, "process", FetchProcessNode(pin), cancellationToken);
        var filamentJson = await MergeFilamentChainWithIncludesOrThrowAsync(filamentProfile, pin, cancellationToken);
        if (use is not null && ProfileCompatibility.ValidateResolvedSet(use, machineProfile, machineJson, filamentProfile, filamentJson) is { Count: > 0 } issues)
        {
            throw new ProfileSetBlockedException(issues);
        }

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

        return new ResolvedSlicingProfiles(pin, machineJson, processJson, filamentJson,
            set.MachinePreset, set.ProcessPreset, set.FilamentPreset);
    }

    private Task<string> MergeMachineChainWithIncludesOrThrowAsync(MachineProfile machine, SlicerRef pin, CancellationToken ct) =>
        MergeWithIncludesAsync(machine.Id, machine.IncludedTemplateNames, FetchMachineNode(pin), async (name, token) =>
        {
            var rows = await machineProfileRepository.FindByNameInCatalogAsync(machine.SourceFormat, pin.Version, name, token);
            var preferred = rows.Where(row => row.Origin == ProfileOrigin.System && row.VendorName == machine.VendorName).ToList();
            var candidates = preferred.Count > 0 ? preferred : rows.ToList();
            return candidates.Count == 1 ? candidates[0].Id : (Guid?)null;
        }, ct);

    private Task<string> MergeFilamentChainWithIncludesOrThrowAsync(FilamentProfile filament, SlicerRef pin, CancellationToken ct) =>
        MergeWithIncludesAsync(filament.Id, filament.IncludedTemplateNames, FetchFilamentNode(pin), async (name, token) =>
        {
            var rows = await filamentProfileRepository.FindByNameInCatalogAsync(filament.SourceFormat, pin.Version, name, token);
            var preferred = rows.Where(row => row.Origin == ProfileOrigin.System && row.VendorName == filament.VendorName).ToList();
            var candidates = preferred.Count > 0 ? preferred : rows.ToList();
            return candidates.Count == 1 ? candidates[0].Id : (Guid?)null;
        }, ct);

    // Templates (Bambu `include` fragments) resolve in the pin's own catalog: every System node of the chain was
    // mapped there. A missing, ambiguous or cyclical template must never be silently passed to a different engine.
    private static async Task<string> MergeWithIncludesAsync(Guid id, IReadOnlyList<string>? declaredIncludes,
        ProfileChainWalker.FetchById fetch, Func<string, CancellationToken, Task<Guid?>> findTemplate,
        CancellationToken ct, HashSet<Guid>? visiting = null)
    {
        visiting ??= [];
        if (visiting.Count >= 32 || !visiting.Add(id))
            throw new SlicingProfileUnconfiguredException("The selected profile has a cyclical or excessively deep template chain.");
        try
        {
            var (nodes, unresolved) = await ProfileChainWalker.WalkNodesByIdAsync(id, fetch, ct);
            if (unresolved)
                throw new SlicingProfileUnconfiguredException("The selected profile has an unresolved ancestor in its inheritance chain.");
            var layers = new List<string>();
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                using var json = System.Text.Json.JsonDocument.Parse(node.RawDataJson);
                var includes = ProfileCompatibility.ReadStrings(json.RootElement, "include");
                if (index == nodes.Count - 1 && includes.Count == 0 && declaredIncludes is not null)
                    includes = declaredIncludes;
                foreach (var name in includes)
                {
                    var templateId = await findTemplate(name, ct)
                        ?? throw new SlicingProfileUnconfiguredException($"Included profile template '{name}' is missing or ambiguous in its source catalog.");
                    layers.Add(await MergeWithIncludesAsync(templateId, null, fetch, findTemplate, ct, visiting));
                }
                layers.Add(node.RawDataJson);
            }
            return ProfileRawDataMerger.MergeAncestorChain(layers);
        }
        finally { visiting.Remove(id); }
    }

    private async Task<(Printer Printer, Guid MachineProfileId, Guid ProcessProfileId, BuildPlateType BuildPlateType)> ResolveForPrinterAsync(Guid printerId, CancellationToken cancellationToken)
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
        return (printer, machineProfileId, processProfileId, buildPlateType);
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

    // A chain node is a preset row; a System ancestor of another version is swapped for its pin-version counterpart —
    // so a User preset built on an old-version base inherits from the pin's base, and everything below that base is
    // already in the pin's catalog. No counterpart blocks the slice.
    private ProfileChainWalker.FetchById FetchMachineNode(SlicerRef pin) => async (id, cancellationToken) =>
    {
        var row = await machineProfileRepository.GetByIdAsync(id, cancellationToken);
        if (row is not null)
            row = await resolver.MapAncestorAsync(row, pin, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName, row.Origin, row.SourceVersion);
    };

    private ProfileChainWalker.FetchById FetchProcessNode(SlicerRef pin) => async (id, cancellationToken) =>
    {
        var row = await processProfileRepository.GetByIdAsync(id, cancellationToken);
        if (row is not null)
            row = await resolver.MapAncestorAsync(row, pin, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName, row.Origin, row.SourceVersion);
    };

    private ProfileChainWalker.FetchById FetchFilamentNode(SlicerRef pin) => async (id, cancellationToken) =>
    {
        var row = await filamentProfileRepository.GetByIdAsync(id, cancellationToken);
        if (row is not null)
            row = await resolver.MapAncestorAsync(row, pin, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName, row.Origin, row.SourceVersion);
    };
}
