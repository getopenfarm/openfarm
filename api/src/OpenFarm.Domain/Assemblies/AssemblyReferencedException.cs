namespace OpenFarm.Domain.Assemblies;

/// <summary>FR-011: delete blocked — this assembly is a sub-assembly target on one or more other assemblies.</summary>
public sealed class AssemblyReferencedException(Guid assemblyId, IReadOnlyList<string> referencingAssemblyNames)
    : Exception($"Assembly '{assemblyId}' is still referenced by: {string.Join(", ", referencingAssemblyNames)}.")
{
    public Guid AssemblyId { get; } = assemblyId;

    public IReadOnlyList<string> ReferencingAssemblyNames { get; } = referencingAssemblyNames;
}
