namespace OpenFarm.Domain.Assemblies;

/// <summary>FR-011: delete blocked — this component is targeted by a BOM line on one or more assemblies.</summary>
public sealed class NonPrintedComponentReferencedException(Guid nonPrintedComponentId, IReadOnlyList<string> referencingAssemblyNames)
    : Exception($"NonPrintedComponent '{nonPrintedComponentId}' is still referenced by: {string.Join(", ", referencingAssemblyNames)}.")
{
    public Guid NonPrintedComponentId { get; } = nonPrintedComponentId;

    public IReadOnlyList<string> ReferencingAssemblyNames { get; } = referencingAssemblyNames;
}
