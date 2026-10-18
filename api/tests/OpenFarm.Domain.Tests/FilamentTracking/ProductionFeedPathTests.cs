using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class ProductionFeedPathTests
{
    private static readonly Guid Printer = Guid.NewGuid();

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
        ProductionFeedPath.ActiveSlotIds([port, reloader], false).Should().BeEquivalentTo([feeding.Id]);
        reloader.Archive();
        ProductionFeedPath.ActiveSlotIds([port, reloader], false).Should().BeEquivalentTo([portSlot.Id]);
        standby.Should().NotBeNull();
    }

    [Fact]
    public void NoAms_ExternalSpoolIsAlwaysUsable()
    {
        var external = FeedUnit.CreateConnectorDerived(Printer, FeedUnitKindCatalog.ExternalSpool, 0);
        var slot = external.AddFeedSlot(0);

        ProductionFeedPath.ActiveSlotIds([external], false).Should().BeEquivalentTo([slot.Id]);
        ProductionFeedPath.ActiveSlotIds([external], true).Should().BeEquivalentTo([slot.Id]);
    }

    [Fact]
    public void WithAms_ExternalSpoolIsUnusableUnlessThePrinterSaysItIsTheActiveFeed()
    {
        var (ams, slots, external, externalSlot) = AmsPrinter();

        ProductionFeedPath.ActiveSlotIds([ams, external], false).Should().BeEquivalentTo(slots.Select(s => s.Id));
        ProductionFeedPath.ActiveSlotIds([ams, external], true).Should().BeEquivalentTo([externalSlot.Id]);
    }

    [Fact]
    public void WithAms_ASlotTheAmsNoLongerReportsDoesNotCount()
    {
        var (ams, slots, external, externalSlot) = AmsPrinter();
        slots[1].Deactivate();

        ProductionFeedPath.ActiveSlotIds([ams, external], false).Should().BeEquivalentTo([slots[0].Id]);

        slots[0].Deactivate();
        ProductionFeedPath.ActiveSlotIds([ams, external], false).Should().BeEquivalentTo([externalSlot.Id], "an AMS with no active slot is not attached");
    }

    [Fact]
    public void WithAms_ReloaderFeedingSlotIsUsableOnEitherPath()
    {
        var (ams, slots, external, _) = AmsPrinter();
        var reloader = FeedUnit.CreateAutomaticFilamentReloader(Printer, 0, "Reloader", external.Id);
        var feeding = reloader.AddFeedSlot(0);
        reloader.SelectFeedingSlot(feeding.Id);

        ProductionFeedPath.ActiveSlotIds([ams, external, reloader], false).Should().BeEquivalentTo(slots.Select(s => s.Id).Append(feeding.Id));
        ProductionFeedPath.ActiveSlotIds([ams, external, reloader], true).Should().BeEquivalentTo([feeding.Id]);
    }
}
