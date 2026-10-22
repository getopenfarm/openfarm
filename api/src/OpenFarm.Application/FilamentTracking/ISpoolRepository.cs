using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public interface ISpoolRepository
{
    Task AddAsync(Spool spool, CancellationToken cancellationToken);

    /// <summary>Adds every spool in one save, so all are kept or none. The default adds them one at a time, which suits in-memory stand-ins.</summary>
    async Task AddRangeAsync(IReadOnlyList<Spool> spools, CancellationToken cancellationToken)
    {
        foreach (var spool in spools)
        {
            await AddAsync(spool, cancellationToken);
        }
    }

    Task<Spool?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Spool>> ListAsync(SpoolState? state, Guid? filamentVariantId, CancellationToken cancellationToken);

    /// <summary>Every <see cref="Spool"/> currently <see cref="SpoolState.Using"/> a <see cref="FeedSlot"/> belonging to <paramref name="feedUnitId"/> — the auto-continuation sibling lookup.</summary>
    Task<IReadOnlyList<Spool>> FindLoadedInFeedUnitAsync(Guid feedUnitId, CancellationToken cancellationToken);

    /// <summary>The <see cref="Spool"/> (if any) currently <see cref="SpoolState.Using"/> a given <see cref="FeedSlot"/> — the load pre-check ahead of the partial unique index.</summary>
    Task<Spool?> FindUsingFeedSlotAsync(Guid feedSlotId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes to a tracked <see cref="Spool"/>. A concurrency conflict — either the
    /// partial unique index (two loads racing for the same feed slot) or the <c>xmin</c> optimistic
    /// token (two operators racing a state change) — surfaces as <see cref="SpoolConcurrencyConflictException"/>.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
