using System.Globalization;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts.SlicerInvocation;

/// <summary>
/// SuperSlicer's PrusaSlicer-fork CLI form (research.md §4) — its slice/export flags diverge from
/// the shared <see cref="PrusaSliceFamilyInvocation"/> form (a filament-type flag instead of a named
/// filament-profile load, and a plain <c>--slice</c> switch with no plate index). The executable
/// itself was already resolved per-platform (AppImage <c>AppRun</c> / Windows <c>*.exe</c> / macOS
/// <c>.app</c> bundle) by <see cref="SlicerExecutableResolver"/> at discovery time — this strategy
/// only builds the argument list. The exact flag surface is validated manually against the real
/// binary, the same carve-out as the rest of this CLI-invocation layer (never a CI gate).
/// </summary>
public sealed class SuperSlicerInvocation : ISlicerInvocation
{
    public bool SupportsEngine(SlicerEngine engine) => engine == SlicerEngine.SuperSlicer;

    public SlicerProcessInvocation BuildInvocation(
        AvailableSlicer slicer, string modelFilePath, string materialType, decimal nozzleDiameterMm, string outputPath)
    {
        var arguments = $"--slice --filament-type \"{EscapeMaterial(materialType)}\" " +
            $"--nozzle-diameter {nozzleDiameterMm.ToString(CultureInfo.InvariantCulture)} " +
            $"--export-gcode \"{outputPath}\" \"{modelFilePath}\"";

        return new SlicerProcessInvocation(slicer.ExecutablePath, arguments);
    }

    private static string EscapeMaterial(string materialType) => materialType.Replace("\"", "\\\"");
}
