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
/// One platform's pinned <c>(version, download URL)</c> for an engine (contracts/slicer-manifest.schema.json).
/// Platforms are independent — different platforms commonly need different versions pinned, e.g.
/// because a given release regressed on only one platform.
/// </summary>
public sealed record PlatformBuild(string Version, string AssetUrl);

/// <summary>
/// One maintainer-declared desired engine entry, with a per-platform pinned version + exact download
/// URL for each platform it should be fetched for (contracts/slicer-manifest.schema.json). The tool
/// never derives/guesses an asset URL from a naming pattern — each engine's release asset naming can
/// and does change between versions, so the manifest is the sole source of truth for where to download
/// from; a maintainer copies the real link from the engine's GitHub releases page.
/// </summary>
public sealed record SlicerManifestEntry(Engine Engine, IReadOnlyDictionary<Platform, PlatformBuild> Builds, string? Notes);

/// <summary>The full parsed manifest — the desired bundle state (research.md §8).</summary>
public sealed record SlicerManifest(int SchemaVersion, IReadOnlyList<SlicerManifestEntry> Slicers);
