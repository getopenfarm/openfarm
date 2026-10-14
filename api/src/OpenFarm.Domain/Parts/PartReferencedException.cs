namespace OpenFarm.Domain.Parts;

/// <summary>A catalog part cannot be removed while assembly or production references exist.</summary>
public sealed class PartReferencedException(Guid partId, string reason)
    : Exception($"Part '{partId}' cannot be removed: {reason}")
{
    public Guid PartId { get; } = partId;
}
