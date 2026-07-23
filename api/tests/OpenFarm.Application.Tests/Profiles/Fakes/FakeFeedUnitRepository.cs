using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeFeedUnitRepository : IFeedUnitRepository
{
    private readonly List<FeedUnit> _feedUnits = [];

    public Task AddAsync(FeedUnit feedUnit, CancellationToken cancellationToken)
    {
        _feedUnits.Add(feedUnit);
        return Task.CompletedTask;
    }

    public Task<FeedUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_feedUnits.FirstOrDefault(u => u.Id == id));

    public Task<IReadOnlyList<FeedUnit>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FeedUnit>>(_feedUnits.ToList());

    public Task<IReadOnlyList<FeedUnit>> GetByPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FeedUnit>>(_feedUnits.Where(u => u.PrinterId == printerId).ToList());

    public Task<FeedUnit?> FindByPrinterKindUnitIndexAsync(Guid printerId, string kind, int unitIndex, CancellationToken cancellationToken) =>
        Task.FromResult(_feedUnits.FirstOrDefault(u => u.PrinterId == printerId && u.Kind == kind && u.UnitIndex == unitIndex));

    public Task<FeedSlot?> GetFeedSlotByIdAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
        Task.FromResult(_feedUnits.SelectMany(u => u.FeedSlots).FirstOrDefault(s => s.Id == feedSlotId));

    public Task<FeedUnit?> GetByFeedSlotIdAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
        Task.FromResult(_feedUnits.FirstOrDefault(u => u.FeedSlots.Any(s => s.Id == feedSlotId)));

    public void TrackNewFeedSlot(FeedSlot feedSlot)
    {
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
