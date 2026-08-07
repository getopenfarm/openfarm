namespace OpenFarm.Domain.Assemblies;

/// <summary>FR-011: delete blocked — this Part is targeted by a BOM line on one or more assemblies.</summary>
public sealed class PartReferencedByAssemblyException(Guid partId, IReadOnlyList<string> referencingAssemblyNames)
    : Exception($"Part '{partId}' is still referenced by: {string.Join(", ", referencingAssemblyNames)}.")
{
    public Guid PartId { get; } = partId;

    public IReadOnlyList<string> ReferencingAssemblyNames { get; } = referencingAssemblyNames;
}
