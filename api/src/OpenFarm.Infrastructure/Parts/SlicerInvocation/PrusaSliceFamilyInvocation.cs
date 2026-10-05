using System.Globalization;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts.SlicerInvocation;

/// <summary>
/// OrcaSlicer/Bambu Studio's real CLI form (research.md §4, revised twice). Unlike PrusaSlicer/
/// SuperSlicer, neither engine has a <c>--nozzle-diameter</c> or <c>--export-gcode</c> flag, and
/// <c>--load-settings</c>/<c>--load-filaments</c> reject a bare scalar-derived stub JSON outright —
/// they require a complete, typed preset (<c>type</c>/<c>name</c>/<c>inherits</c>/<c>from</c>/etc.),
/// confirmed empirically against the real bundled binary. <see cref="ResolvedSlicingProfiles"/>
/// (from <c>ResolveSlicingProfileSelection</c>) supplies exactly that — the org's configured machine
/// profile, that machine's paired process profile (Orca enforces strict machine/process
/// compatibility; only a preset authored/shipped for the exact machine passes, never a fixed generic
/// one), and the material's default filament profile, each already flattened across its ancestor
/// chain. The only export flag is <c>--export-3mf</c>, which writes a gcode-bearing 3MF (the real
/// G-code lives inside it at <c>Metadata/plate_1.gcode</c>, unzipped by <c>CliSlicerEngine</c>).
/// Nozzle diameter and material are also passed as trailing command-line overrides — confirmed to
/// take highest priority and to parse fine alongside <c>--load-settings</c>/<c>--load-filaments</c> —
/// cheap insurance if a resolved profile is slightly stale.
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

        var machineSettingsPath = Path.Combine(workDirectoryPath, "machine_settings.json");
        File.WriteAllText(machineSettingsPath, profiles.MachineSettingsJson);

        var processSettingsPath = Path.Combine(workDirectoryPath, "process_settings.json");
        File.WriteAllText(processSettingsPath, profiles.ProcessSettingsJson);

        var filamentSettingsPath = Path.Combine(workDirectoryPath, "filament_settings.json");
        File.WriteAllText(filamentSettingsPath, profiles.FilamentSettingsJson);

        var outputPath = Path.Combine(workDirectoryPath, "output.gcode.3mf");

        var arguments = $"--slice 0 --load-settings \"{machineSettingsPath};{processSettingsPath}\" --load-filaments \"{filamentSettingsPath}\" " +
            $"--nozzle-diameter {nozzleDiameterMm.ToString(CultureInfo.InvariantCulture)} --filament-type \"{EscapeMaterial(materialType)}\" " +
            $"--export-3mf \"{outputPath}\" \"{modelFilePath}\"";

        return new SlicerProcessInvocation(slicer.ExecutablePath, arguments, outputPath, ProducesGCodeBearingThreeMf: true);
    }

    public static SlicerProcessInvocation BuildEmbeddedProjectInvocation(AvailableSlicer slicer, string modelPath, string workDirectory)
    {
        if (slicer.Engine is not (SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio))
            throw new SlicingFailedException("This engine does not support Orca/Bambu embedded projects.");
        if (!SupportsEmbeddedProject(slicer))
            throw new SlicingFailedException("Embedded-project estimation is unavailable for Bambu Studio 2.7.1 on macOS because its CLI is not compatible with this mode.");
        var output = Path.Combine(workDirectory, "output.gcode.3mf");
        // No load-settings, scalar overrides, updates, or profile-name substitution: preserve the project.
        return new(slicer.ExecutablePath, $"--slice 0 --export-3mf \"{output}\" \"{modelPath}\"", output, true);
    }

    public static bool SupportsEmbeddedProject(AvailableSlicer slicer) =>
        slicer.Engine is SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio
        && !(slicer.Engine == SlicerEngine.BambuStudio && slicer.Platform == SlicerPlatform.MacOs && slicer.Version == "2.7.1");

    private static string EscapeMaterial(string materialType) => materialType.Replace("\"", "\\\"");
}
