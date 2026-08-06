namespace OpenFarm.Domain.Assemblies;

public sealed class NonPrintedComponentConcurrencyConflictException(Guid nonPrintedComponentId)
    : Exception($"NonPrintedComponent '{nonPrintedComponentId}' was concurrently modified by another operation.")
{
    public Guid NonPrintedComponentId { get; } = nonPrintedComponentId;
}
