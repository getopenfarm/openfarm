using System.Globalization;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts.SlicerInvocation;

/// <summary>
/// The shared PrusaSlicer-derived CLI form used by OrcaSlicer and Bambu Studio (research.md §4) —
/// lifted, unchanged, from the pre-Phase-8 <c>CliSlicerEngine</c>'s single argument string.
/// </summary>
public sealed class PrusaSliceFamilyInvocation : ISlicerInvocation
{
    public bool SupportsEngine(SlicerEngine engine) => engine is SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio;

    public SlicerProcessInvocation BuildInvocation(
        AvailableSlicer slicer, string modelFilePath, string materialType, decimal nozzleDiameterMm, string outputPath)
    {
        var arguments = $"--slice 0 --load-filaments \"{EscapeMaterial(materialType)}\" " +
            $"--nozzle-diameter {nozzleDiameterMm.ToString(CultureInfo.InvariantCulture)} " +
            $"--export-gcode \"{outputPath}\" \"{modelFilePath}\"";

        return new SlicerProcessInvocation(slicer.ExecutablePath, arguments);
    }

    private static string EscapeMaterial(string materialType) => materialType.Replace("\"", "\\\"");
}
