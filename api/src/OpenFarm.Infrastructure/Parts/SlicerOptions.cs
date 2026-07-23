namespace OpenFarm.Infrastructure.Parts;

/// <summary>The path-centric fields have moved to <see cref="SlicerBundleOptions"/> (FR-020) — this retains only the operational knobs that are still fixed/never surfaced (data-model.md "Configuration delta").</summary>
public sealed class SlicerOptions
{
    public const string SectionName = "Slicing";

    public TimeSpan CliTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Bounds concurrent CPU/IO-heavy slicer CLI processes (research.md §3) — never the bottleneck for the parsing branch.</summary>
    public int MaxConcurrentSlicingOperations { get; set; } = 2;

    /// <summary>
    /// Fixed, hardcoded, never operator-configurable — the material type used when an STL (which
    /// structurally carries no material data) is sliced against the "any material" wildcard
    /// (research.md §7a). Engine-independent: every slicer engine's filament resolution path expects
    /// a real material type (e.g. <c>"PLA"</c>), never an engine-specific literal profile name.
    /// </summary>
    public string FallbackMaterialType { get; set; } = "PLA";
}
