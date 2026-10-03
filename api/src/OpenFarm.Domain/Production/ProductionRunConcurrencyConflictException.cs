namespace OpenFarm.Domain.Production;

public sealed class ProductionRunConcurrencyConflictException(Guid entityId)
    : Exception($"'{entityId}' was concurrently modified by another operation.")
{
    public Guid EntityId { get; } = entityId;
}
