using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>The path-centric fields have moved to <see cref="SlicerBundleOptions"/> (FR-020) — this retains only the operational knobs that are still fixed/never surfaced (data-model.md "Configuration delta").</summary>
public sealed class SlicerOptions
{
    public const string SectionName = "Slicing";

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
        [SlicerEngine.SuperSlicer] = "Generic PLA",
    };
}
