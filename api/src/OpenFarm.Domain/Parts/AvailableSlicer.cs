namespace OpenFarm.Domain.Parts;

/// <summary>
/// One discovered bundle leaf — presence is availability, no execution/validation (FR-002).
/// <see cref="ExecutablePath"/> is backend-internal only, resolved within the bundle leaf by a
/// per-engine/per-platform convention (research.md §4/§9) — it is never serialized to the wire or
/// shown to a user (FR-001/FR-020).
/// </summary>
public sealed record AvailableSlicer(SlicerEngine Engine, string Version, SlicerPlatform Platform, string ExecutablePath)
{
    public SlicerRef ToSlicerRef() => new(Engine, Version);
}
