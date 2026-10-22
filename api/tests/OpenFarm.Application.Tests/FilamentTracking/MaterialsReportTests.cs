using FluentAssertions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.FilamentTracking;

public class MaterialsReportTests
{
    private static readonly MaterialsReportColor Red = new(Guid.NewGuid(), "Bambu", "PLA Basic", "Red", "#ff0000");
    private static readonly MaterialsReportColor Blue = new(Guid.NewGuid(), "Bambu", "PLA Basic", "Blue", "#0000ff");

    [Fact]
    public void Of_SplitsUsedFromWaste_CostsOnlyPricedGrams_AndCountsEachPrintOnce()
    {
        var twoColours = Guid.NewGuid();

        var report = MaterialsReport.Of(
            [
                new(twoColours, PrintJobStatus.Completed, Red.FilamentVariantId, 30, 20, false),
                new(twoColours, PrintJobStatus.Completed, Blue.FilamentVariantId, 10, null, false),
                new(Guid.NewGuid(), PrintJobStatus.Cancelled, Red.FilamentVariantId, 10, 20, false),
                new(Guid.NewGuid(), PrintJobStatus.Failed, null, null, null, true),
            ],
            new Dictionary<Guid, decimal> { [Blue.FilamentVariantId] = 250 },
            new Dictionary<Guid, MaterialsReportColor> { [Red.FilamentVariantId] = Red, [Blue.FilamentVariantId] = Blue });

        report.Rows.Select(r => r.Color).Should().Equal(Blue, Red, null);
        report.Rows[1].Should().BeEquivalentTo(new { UsedGrams = 30m, WasteGrams = 10m, WasteShare = 0.25m, Cost = 0.8m, UnpricedGrams = 0m, PrintCount = 2 });
        report.Rows[0].Should().BeEquivalentTo(new { UsedGrams = 10m, WrittenOffGrams = 250m, Cost = 0m, UnpricedGrams = 10m, PrintCount = 1 });
        report.Rows[2].Should().BeEquivalentTo(new { UsedGrams = 0m, WasteGrams = 0m, NotDeductedCount = 1, PrintCount = 1 });
        report.Total.Should().BeEquivalentTo(new
        {
            UsedGrams = 40m,
            WasteGrams = 10m,
            WasteShare = 0.2m,
            WrittenOffGrams = 250m,
            Cost = 0.8m,
            UnpricedGrams = 10m,
            PrintCount = 3,
            NotDeductedCount = 1,
        });
    }

    [Fact]
    public void WasteShare_WhenPrintsTookNothing_IsNull() =>
        MaterialsReportRow.Of(Red, [], writtenOffGrams: 100).WasteShare.Should().BeNull();
}
