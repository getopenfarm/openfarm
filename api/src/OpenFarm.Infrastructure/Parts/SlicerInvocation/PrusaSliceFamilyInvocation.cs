using System.Globalization;
using System.Text.Json;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts.SlicerInvocation;

/// <summary>
/// OrcaSlicer/Bambu Studio's real CLI form (research.md §4, revised). Unlike PrusaSlicer/SuperSlicer,
/// neither engine has a <c>--nozzle-diameter</c> or <c>--export-gcode</c> flag: nozzle diameter and
/// filament type are only settable via JSON files loaded through <c>--load-settings</c>/
/// <c>--load-filaments</c>, and the only export flag is <c>--export-3mf</c>, which writes a
/// gcode-bearing 3MF (the real G-code lives inside it at <c>Metadata/plate_1.gcode</c>, unzipped by
/// <c>CliSlicerEngine</c> when <see cref="SlicerProcessInvocation.ProducesGCodeBearingThreeMf"/> is
/// true). The previous "--slice 0 --load-filaments … --nozzle-diameter … --export-gcode …" form
/// this class used to build doesn't parse on either engine's real binary (exit 254, "Invalid option
/// --export-gcode").
/// </summary>
public sealed class PrusaSliceFamilyInvocation : ISlicerInvocation
{
    public bool SupportsEngine(SlicerEngine engine) => engine is SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio;

    public SlicerProcessInvocation BuildInvocation(
        AvailableSlicer slicer, string modelFilePath, string materialType, decimal nozzleDiameterMm, string workDirectoryPath)
    {
        var machineSettingsPath = Path.Combine(workDirectoryPath, "machine_settings.json");
        File.WriteAllText(machineSettingsPath, JsonSerializer.Serialize(new
        {
            nozzle_diameter = new[] { nozzleDiameterMm.ToString(CultureInfo.InvariantCulture) },
        }));

        var filamentSettingsPath = Path.Combine(workDirectoryPath, "filament_settings.json");
        File.WriteAllText(filamentSettingsPath, JsonSerializer.Serialize(new
        {
            filament_type = new[] { materialType },
        }));

        var outputPath = Path.Combine(workDirectoryPath, "output.gcode.3mf");

        var arguments = $"--slice 0 --load-settings \"{machineSettingsPath}\" --load-filaments \"{filamentSettingsPath}\" " +
            $"--export-3mf \"{outputPath}\" \"{modelFilePath}\"";

        return new SlicerProcessInvocation(slicer.ExecutablePath, arguments, outputPath, ProducesGCodeBearingThreeMf: true);
    }
}
