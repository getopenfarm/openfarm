using FluentAssertions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Production;
using OpenFarm.Application.Tests.Production.Fakes;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.FilamentTracking;

public class EditSpoolCommandHandlerTests
{
    private readonly InMemorySpoolUsageRecordRepository _usage = new();
    private readonly FakeFeedUnitRepository _feedUnits = new();
    private readonly RecordingDispatcher _dispatcher = new();

    private sealed class RecordingDispatcher : IProductionDispatcher
    {
        public List<Guid> Requested { get; } = [];

        public void RequestDispatchAttempt(Guid printerId) => Requested.Add(printerId);
    }

    private static Spool NewSpool(decimal remaining = 800) => Spool.Create(Guid.NewGuid(), "Shelf B", 1000, remaining);

    private EditSpoolCommandHandler Handler(params Spool[] spools) =>
        new(new InMemorySpoolRepository(spools), _usage, _feedUnits, _dispatcher);

    [Fact]
    public async Task ReweighingALoadedSpool_AsksItsPrinterToLookAtTheQueueAgain()
    {
        var printerId = Guid.NewGuid();
        var unit = FeedUnit.CreateConnectorDerived(printerId, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var slot = unit.AddFeedSlot(0);
        await _feedUnits.AddAsync(unit, CancellationToken.None);
        var loaded = NewSpool(50);
        loaded.LoadOntoFeedSlot(slot.Id);

        await Handler(loaded).HandleAsync(new EditSpoolCommand(loaded.Id, "Shelf B", 500), CancellationToken.None);
        await Handler(loaded).HandleAsync(new EditSpoolCommand(loaded.Id, "Shelf C", 500), CancellationToken.None);

        _dispatcher.Requested.Should().Equal(printerId);
    }

    [Fact]
    public async Task ReweighingASpoolOnTheShelf_WakesNoPrinter()
    {
        var spool = NewSpool(800);

        await Handler(spool).HandleAsync(new EditSpoolCommand(spool.Id, "Shelf B", 700), CancellationToken.None);

        _dispatcher.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task ALowerWeight_AddsOneCorrectionEntryWithTheReason()
    {
        var spool = NewSpool(800);

        await Handler(spool).HandleAsync(new EditSpoolCommand(spool.Id, "Shelf B", 750, "weighed on scale"), CancellationToken.None);

        spool.RemainingWeightGrams.Should().Be(750);
        var entry = _usage.Records.Should().ContainSingle().Subject;
        entry.SpoolId.Should().Be(spool.Id);
        entry.Source.Should().Be(SpoolUsageSource.Correction);
        entry.GramsConsumed.Should().Be(50);
        entry.RemainingAfterGrams.Should().Be(750);
        entry.Reason.Should().Be("weighed on scale");
    }

    [Fact]
    public async Task AHigherWeight_AddsAnEntryThatPutsFilamentBack()
    {
        var spool = NewSpool(750);

        await Handler(spool).HandleAsync(new EditSpoolCommand(spool.Id, "Shelf B", 780), CancellationToken.None);

        _usage.Records.Should().ContainSingle(r => r.GramsConsumed == -30 && r.RemainingAfterGrams == 780 && r.Reason == null);
    }

    [Fact]
    public async Task TheSameWeight_AddsNoEntry_EvenWithAReason()
    {
        var spool = NewSpool(800);

        await Handler(spool).HandleAsync(new EditSpoolCommand(spool.Id, "Shelf C", 800, "no change"), CancellationToken.None);

        spool.Label.Should().Be("Shelf C");
        _usage.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task PurchaseInfo_IsReplaced_AndNeedsNoWeightChangeOrEntry()
    {
        var spool = NewSpool(800);

        await Handler(spool).HandleAsync(
            new EditSpoolCommand(spool.Id, "Shelf B", 800, Price: 18.5m, Supplier: " Filament Shop ", OrderNumber: "PO-1042", ReceivedOn: new DateOnly(2026, 10, 20)),
            CancellationToken.None);

        spool.Price.Should().Be(18.5m);
        spool.Supplier.Should().Be("Filament Shop");
        spool.OrderNumber.Should().Be("PO-1042");
        spool.ReceivedOn.Should().Be(new DateOnly(2026, 10, 20));
        _usage.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task OmittingThePurchaseInfo_ClearsIt()
    {
        var spool = NewSpool(800);
        spool.SetPurchase(18.5m, "Filament Shop", "PO-1042", new DateOnly(2026, 10, 20));

        await Handler(spool).HandleAsync(new EditSpoolCommand(spool.Id, "Shelf B", 800), CancellationToken.None);

        spool.Price.Should().BeNull();
        spool.Supplier.Should().BeNull();
        spool.OrderNumber.Should().BeNull();
        spool.ReceivedOn.Should().BeNull();
    }

    [Fact]
    public async Task AWeightAboveTheSpoolCapacity_Throws_AndAddsNoEntry()
    {
        var spool = NewSpool(800);

        var edit = () => Handler(spool).HandleAsync(new EditSpoolCommand(spool.Id, "Shelf B", 1500), CancellationToken.None);

        await edit.Should().ThrowAsync<ArgumentException>();
        _usage.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownSpool_IsNotFound()
    {
        var edit = () => Handler().HandleAsync(new EditSpoolCommand(Guid.NewGuid(), null, 100), CancellationToken.None);

        await edit.Should().ThrowAsync<SpoolNotFoundException>();
    }
}
