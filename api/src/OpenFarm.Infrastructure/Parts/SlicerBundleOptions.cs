namespace OpenFarm.Infrastructure.Parts;

/// <summary>The bundled slicer directory <see cref="BundledSlicerScanner"/> scans at startup (research.md §1) — the only "path" knowledge anywhere, entirely backend-internal.</summary>
public sealed class SlicerBundleOptions
{
    public const string SectionName = "SlicerBundle";

    public string RootDirectory { get; set; } = "/data/slicers";
}
