namespace OpenFarm.Domain.Assemblies;

public sealed class AssemblyConcurrencyConflictException(Guid assemblyId)
    : Exception($"Assembly '{assemblyId}' was concurrently modified by another operation.")
{
    public Guid AssemblyId { get; } = assemblyId;
}
