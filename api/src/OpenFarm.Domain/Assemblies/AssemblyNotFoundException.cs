namespace OpenFarm.Domain.Assemblies;

public sealed class AssemblyNotFoundException(Guid assemblyId) : Exception($"Assembly '{assemblyId}' was not found.")
{
    public Guid AssemblyId { get; } = assemblyId;
}
