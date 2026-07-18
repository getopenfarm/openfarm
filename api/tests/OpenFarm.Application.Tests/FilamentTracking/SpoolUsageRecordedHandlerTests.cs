using FluentAssertions;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.FilamentTracking;

public class SpoolUsageRecordedHandlerTests
{
    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly InMemorySpoolRepository _spoolRepository = new();
    private readonly InMemoryFeedUnitRepository _feedUnitRepository = new();
    private readonly RecordingActivityLogWriter _activityLogWriter = new();
    private readonly RecordingEventPublisher _eventPublisher = new();

    private SpoolUsageRecordedHandler CreateHandler() =>
        new(_spoolRepository, _feedUnitRepository, _activityLogWriter, _eventPublisher);

    private (FeedUnit Unit, FeedSlot[] Slots) CreateFeedUnit(string kind, int slotCount)
    {
        var unit = kind == FeedUnitKindCatalog.AutomaticFilamentReloader
            ? FeedUnit.CreateAutomaticFilamentReloader(_organizationId, Guid.NewGuid(), 0, "Splicer A", null)
            : FeedUnit.CreateConnectorDerived(_organizationId, Guid.NewGuid(), kind, 0);

        var slots = Enumerable.Range(0, slotCount).Select(i => unit.AddFeedSlot(i)).ToArray();
        _feedUnitRepository.Add(unit);
        foreach (var slot in slots)
        {
            _spoolRepository.FeedSlotOwners[slot.Id] = unit.Id;
        }

        return (unit, slots);
    }

    private Spool CreateUsingSpool(Guid filamentVariantId, FeedSlot feedSlot)
    {
        var spool = Spool.Create(_organizationId, filamentVariantId, null, 1000, 1000);
        spool.LoadOntoFeedSlot(feedSlot.Id);
        _spoolRepository.Add(spool);
        return spool;
    }

    [Fact]
    public async Task MultiMaterialUnit_WithMatchingSiblingSpool_AutoContinues()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.MultiMaterialUnit, 2);
        var variantId = Guid.NewGuid();
        var depleted = CreateUsingSpool(variantId, slots[0]);
        var sibling = CreateUsingSpool(variantId, slots[1]);

        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);

        result.Occurred.Should().BeTrue();
        result.ContinuedSpoolId.Should().Be(sibling.Id);
        result.ContinuedFeedSlotId.Should().Be(slots[1].Id);
        result.MaterialMismatch.Should().BeFalse();
        depleted.State.Should().Be(SpoolState.Finished);
        sibling.State.Should().Be(SpoolState.Using, "the continuation target is already loaded — it never moves");
    }

    [Fact]
    public async Task MultiMaterialUnit_WithMultipleMatches_LowestSlotPositionWinsTies()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.MultiMaterialUnit, 3);
        var variantId = Guid.NewGuid();
        var depleted = CreateUsingSpool(variantId, slots[0]);
        var farther = CreateUsingSpool(variantId, slots[2]);
        var nearer = CreateUsingSpool(variantId, slots[1]);

        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);

        result.ContinuedSpoolId.Should().Be(nearer.Id);
    }

    [Fact]
    public async Task MultiMaterialUnit_WithNonMatchingSibling_DoesNotAutoContinue()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.MultiMaterialUnit, 2);
        var depleted = CreateUsingSpool(Guid.NewGuid(), slots[0]);
        CreateUsingSpool(Guid.NewGuid(), slots[1]); // different variant

        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);

        result.Occurred.Should().BeFalse();
        depleted.State.Should().Be(SpoolState.Finished);
    }

    [Fact]
    public async Task MultiMaterialUnit_WithAbsentSibling_DoesNotAutoContinue()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.MultiMaterialUnit, 2);
        var depleted = CreateUsingSpool(Guid.NewGuid(), slots[0]);

        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);

        result.Occurred.Should().BeFalse();
    }

    [Fact]
    public async Task AutomaticFilamentReloader_WithNextSiblingLoaded_ContinuesRegardlessOfMaterialMatch()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var depleted = CreateUsingSpool(Guid.NewGuid(), slots[0]);
        var sibling = CreateUsingSpool(Guid.NewGuid(), slots[1]); // different variant — no match required

        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);

        result.Occurred.Should().BeTrue();
        result.ContinuedSpoolId.Should().Be(sibling.Id);
        result.MaterialMismatch.Should().BeTrue();
    }

    [Fact]
    public async Task AutomaticFilamentReloader_WithMatchingSibling_ContinuesWithoutMismatch()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var variantId = Guid.NewGuid();
        var depleted = CreateUsingSpool(variantId, slots[0]);
        CreateUsingSpool(variantId, slots[1]);

        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);

        result.Occurred.Should().BeTrue();
        result.MaterialMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task AutomaticFilamentReloader_WithNoSiblingLoaded_DoesNotAutoContinue()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var depleted = CreateUsingSpool(Guid.NewGuid(), slots[0]);

        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);

        result.Occurred.Should().BeFalse();
        _activityLogWriter.Entries.Should().Contain(e => e.EventType == "FeedUnitNoQualifyingReload");
    }

    [Fact]
    public async Task ExternalSpool_NeverAutoContinues()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.ExternalSpool, 1);
        var depleted = CreateUsingSpool(Guid.NewGuid(), slots[0]);

        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);

        result.Occurred.Should().BeFalse();
        depleted.State.Should().Be(SpoolState.Finished);
    }

    private sealed class InMemorySpoolRepository : ISpoolRepository
    {
        private readonly List<Spool> _spools = [];

        public void Add(Spool spool) => _spools.Add(spool);

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

        public Task<IReadOnlyList<Spool>> FindLoadedInFeedUnitAsync(Guid feedUnitId, CancellationToken cancellationToken)
        {
            // Test double: caller (SpoolUsageRecordedHandler) is expected to have loaded the
            // FeedUnit's own FeedSlot ids; here we just match any Using spool whose FeedSlotId
            // matches one registered against this feedUnitId via the fixture setup below.
            return Task.FromResult<IReadOnlyList<Spool>>(_spools
                .Where(s => s.State == SpoolState.Using && s.FeedSlotId is not null && FeedSlotOwners.TryGetValue(s.FeedSlotId.Value, out var ownerId) && ownerId == feedUnitId)
                .ToList());
        }

        public Task<Spool?> FindUsingFeedSlotAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
            Task.FromResult(_spools.FirstOrDefault(s => s.State == SpoolState.Using && s.FeedSlotId == feedSlotId));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Dictionary<Guid, Guid> FeedSlotOwners { get; } = [];
    }

    private sealed class InMemoryFeedUnitRepository : IFeedUnitRepository
    {
        private readonly List<FeedUnit> _feedUnits = [];

        public void Add(FeedUnit unit) => _feedUnits.Add(unit);

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

    private sealed class RecordingActivityLogWriter : IActivityLogWriter
    {
        public List<ActivityLogEntry> Entries { get; } = [];

        public Task WriteAsync(ActivityLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEventPublisher : IFilamentTrackingEventPublisher
    {
        public Task SpoolStateChangedAsync(Guid organizationId, Guid spoolId, SpoolState state, decimal remainingWeightGrams, Guid? feedSlotId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FeedUnitAutoContinuedAsync(Guid organizationId, Guid feedUnitId, Guid finishedSpoolId, Guid continuedSpoolId, Guid continuedFeedSlotId, bool materialMismatch, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FeedUnitsChangedAsync(Guid organizationId, Guid printerId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
