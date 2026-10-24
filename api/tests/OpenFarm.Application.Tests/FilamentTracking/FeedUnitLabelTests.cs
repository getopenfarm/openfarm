using FluentAssertions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.FilamentTracking;

/// <summary>Feed unit and slot labels come from the printer's catalog model, never from its brand.</summary>
public class FeedUnitLabelTests
{
    [Theory]
    [InlineData("Bambu", "P1S", "AMS 1", "AMS 1 · Slot 1", "AMS 1 · Slot 4")]
    [InlineData("Snapmaker", "U1", "Toolheads", "T1", "T4")]
    [InlineData("System", "Simulation", "Multi-Material Unit 1", "Multi-Material Unit 1 · Slot 1", "Multi-Material Unit 1 · Slot 4")]
    public async Task MultiMaterialUnit_IsNamedByTheModelsFeedUnitStyle(
        string brand, string modelCode, string unitLabel, string firstSlot, string lastSlot)
    {
        var printer = Printer.Create("P", brand, modelCode, "10.0.0.5", "SN-1");
        var feedUnits = new FakeFeedUnitRepository();
        var unit = FeedUnit.CreateConnectorDerived(printer.Id, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        for (var position = 0; position < 4; position++) unit.AddFeedSlot(position);
        await feedUnits.AddAsync(unit, CancellationToken.None);
        var handler = new GetFeedUnitsQueryHandler(new FakePrinterRepository(printer), feedUnits, new FakeSpoolRepository());

        var detail = (await handler.HandleAsync(new GetFeedUnitsQuery(printer.Id), CancellationToken.None)).Single();

        detail.DisplayLabel.Should().Be(unitLabel);
        detail.FeedSlots.Select(s => s.DisplayLabel).Should().HaveElementAt(0, firstSlot).And.HaveElementAt(3, lastSlot);
    }
}
