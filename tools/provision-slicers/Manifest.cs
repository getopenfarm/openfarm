namespace OpenFarm.Tools.ProvisionSlicers;

/// <summary>Supported slicer engines (FR-001) — kept as this tool's own enum, deliberately not a reference to OpenFarm.Domain (plan.md "Structure Decision": no dependency edge from the runtime layers, FR-018).</summary>
public enum Engine
{
    OrcaSlicer,
    BambuStudio,
    SuperSlicer,
}

/// <summary>Target host platform for a fetched build (FR-009/FR-010).</summary>
public enum Platform
{
    Linux,
    Windows,
    MacOs,
}

/// <summary>
/// One maintainer-declared desired <c>(engine, version)</c> entry, with the exact download URL for
/// each platform it should be fetched for (contracts/slicer-manifest.schema.json). The tool never
/// derives/guesses an asset URL from a naming pattern — each engine's release asset naming can and
/// does change between versions, so the manifest is the sole source of truth for where to download
/// from; a maintainer copies the real link from the engine's GitHub releases page.
/// </summary>
public sealed record SlicerManifestEntry(Engine Engine, string Version, IReadOnlyDictionary<Platform, string> Assets, string? Notes);

/// <summary>The full parsed manifest — the desired bundle state (research.md §8).</summary>
public sealed record SlicerManifest(int SchemaVersion, IReadOnlyList<SlicerManifestEntry> Slicers);
