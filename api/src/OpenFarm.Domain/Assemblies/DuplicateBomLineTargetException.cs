namespace OpenFarm.Domain.Assemblies;

/// <summary>FR-013: the same target may appear at most once on one assembly.</summary>
public sealed class DuplicateBomLineTargetException(BomLineTargetKind targetKind, Guid targetId)
    : Exception($"A BOM line targeting {targetKind} '{targetId}' already exists on this assembly.")
{
    public BomLineTargetKind TargetKind { get; } = targetKind;

    public Guid TargetId { get; } = targetId;
}
