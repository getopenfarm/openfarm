namespace OpenFarm.Domain.FilamentTracking;

public sealed class FeedSlotNotFoundException(Guid feedSlotId)
    : Exception($"Feed slot '{feedSlotId}' was not found.")
{
    public Guid FeedSlotId { get; } = feedSlotId;
}
