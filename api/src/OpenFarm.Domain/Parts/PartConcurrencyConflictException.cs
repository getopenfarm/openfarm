namespace OpenFarm.Domain.Parts;

public sealed class PartConcurrencyConflictException(Guid partId)
    : Exception($"Part '{partId}' was concurrently modified by another operation.")
{
    public Guid PartId { get; } = partId;
}
