namespace OpenFarm.Domain.Assemblies;

public sealed class NonPrintedComponentNotFoundException(Guid nonPrintedComponentId)
    : Exception($"NonPrintedComponent '{nonPrintedComponentId}' was not found.")
{
    public Guid NonPrintedComponentId { get; } = nonPrintedComponentId;
}
