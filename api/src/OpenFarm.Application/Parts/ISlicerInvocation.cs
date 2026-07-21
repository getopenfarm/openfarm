using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// The process <see cref="FileName"/> + argument list a resolved <see cref="ISlicerInvocation"/>
/// strategy builds for one slice (research.md §4), plus where it wrote its result and whether that
/// result is a raw <c>.gcode</c> file or a gcode-bearing <c>.3mf</c> archive the caller must unzip
/// (OrcaSlicer/Bambu Studio have no direct G-code export flag — see
/// <c>PrusaSliceFamilyInvocation</c>).
/// </summary>
public sealed record SlicerProcessInvocation(string FileName, string Arguments, string OutputPath, bool ProducesGCodeBearingThreeMf);

/// <summary>
/// Per-engine CLI-invocation strategy — isolates each engine's own command-line form (research.md
/// §4) so <c>CliSlicerEngine</c> stays a pure orchestration shell (temp dir, process lifetime,
/// timeout, failure mapping) regardless of which engine it's driving. Adding a fourth engine (FR-022)
/// means one more implementation, not a change to any existing one.
/// </summary>
public interface ISlicerInvocation
{
    /// <summary>Whether this strategy knows how to invoke <paramref name="engine"/>.</summary>
    bool SupportsEngine(SlicerEngine engine);

    /// <summary>
    /// Builds the process invocation for slicing <paramref name="modelFilePath"/> against
    /// <paramref name="slicer"/>. <paramref name="workDirectoryPath"/> is the per-slice temp
    /// directory <c>CliSlicerEngine</c> creates and deletes — a strategy that needs to write its own
    /// settings files (rather than pass raw values as flags) writes them there, and reports its own
    /// chosen output file name back via <see cref="SlicerProcessInvocation.OutputPath"/>.
    /// </summary>
    SlicerProcessInvocation BuildInvocation(
        AvailableSlicer slicer,
        string modelFilePath,
        string materialType,
        decimal nozzleDiameterMm,
        string workDirectoryPath);
}
