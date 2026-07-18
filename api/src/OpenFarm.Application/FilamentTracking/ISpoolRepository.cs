using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public interface ISpoolRepository
{
    Task AddAsync(Spool spool, CancellationToken cancellationToken);

    Task<Spool?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Spool>> ListAsync(SpoolState? state, Guid? filamentVariantId, CancellationToken cancellationToken);

    /// <summary>Every <see cref="Spool"/> currently <see cref="SpoolState.Using"/> a <see cref="FeedSlot"/> belonging to <paramref name="feedUnitId"/> — the auto-continuation sibling lookup.</summary>
    Task<IReadOnlyList<Spool>> FindLoadedInFeedUnitAsync(Guid feedUnitId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
