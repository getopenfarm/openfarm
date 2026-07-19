namespace OpenFarm.Domain.FilamentTracking;

public sealed class FeedUnitNotFoundException(Guid feedUnitId)
    : Exception($"Feed unit '{feedUnitId}' was not found.")
{
    public Guid FeedUnitId { get; } = feedUnitId;
}
