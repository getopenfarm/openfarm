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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
