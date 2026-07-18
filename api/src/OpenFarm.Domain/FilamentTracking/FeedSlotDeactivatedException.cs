namespace OpenFarm.Domain.FilamentTracking;

public sealed class FeedSlotDeactivatedException(Guid feedSlotId)
    : Exception($"Feed slot '{feedSlotId}' is deactivated and cannot accept a spool.")
{
    public Guid FeedSlotId { get; } = feedSlotId;
}
