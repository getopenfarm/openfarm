using FluentAssertions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.Production;

public class FeedTrayResolverTests
{
    private readonly Guid _printer = Guid.NewGuid();
    private readonly FakeFeedUnitRepository _units = new();
    private readonly SlotSpoolRepository _spools = new();

    private FeedTrayResolver Resolver => new(_spools, _units);

    private async Task<FeedUnit> AddUnitAsync(FeedUnit unit)
    {
        await _units.AddAsync(unit, CancellationToken.None);
        return unit;
    }

    private Spool LoadedOn(FeedSlot slot)
    {
        var spool = Spool.Create(Guid.NewGuid(), null, 1000, 1000);
        spool.LoadOntoFeedSlot(slot.Id);
        _spools.Add(spool);
        return spool;
    }

    private async Task<FeedUnit> ExternalAsync() =>
        await AddUnitAsync(FeedUnit.CreateConnectorDerived(_printer, FeedUnitKindCatalog.ExternalSpool, 0));

    [Fact]
    public async Task ExternalTray_WithoutAReloader_IsTheExternalSlotsSpool()
    {
        var external = await ExternalAsync();
        var spool = LoadedOn(external.AddFeedSlot(0));

        (await Resolver.ResolveSpoolAsync(_printer, 254, CancellationToken.None)).Should().Be(spool.Id);
    }

    [Fact]
    public async Task ExternalTray_WithAReloaderReplacingIt_IsTheSpoolTheReloaderIsFeeding()
    {
        var external = await ExternalAsync();
        external.AddFeedSlot(0);
        var reloader = await AddUnitAsync(FeedUnit.CreateAutomaticFilamentReloader(_printer, 0, "AFR", external.Id));
        LoadedOn(reloader.AddFeedSlot(0));
        var feeding = reloader.AddFeedSlot(1);
        var spool = LoadedOn(feeding);
        reloader.SelectFeedingSlot(feeding.Id);

        (await Resolver.ResolveSpoolAsync(_printer, 254, CancellationToken.None)).Should().Be(spool.Id);
    }

    [Fact]
    public async Task ExternalTray_WithAReloaderThatIsNotFeeding_IsUnknown_NotTheSupersededExternalSlot()
    {
        var external = await ExternalAsync();
        LoadedOn(external.AddFeedSlot(0));
        var reloader = await AddUnitAsync(FeedUnit.CreateAutomaticFilamentReloader(_printer, 0, "AFR", external.Id));
        LoadedOn(reloader.AddFeedSlot(0));

        (await Resolver.ResolveSpoolAsync(_printer, 254, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ExternalTray_AnArchivedReloaderIsIgnored()
    {
        var external = await ExternalAsync();
        var spool = LoadedOn(external.AddFeedSlot(0));
        var reloader = await AddUnitAsync(FeedUnit.CreateAutomaticFilamentReloader(_printer, 0, "AFR", external.Id));
        var slot = reloader.AddFeedSlot(0);
        LoadedOn(slot);
        reloader.SelectFeedingSlot(slot.Id);
        reloader.Archive();

        (await Resolver.ResolveSpoolAsync(_printer, 254, CancellationToken.None)).Should().Be(spool.Id);
    }

    [Fact]
    public async Task AmsTray_IsTheSpoolOnThatUnitAndSlot()
    {
        var ams = await AddUnitAsync(FeedUnit.CreateConnectorDerived(_printer, FeedUnitKindCatalog.MultiMaterialUnit, 1));
        ams.AddFeedSlot(0);
        var spool = LoadedOn(ams.AddFeedSlot(2));

        (await Resolver.ResolveSpoolAsync(_printer, 6, CancellationToken.None)).Should().Be(spool.Id);
    }

    /// <summary>Finds the spool in the Using state on a slot, which the shared production fake does not.</summary>
    private sealed class SlotSpoolRepository : ISpoolRepository
    {
        private readonly List<Spool> _spools = [];

        public void Add(Spool spool) => _spools.Add(spool);

        public Task AddAsync(Spool spool, CancellationToken cancellationToken)
        {
            _spools.Add(spool);
            return Task.CompletedTask;
        }

        public Task<Spool?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_spools.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<Spool>> ListAsync(SpoolState? state, Guid? filamentVariantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Spool>>(_spools.ToList());

        public Task<IReadOnlyList<Spool>> FindLoadedInFeedUnitAsync(Guid feedUnitId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Spool>>([]);

        public Task<Spool?> FindUsingFeedSlotAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
            Task.FromResult(_spools.FirstOrDefault(s => s.FeedSlotId == feedSlotId && s.State == SpoolState.Using));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
