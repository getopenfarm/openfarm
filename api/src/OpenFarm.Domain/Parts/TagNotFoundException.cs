namespace OpenFarm.Domain.Parts;

public sealed class TagNotFoundException(Guid tagId) : Exception($"Tag '{tagId}' was not found.")
{
    public Guid TagId { get; } = tagId;
}
