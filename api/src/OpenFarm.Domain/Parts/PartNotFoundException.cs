namespace OpenFarm.Domain.Parts;

public sealed class PartNotFoundException(Guid partId) : Exception($"Part '{partId}' was not found.")
{
    public Guid PartId { get; } = partId;
}
