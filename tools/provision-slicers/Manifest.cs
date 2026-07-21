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

/// <summary>One maintainer-declared desired <c>(engine, version, platforms[])</c> entry (contracts/slicer-manifest.schema.json).</summary>
public sealed record SlicerManifestEntry(Engine Engine, string Version, IReadOnlyList<Platform> Platforms, string? Notes);

/// <summary>The full parsed manifest — the desired bundle state (research.md §8).</summary>
public sealed record SlicerManifest(int SchemaVersion, IReadOnlyList<SlicerManifestEntry> Slicers);
