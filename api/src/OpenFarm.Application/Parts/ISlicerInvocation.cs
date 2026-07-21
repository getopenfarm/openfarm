using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>The process <see cref="FileName"/> + argument list a resolved <see cref="ISlicerInvocation"/> strategy builds for one slice (research.md §4).</summary>
public sealed record SlicerProcessInvocation(string FileName, string Arguments);

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

    /// <summary>Builds the process invocation for slicing <paramref name="modelFilePath"/> against <paramref name="slicer"/>.</summary>
    SlicerProcessInvocation BuildInvocation(
        AvailableSlicer slicer,
        string modelFilePath,
        string materialType,
        decimal nozzleDiameterMm,
        string outputPath);
}
