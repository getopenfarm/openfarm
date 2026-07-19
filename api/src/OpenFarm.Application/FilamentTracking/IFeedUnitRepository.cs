using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public interface IFeedUnitRepository
{
    Task AddAsync(FeedUnit feedUnit, CancellationToken cancellationToken);

    Task<FeedUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeedUnit>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FeedUnit>> GetByPrinterIdAsync(Guid printerId, CancellationToken cancellationToken);

    Task<FeedUnit?> FindByPrinterKindUnitIndexAsync(Guid printerId, string kind, int unitIndex, CancellationToken cancellationToken);

    Task<FeedSlot?> GetFeedSlotByIdAsync(Guid feedSlotId, CancellationToken cancellationToken);

    /// <summary>The parent <see cref="FeedUnit"/> (with its <see cref="FeedUnit.FeedSlots"/> loaded) owning a given <see cref="FeedSlot"/>.</summary>
    Task<FeedUnit?> GetByFeedSlotIdAsync(Guid feedSlotId, CancellationToken cancellationToken);

    /// <summary>
    /// Explicitly tracks a new <see cref="FeedSlot"/> as Added — a slot appended only to an
    /// already-tracked <see cref="FeedUnit"/>'s <see cref="FeedUnit.FeedSlots"/> collection is
    /// misdetected as Modified by EF Core's graph-based change tracking (its client-generated Guid
    /// key is already non-default when discovered). Does not itself call
    /// <see cref="SaveChangesAsync"/> — batched with the caller's other tracked changes.
    /// </summary>
    void TrackNewFeedSlot(FeedSlot feedSlot);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
