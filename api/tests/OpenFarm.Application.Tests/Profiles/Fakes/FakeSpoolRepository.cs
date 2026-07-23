using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeSpoolRepository : ISpoolRepository
{
    private readonly List<Spool> _spools = [];

    public Task AddAsync(Spool spool, CancellationToken cancellationToken)
    {
        _spools.Add(spool);
        return Task.CompletedTask;
    }

    public Task<Spool?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_spools.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<Spool>> ListAsync(SpoolState? state, Guid? filamentVariantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Spool>>(_spools
            .Where(s => state is null || s.State == state)
            .Where(s => filamentVariantId is null || s.FilamentVariantId == filamentVariantId)
            .ToList());

    public Task<IReadOnlyList<Spool>> FindLoadedInFeedUnitAsync(Guid feedUnitId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Spool>>([]);

    public Task<Spool?> FindUsingFeedSlotAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
        Task.FromResult(_spools.FirstOrDefault(s => s.State == SpoolState.Using && s.FeedSlotId == feedSlotId));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
