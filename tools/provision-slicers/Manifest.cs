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
/// One pinned <c>(version, download URL)</c> build of an engine for a platform
/// (contracts/slicer-manifest.schema.json), with an optional note explaining that particular pin.
/// </summary>
public sealed record PlatformBuild(string Version, string AssetUrl, string? Notes = null);

/// <summary>
/// One maintainer-declared engine, grouped engine → platform → versions (schemaVersion 4): each
/// platform carries its own list of pinned builds, because platforms commonly need different version
/// sets (e.g. a release that regressed only on macOS is left out of that platform's list). The tool
/// never derives/guesses an asset URL from a naming pattern — each engine's release asset naming can
/// and does change between versions, so the manifest is the sole source of truth for where to download
/// from; a maintainer copies the real link from the engine's GitHub releases page.
/// </summary>
public sealed record SlicerManifestEntry(Engine Engine, IReadOnlyDictionary<Platform, IReadOnlyList<PlatformBuild>> Builds, string? Notes);

/// <summary>The full parsed manifest — the desired bundle state (research.md §8).</summary>
public sealed record SlicerManifest(int SchemaVersion, IReadOnlyList<SlicerManifestEntry> Slicers);
