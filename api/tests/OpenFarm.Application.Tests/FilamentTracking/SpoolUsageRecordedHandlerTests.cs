using FluentAssertions;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.FilamentTracking;

public class SpoolUsageRecordedHandlerTests
{
    private readonly InMemorySpoolRepository _spoolRepository = new();
    private readonly InMemoryFeedUnitRepository _feedUnitRepository = new();
    private readonly RecordingActivityLogWriter _activityLogWriter = new();
    private readonly RecordingEventPublisher _eventPublisher = new();
    private readonly FakePrinterRepository _printers = new();

    private SpoolUsageRecordedHandler CreateHandler() =>
        new(_spoolRepository, _feedUnitRepository, _activityLogWriter, _eventPublisher, _printers);

    private (FeedUnit Unit, FeedSlot[] Slots) CreateFeedUnit(string kind, int slotCount)
    {
        var unit = kind == FeedUnitKindCatalog.AutomaticFilamentReloader
            ? FeedUnit.CreateAutomaticFilamentReloader(Guid.NewGuid(), 0, "Splicer A", null)
            : FeedUnit.CreateConnectorDerived(Guid.NewGuid(), kind, 0);

        var slots = Enumerable.Range(0, slotCount).Select(i => unit.AddFeedSlot(i)).ToArray();
        if (kind == FeedUnitKindCatalog.AutomaticFilamentReloader) unit.SelectFeedingSlot(slots[0].Id);
        _feedUnitRepository.Add(unit);
        foreach (var slot in slots)
        {
            _spoolRepository.FeedSlotOwners[slot.Id] = unit.Id;
        }

        return (unit, slots);
    }

    private Spool CreateUsingSpool(Guid filamentVariantId, FeedSlot feedSlot)
    {
        var spool = Spool.Create(filamentVariantId, null, 1000, 1000);
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
    public async Task Toolheads_NeverContinueOntoAHeadWithAnotherNozzle()
    {
        var printer = Printer.Create("U1", "Snapmaker", "U1", "127.0.0.1", "SN");
        printer.ObserveNozzles([0.4m, 0.6m, 0.4m], DateTimeOffset.UtcNow);
        await _printers.AddAsync(printer, default);
        var unit = FeedUnit.CreateConnectorDerived(printer.Id, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var slots = Enumerable.Range(0, 3).Select(head => unit.AddFeedSlot(head, extruderIndex: head)).ToArray();
        _feedUnitRepository.Add(unit);
        foreach (var slot in slots) _spoolRepository.FeedSlotOwners[slot.Id] = unit.Id;
        var variantId = Guid.NewGuid();
        var depleted = CreateUsingSpool(variantId, slots[0]);
        CreateUsingSpool(variantId, slots[1]);
        var sameNozzle = CreateUsingSpool(variantId, slots[2]);

        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);

        result.ContinuedSpoolId.Should().Be(sameNozzle.Id);
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
        unit.FeedingSlotId.Should().Be(slots[1].Id);
        unit.FeedingSelectionSource.Should().Be("Estimated");
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

    [Fact]
    public async Task Reloader_StandbyDepletion_DoesNotChangeFeedingSlot()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        CreateUsingSpool(Guid.NewGuid(), slots[0]);
        var standby = CreateUsingSpool(Guid.NewGuid(), slots[1]);
        var result = await CreateHandler().ResolveAsync(standby, CancellationToken.None);
        result.Occurred.Should().BeFalse();
        unit.FeedingSlotId.Should().Be(slots[0].Id);
        unit.FeedingSelectionSource.Should().Be("Operator");
    }

    [Fact]
    public async Task Reloader_SlotTwoDepletion_ContinuesBackToSlotOne()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        unit.SelectFeedingSlot(slots[1].Id);
        var standby = CreateUsingSpool(Guid.NewGuid(), slots[0]);
        var depleted = CreateUsingSpool(Guid.NewGuid(), slots[1]);
        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);
        result.ContinuedSpoolId.Should().Be(standby.Id);
        unit.FeedingSlotId.Should().Be(slots[0].Id);
        unit.FeedingSelectionSource.Should().Be("Estimated");
    }

    [Fact]
    public async Task Reloader_NoUsableStandby_ClearsSelection()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var depleted = CreateUsingSpool(Guid.NewGuid(), slots[0]);
        CreateUsingSpool(Guid.NewGuid(), slots[1]);
        slots[1].Deactivate();
        var result = await CreateHandler().ResolveAsync(depleted, CancellationToken.None);
        result.Occurred.Should().BeFalse();
        unit.FeedingSlotId.Should().BeNull();
        unit.FeedingSelectionSource.Should().BeNull();
    }

    [Fact]
    public async Task Configuration_TransfersExternalSpool_AndHidesPort()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var external = FeedUnit.CreateConnectorDerived(unit.PrinterId, FeedUnitKindCatalog.ExternalSpool, 0);
        var externalSlot = external.AddFeedSlot(0);
        external.Reveal();
        _spoolRepository.FeedSlotOwners[externalSlot.Id] = external.Id;
        var spool = CreateUsingSpool(Guid.NewGuid(), externalSlot);
        await new ReloaderConfigurationService(_spoolRepository).ApplyAsync(unit, [unit, external], external.Id, slots[1].Id, true, CancellationToken.None);
        spool.State.Should().Be(SpoolState.Using);
        spool.FeedSlotId.Should().Be(slots[1].Id);
        spool.RemainingWeightGrams.Should().Be(1000);
        external.IsRevealed.Should().BeFalse();
        unit.ReplacesFeedUnitId.Should().Be(external.Id);
    }

    [Fact]
    public async Task Configuration_RejectsOccupiedTransfer_BeforeChangingAssignments()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var external = FeedUnit.CreateConnectorDerived(unit.PrinterId, FeedUnitKindCatalog.ExternalSpool, 0);
        var externalSlot = external.AddFeedSlot(0);
        _spoolRepository.FeedSlotOwners[externalSlot.Id] = external.Id;
        var original = CreateUsingSpool(Guid.NewGuid(), externalSlot);
        CreateUsingSpool(Guid.NewGuid(), slots[0]);
        var act = () => new ReloaderConfigurationService(_spoolRepository).ApplyAsync(unit, [unit, external], external.Id, slots[0].Id, true, CancellationToken.None);
        await act.Should().ThrowAsync<FeedSlotOccupiedException>();
        original.FeedSlotId.Should().Be(externalSlot.Id);
        unit.ReplacesFeedUnitId.Should().BeNull();
    }

    [Fact]
    public async Task Configuration_CanUnloadExternalSpoolInsteadOfTransfer()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var external = FeedUnit.CreateConnectorDerived(unit.PrinterId, FeedUnitKindCatalog.ExternalSpool, 0);
        var externalSlot = external.AddFeedSlot(0);
        _spoolRepository.FeedSlotOwners[externalSlot.Id] = external.Id;
        var original = CreateUsingSpool(Guid.NewGuid(), externalSlot);
        await new ReloaderConfigurationService(_spoolRepository).ApplyAsync(unit, [unit, external], external.Id, slots[0].Id, false, CancellationToken.None);
        original.State.Should().Be(SpoolState.InStorage);
        original.FeedSlotId.Should().BeNull();
        original.RemainingWeightGrams.Should().Be(1000);
    }

    [Fact]
    public async Task Removal_UnloadsBothSpools_ArchivesAndClearsSelection()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var first = CreateUsingSpool(Guid.NewGuid(), slots[0]);
        var second = CreateUsingSpool(Guid.NewGuid(), slots[1]);
        await new RemoveFeedUnitCommandHandler(_feedUnitRepository, _eventPublisher, _spoolRepository)
            .HandleAsync(new(unit.Id), CancellationToken.None);
        unit.ArchivedAtUtc.Should().NotBeNull();
        unit.FeedingSlotId.Should().BeNull();
        unit.FeedSlots.Should().OnlyContain(s => s.DeactivatedAtUtc != null);
        new[] { first, second }.Should().OnlyContain(s => s.State == SpoolState.InStorage && s.FeedSlotId == null && s.RemainingWeightGrams == 1000);
    }

    [Fact]
    public async Task UnloadFeedingSlot_ClearsSelectionWithoutAssumingSwitch()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var spool = CreateUsingSpool(Guid.NewGuid(), slots[0]);
        CreateUsingSpool(Guid.NewGuid(), slots[1]);
        await new UnloadSpoolCommandHandler(_spoolRepository, _feedUnitRepository, _eventPublisher,
                new OpenFarm.Application.Tests.Production.Fakes.InMemorySpoolUsageRecordRepository())
            .HandleAsync(new(spool.Id, UnloadSpoolAction.ReturnToStorage), CancellationToken.None);
        unit.FeedingSlotId.Should().BeNull();
    }

    [Fact]
    public async Task ProductionConsumption_UsesContinuation_AndRepeatedSubmissionDoesNotConsumeTwice()
    {
        var (unit, slots) = CreateFeedUnit(FeedUnitKindCatalog.AutomaticFilamentReloader, 2);
        var first = CreateUsingSpool(Guid.NewGuid(), slots[0]);
        var second = CreateUsingSpool(first.FilamentVariantId, slots[1]);
        var job = OpenFarm.Application.Tests.Production.Fakes.ProductionTestFixtures.CreateQueuedJob(Guid.NewGuid());
        job.BindFilamentUsage([(first.Id, 1000m, null)]);
        var deduction = new OpenFarm.Application.Production.JobFilamentAutoDeduction(
            _spoolRepository, new OpenFarm.Application.Tests.Production.Fakes.InMemorySpoolUsageRecordRepository(), CreateHandler(), _activityLogWriter,
            new OpenFarm.Application.Tests.Profiles.Fakes.FakeFilamentRepository());
        await deduction.ApplyAsync(job, new OpenFarm.Application.Production.JobProgress(OpenFarm.Domain.Production.PrintJobStatus.Completed), CancellationToken.None);
        await deduction.ApplyAsync(job, new OpenFarm.Application.Production.JobProgress(OpenFarm.Domain.Production.PrintJobStatus.Completed), CancellationToken.None);
        first.State.Should().Be(SpoolState.Finished);
        unit.FeedingSlotId.Should().Be(slots[1].Id);
        second.RemainingWeightGrams.Should().Be(1000);
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
        public Task SpoolStateChangedAsync(Guid spoolId, SpoolState state, decimal remainingWeightGrams, Guid? feedSlotId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FeedUnitAutoContinuedAsync(Guid feedUnitId, Guid finishedSpoolId, Guid continuedSpoolId, Guid continuedFeedSlotId, bool materialMismatch, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FeedUnitsChangedAsync(Guid printerId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
