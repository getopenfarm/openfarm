namespace OpenFarm.Domain.FilamentTracking;

/// <summary>
/// A single loading position within a <see cref="FeedUnit"/>. Carries no kind-specific fields —
/// <c>Kind</c> lives on the parent <see cref="FeedUnit"/> only (research.md §3).
/// </summary>
public sealed class FeedSlot
{
    private FeedSlot(Guid id, Guid organizationId, Guid feedUnitId, int slotPosition, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        FeedUnitId = feedUnitId;
        SlotPosition = slotPosition;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid FeedUnitId { get; }

    public int SlotPosition { get; }

    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    internal static FeedSlot Create(Guid organizationId, Guid feedUnitId, int slotPosition) =>
        new(Guid.NewGuid(), organizationId, feedUnitId, slotPosition, DateTimeOffset.UtcNow);

    /// <summary>Idempotent — a no-op if already deactivated.</summary>
    public void Deactivate() => DeactivatedAtUtc ??= DateTimeOffset.UtcNow;

    /// <summary>Idempotent — a no-op if already active. Clears a prior deactivation when a connector-derived slot reappears.</summary>
    public void Reactivate() => DeactivatedAtUtc = null;
}
