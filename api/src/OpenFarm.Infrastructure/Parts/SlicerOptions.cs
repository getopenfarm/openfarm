using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

public sealed class SlicerOptions
{
    public const string SectionName = "Slicing";

    public string OrcaSlicerCliPath { get; set; } = "/usr/local/bin/orca-slicer-cli";

    public string? BambuStudioCliPath { get; set; }

    public TimeSpan CliTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Bounds concurrent CPU/IO-heavy slicer CLI processes (research.md §3) — never the bottleneck for the parsing branch.</summary>
    public int MaxConcurrentSlicingOperations { get; set; } = 2;

    /// <summary>
    /// Fixed, hardcoded, never operator-configurable — used only when an STL (which structurally
    /// carries no material data) is sliced against the "any material" wildcard (research.md §7a).
    /// </summary>
    public Dictionary<SlicerEngine, string> GenericFallbackMaterialProfile { get; set; } = new()
    {
        [SlicerEngine.OrcaSlicer] = "Generic PLA",
        [SlicerEngine.BambuStudio] = "Bambu PLA Basic",
    };

    public string CliPathFor(SlicerEngine engine) => engine switch
    {
        SlicerEngine.OrcaSlicer => OrcaSlicerCliPath,
        SlicerEngine.BambuStudio => BambuStudioCliPath
            ?? throw new InvalidOperationException("Slicing:BambuStudioCliPath is not configured."),
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };
}
