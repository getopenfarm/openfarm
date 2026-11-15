using System.Text.Json.Nodes;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts.SlicerInvocation;

/// <summary>
/// OrcaSlicer/Bambu Studio's real CLI form (research.md §4, revised twice). Unlike PrusaSlicer/
/// SuperSlicer, neither engine has an <c>--export-gcode</c> flag, and
/// <c>--load-settings</c>/<c>--load-filaments</c> reject a bare scalar-derived stub JSON outright —
/// they require a complete, typed preset (<c>type</c>/<c>name</c>/<c>inherits</c>/<c>from</c>/etc.),
/// confirmed empirically against the real bundled binary. <see cref="ResolvedSlicingProfiles"/>
/// (from <c>ResolveSlicingProfileSelection</c>) supplies exactly that — the farm's configured machine
/// profile, that machine's paired process profile (Orca enforces strict machine/process
/// compatibility; only a preset authored/shipped for the exact machine passes, never a fixed generic
/// one), and the material's default filament profile, each already flattened across its ancestor
/// chain. The only export flag is <c>--export-3mf</c>, which writes a gcode-bearing 3MF (the real
/// G-code lives inside it at <c>Metadata/plate_&lt;n&gt;.gcode</c>; <c>CliSlicerEngine</c> retains the
/// archive for production while extracting metadata). <c>--slice</c> takes the profiles' plate (1-based), or 0 for every plate.
/// <c>--allow-newer-file</c> is always passed: the CLI otherwise refuses any 3MF whose generator has a
/// different major version or a newer minor version than the bundled slicer (exit -24), e.g. a project
/// saved by Bambu Studio 1.x or a newer Orca. Nozzle diameter and material are deliberately NOT passed
/// as overrides: the resolved machine profile is already validated against the nozzle and the filament
/// profile against the material, and a single-value vector override collapses a multi-filament project.
/// OrcaSlicer 2.5+ resolves a loaded preset's <c>inherits</c> against its own preset files and exits -5 for any other
/// file, so for it <c>inherits</c> is dropped: the presets are already flattened, and OpenFarm has checked them.
/// </summary>
public sealed class PrusaSliceFamilyInvocation : ISlicerInvocation
{
    public bool SupportsEngine(SlicerEngine engine) => engine is SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio;

    public SlicerProcessInvocation BuildInvocation(
        AvailableSlicer slicer, string modelFilePath, string materialType, decimal nozzleDiameterMm,
        string workDirectoryPath, ResolvedSlicingProfiles? resolvedProfiles)
    {
        var profiles = resolvedProfiles
            ?? throw new SlicingFailedException("OrcaSlicer/Bambu Studio require a resolved machine/process/filament profile to slice.");

        var dropInherits = ResolvesInherits(slicer);
        // A project's own machine and process are left alone: no --load-settings, so only the filaments are loaded over it.
        var loadSettings = string.Empty;
        if (!profiles.FollowsProject)
        {
            var machineSettingsPath = Path.Combine(workDirectoryPath, "machine_settings.json");
            File.WriteAllText(machineSettingsPath, ForCli(JsonNode.Parse(profiles.MachineSettingsJson!)!.AsObject(), "machine", dropInherits));

            var processSettingsPath = Path.Combine(workDirectoryPath, "process_settings.json");
            File.WriteAllText(processSettingsPath, ForCli(JsonNode.Parse(profiles.ProcessSettingsJson!)!.AsObject(), "process", dropInherits));
            loadSettings = $"--load-settings \"{machineSettingsPath};{processSettingsPath}\" ";
        }

        var filamentSettingsPaths = WriteFilamentSettings(profiles, workDirectoryPath, dropInherits);

        var outputPath = Path.Combine(workDirectoryPath, "output.gcode.3mf");

        var arguments = $"--slice {profiles.Plate} --allow-newer-file {loadSettings}--load-filaments \"{string.Join(';', filamentSettingsPaths)}\" " +
            $"--export-3mf \"{outputPath}\" \"{modelFilePath}\"";

        return new SlicerProcessInvocation(slicer.ExecutablePath, arguments, outputPath, ProducesGCodeBearingThreeMf: true);
    }

    /// <summary>
    /// One file per project filament slot (the CLI loads them by position), so each slot slices with its own line's
    /// preset. The slot's colour is written into its file: the CLI ranks a loaded filament above the project, and dispatch
    /// matches the output's filaments to job lines by material and colour. Without slots, the one filament alone.
    /// </summary>
    private static List<string> WriteFilamentSettings(ResolvedSlicingProfiles profiles, string workDirectoryPath, bool dropInherits)
    {
        if (profiles.Slots.Count == 0)
        {
            var path = Path.Combine(workDirectoryPath, "filament_settings.json");
            File.WriteAllText(path, ForCli(JsonNode.Parse(profiles.FilamentSettingsJson)!.AsObject(), "filament", dropInherits));
            return [path];
        }

        return profiles.Slots.Select((slot, index) =>
        {
            var settings = JsonNode.Parse(profiles.Filaments[slot.Filament].SettingsJson)!.AsObject();
            if (slot.ColorHex is { } color) settings["filament_colour"] = new JsonArray(color);
            var path = Path.Combine(workDirectoryPath, $"filament_settings_{index}.json");
            File.WriteAllText(path, ForCli(settings, "filament", dropInherits));
            return path;
        }).ToList();
    }

    /// <summary>
    /// The CLI rejects a settings file with no <c>type</c> (exit 251, "unknown config type"). The slicers' own export of a user
    /// preset leaves it out — the folder it sits in implies it — so a user-imported preset needs it added.
    /// </summary>
    private static string ForCli(JsonObject settings, string type, bool dropInherits)
    {
        if (!settings.ContainsKey("type")) settings["type"] = type;
        if (dropInherits) settings.Remove("inherits");
        return settings.ToJsonString();
    }

    /// <summary>
    /// OrcaSlicer 2.5.0 (#15438) resolves <c>inherits</c> by file identity within its own presets, so a flattened copy
    /// fails with -5. Older Orca and Bambu Studio match a user machine to process presets by its <c>inherits</c>, so they keep it.
    /// </summary>
    private static bool ResolvesInherits(AvailableSlicer slicer) =>
        slicer.Engine == SlicerEngine.OrcaSlicer && new SlicerVersionComparer().Compare(slicer.Version, "2.5.0") >= 0;

    public static SlicerProcessInvocation BuildEmbeddedProjectInvocation(AvailableSlicer slicer, string modelPath, string workDirectory)
    {
        if (slicer.Engine is not (SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio))
            throw new SlicingFailedException("This engine does not support Orca/Bambu embedded projects.");
        if (!SupportsEmbeddedProject(slicer))
            throw new SlicingFailedException("Embedded-project estimation is unavailable for Bambu Studio 2.7.1 on macOS because its CLI is not compatible with this mode.");
        var output = Path.Combine(workDirectory, "output.gcode.3mf");
        // No load-settings, scalar overrides, updates, or profile-name substitution: preserve the project.
        return new(slicer.ExecutablePath, $"--slice 0 --allow-newer-file --export-3mf \"{output}\" \"{modelPath}\"", output, true);
    }

    public static bool SupportsEmbeddedProject(AvailableSlicer slicer) =>
        slicer.Engine is SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio
        && !(slicer.Engine == SlicerEngine.BambuStudio && slicer.Platform == SlicerPlatform.MacOs && slicer.Version == "2.7.1");
}
