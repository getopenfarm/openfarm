namespace OpenFarm.Domain.Assemblies;

public sealed class BomLineNotFoundException(Guid assemblyId, Guid lineId)
    : Exception($"BOM line '{lineId}' was not found on assembly '{assemblyId}'.")
{
    public Guid AssemblyId { get; } = assemblyId;

    public Guid LineId { get; } = lineId;
}
