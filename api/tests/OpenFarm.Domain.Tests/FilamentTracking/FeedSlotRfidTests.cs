using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class FeedSlotRfidTests
{
    private const string Black = "5F3D8925633343C29FB9A97FA9125971";
    private const string Brown = "A77739E606CA41FAA0693A6A3BA63726";

    private static FeedSlot NewSlot() =>
        FeedUnit.CreateConnectorDerived(Guid.NewGuid(), FeedUnitKindCatalog.MultiMaterialUnit, 0).AddFeedSlot(0);

    [Fact]
    public void ObserveRfid_ARegisteredTagNewToTheSlot_Links()
    {
        var slot = NewSlot();

        slot.ObserveRfid(Black, registered: true).Should().BeTrue();
        slot.LinkedRfidId.Should().Be(Black);
    }

    [Fact]
    public void ObserveRfid_TheSameTagStillRead_DoesNotLinkAgain()
    {
        var slot = NewSlot();
        slot.ObserveRfid(Black, registered: true);

        // An operator who unloads the spool while it still sits in the AMS is not overruled.
        slot.ObserveRfid(Black, registered: true).Should().BeFalse();
    }

    [Fact]
    public void ObserveRfid_ATagReadAgainAfterTheSlotWasEmpty_LinksAgain()
    {
        var slot = NewSlot();
        slot.ObserveRfid(Black, registered: true);
        slot.ObserveRfid(null, registered: false);

        slot.ObserveRfid(Black, registered: true).Should().BeTrue();
    }

    [Fact]
    public void ObserveRfid_AnUnregisteredTag_IsForgotten_SoRegisteringItLaterLinks()
    {
        var slot = NewSlot();

        slot.ObserveRfid(Black, registered: false).Should().BeFalse();
        slot.LinkedRfidId.Should().BeNull();
        slot.ObserveRfid(Black, registered: true).Should().BeTrue();
    }

    [Fact]
    public void ObserveRfid_AnotherRegisteredTag_Links()
    {
        var slot = NewSlot();
        slot.ObserveRfid(Black, registered: true);

        slot.ObserveRfid(Brown, registered: true).Should().BeTrue();
        slot.LinkedRfidId.Should().Be(Brown);
    }
}
