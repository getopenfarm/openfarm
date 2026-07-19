namespace OpenFarm.Domain.FilamentTracking;

public sealed class FeedSlotOccupiedException(Guid feedSlotId)
    : Exception($"Feed slot '{feedSlotId}' already has a spool loaded.")
{
    public Guid FeedSlotId { get; } = feedSlotId;
}
