using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class ProductionFeedPathTests
{
    [Fact]
    public void ReloaderUsesSelectedSlotAndSupersedesItsExternalPort()
    {
        var org = Guid.NewGuid(); var printer = Guid.NewGuid();
        var port = FeedUnit.CreateConnectorDerived(org, printer, FeedUnitKindCatalog.ExternalSpool, 0);
        var portSlot = port.AddFeedSlot(0);
        var reloader = FeedUnit.CreateAutomaticFilamentReloader(org, printer, 0, "Reloader", port.Id);
        var standby = reloader.AddFeedSlot(0);
        var feeding = reloader.AddFeedSlot(1);
        reloader.SelectFeedingSlot(feeding.Id);
        ProductionFeedPath.ActiveSlotIds([port, reloader]).Should().BeEquivalentTo([feeding.Id]);
        reloader.Archive();
        ProductionFeedPath.ActiveSlotIds([port, reloader]).Should().BeEquivalentTo([portSlot.Id]);
    }
}
