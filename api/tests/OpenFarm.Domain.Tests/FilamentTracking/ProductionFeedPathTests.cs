using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class ProductionFeedPathTests
{
    private static readonly Guid Printer = Guid.NewGuid();

    /// <summary>The printer hasn't said which slot feeds its extruder (or says an AMS slot does).</summary>
    private static readonly IReadOnlyList<ExtruderReading> Unreported = [];

    private static readonly IReadOnlyList<ExtruderReading> ExternalFeeding =
        [new ExtruderReading(0, ActiveSlot: new FeedSlotRef(FeedUnitKindCatalog.ExternalSpool, 0, 0))];

    private static (FeedUnit Ams, FeedSlot[] AmsSlots, FeedUnit External, FeedSlot ExternalSlot) AmsPrinter()
    {
        var ams = FeedUnit.CreateConnectorDerived(Printer, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var slots = new[] { ams.AddFeedSlot(0), ams.AddFeedSlot(1) };
        var external = FeedUnit.CreateConnectorDerived(Printer, FeedUnitKindCatalog.ExternalSpool, 0);
        return (ams, slots, external, external.AddFeedSlot(0));
    }

    [Fact]
    public void ReloaderUsesSelectedSlotAndSupersedesItsExternalPort()
    {
        var port = FeedUnit.CreateConnectorDerived(Printer, FeedUnitKindCatalog.ExternalSpool, 0);
        var portSlot = port.AddFeedSlot(0);
        var reloader = FeedUnit.CreateAutomaticFilamentReloader(Printer, 0, "Reloader", port.Id);
        var standby = reloader.AddFeedSlot(0);
        var feeding = reloader.AddFeedSlot(1);
        reloader.SelectFeedingSlot(feeding.Id);
        ProductionFeedPath.ActiveSlotIds([port, reloader], Unreported).Should().BeEquivalentTo([feeding.Id]);
        reloader.Archive();
        ProductionFeedPath.ActiveSlotIds([port, reloader], Unreported).Should().BeEquivalentTo([portSlot.Id]);
        standby.Should().NotBeNull();
    }

    [Fact]
    public void NoAms_ExternalSpoolIsAlwaysUsable()
    {
        var external = FeedUnit.CreateConnectorDerived(Printer, FeedUnitKindCatalog.ExternalSpool, 0);
        var slot = external.AddFeedSlot(0);

        ProductionFeedPath.ActiveSlotIds([external], Unreported).Should().BeEquivalentTo([slot.Id]);
        ProductionFeedPath.ActiveSlotIds([external], ExternalFeeding).Should().BeEquivalentTo([slot.Id]);
    }

    [Fact]
    public void WithAms_ExternalSpoolIsUnusableUnlessThePrinterSaysItIsTheActiveFeed()
    {
        var (ams, slots, external, externalSlot) = AmsPrinter();

        ProductionFeedPath.ActiveSlotIds([ams, external], Unreported).Should().BeEquivalentTo(slots.Select(s => s.Id));
        ProductionFeedPath.ActiveSlotIds([ams, external], ExternalFeeding).Should().BeEquivalentTo([externalSlot.Id]);
    }

    [Fact]
    public void WithAms_ASlotTheAmsNoLongerReportsDoesNotCount()
    {
        var (ams, slots, external, externalSlot) = AmsPrinter();
        slots[1].Deactivate();

        ProductionFeedPath.ActiveSlotIds([ams, external], Unreported).Should().BeEquivalentTo([slots[0].Id]);

        slots[0].Deactivate();
        ProductionFeedPath.ActiveSlotIds([ams, external], Unreported).Should().BeEquivalentTo([externalSlot.Id], "an AMS with no active slot is not attached");
    }

    [Fact]
    public void WithAms_ReloaderFeedingSlotIsUsableOnEitherPath()
    {
        var (ams, slots, external, _) = AmsPrinter();
        var reloader = FeedUnit.CreateAutomaticFilamentReloader(Printer, 0, "Reloader", external.Id);
        var feeding = reloader.AddFeedSlot(0);
        reloader.SelectFeedingSlot(feeding.Id);

        ProductionFeedPath.ActiveSlotIds([ams, external, reloader], Unreported).Should().BeEquivalentTo(slots.Select(s => s.Id).Append(feeding.Id));
        ProductionFeedPath.ActiveSlotIds([ams, external, reloader], ExternalFeeding).Should().BeEquivalentTo([feeding.Id]);
    }

    [Fact]
    public void TwoExtruders_TheAmsOnOneDoesNotSupersedeTheOthersExternalSpool()
    {
        var ams = FeedUnit.CreateConnectorDerived(Printer, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var amsSlot = ams.AddFeedSlot(0, extruderIndex: 0);
        var right = FeedUnit.CreateConnectorDerived(Printer, FeedUnitKindCatalog.ExternalSpool, 0);
        right.AddFeedSlot(0, extruderIndex: 0);
        var left = FeedUnit.CreateConnectorDerived(Printer, FeedUnitKindCatalog.ExternalSpool, 1);
        var leftSlot = left.AddFeedSlot(0, extruderIndex: 1);

        ProductionFeedPath.ActiveSlotIds([ams, right, left], Unreported).Should().BeEquivalentTo([amsSlot.Id, leftSlot.Id]);
    }

    [Fact]
    public void Toolchanger_EveryHeadsSpoolIsUsable()
    {
        var heads = FeedUnit.CreateConnectorDerived(Printer, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var slots = Enumerable.Range(0, 4).Select(head => heads.AddFeedSlot(head, extruderIndex: head)).ToList();
        IReadOnlyList<ExtruderReading> printing = [new(0), new(1), new(2, ActiveSlot: new FeedSlotRef(FeedUnitKindCatalog.MultiMaterialUnit, 0, 2)), new(3)];

        ProductionFeedPath.ActiveSlotIds([heads], printing).Should().BeEquivalentTo(slots.Select(s => s.Id));
    }
}
