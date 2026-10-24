namespace OpenFarm.Domain.FilamentTracking;

/// <summary>
/// A single loading position within a <see cref="FeedUnit"/>. Carries no kind-specific fields —
/// <c>Kind</c> lives on the parent <see cref="FeedUnit"/> only (research.md §3).
/// </summary>
public sealed class FeedSlot
{
    private FeedSlot(Guid id, Guid feedUnitId, int slotPosition, int extruderIndex, DateTimeOffset createdAtUtc)
    {
        Id = id;
        FeedUnitId = feedUnitId;
        SlotPosition = slotPosition;
        ExtruderIndex = extruderIndex;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid FeedUnitId { get; }

    public int SlotPosition { get; }

    /// <summary>The extruder this slot feeds (an index into the printer model's extruders): 0 on a single-nozzle printer, the head on a toolchanger.</summary>
    public int ExtruderIndex { get; private set; }

    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    /// <summary>The RFID tag this slot last loaded a spool by: how auto-load tells a tag just read from one still sitting there.</summary>
    public string? LinkedRfidId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    internal static FeedSlot Create(Guid feedUnitId, int slotPosition, int extruderIndex) =>
        new(Guid.NewGuid(), feedUnitId, slotPosition, extruderIndex, DateTimeOffset.UtcNow);

    /// <summary>Follows the printer's report of which extruder this slot feeds. True when it changed.</summary>
    public bool AssignExtruder(int extruderIndex)
    {
        if (ExtruderIndex == extruderIndex) return false;
        ExtruderIndex = extruderIndex;
        return true;
    }

    /// <summary>
    /// Takes one reading of the slot's tag. True when the spool with <paramref name="rfidId"/> should be loaded here now: a
    /// registered tag new to the slot. The same tag still read does nothing, so a manual unload is never undone; an empty slot
    /// or an unregistered tag is forgotten, so re-inserting the spool, or registering it later, loads it.
    /// </summary>
    public bool ObserveRfid(string? rfidId, bool registered)
    {
        if (rfidId is null || !registered)
        {
            LinkedRfidId = null;
            return false;
        }

        if (rfidId == LinkedRfidId)
        {
            return false;
        }

        LinkedRfidId = rfidId;
        return true;
    }

    /// <summary>Idempotent — a no-op if already deactivated.</summary>
    public void Deactivate() => DeactivatedAtUtc ??= DateTimeOffset.UtcNow;

    /// <summary>Idempotent — a no-op if already active. Clears a prior deactivation when a connector-derived slot reappears.</summary>
    public void Reactivate() => DeactivatedAtUtc = null;
}
