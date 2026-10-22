using FluentAssertions;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class ProductionPartSnapshotTests
{
    [Fact]
    public void Capture_KeepsEachColorsEstimatedGrams()
    {
        var part = Part.Create("Part", PartFileFormat.GCode, false, "part.gcode", "/part.gcode",
            10, 0.4m, null, null, EstimateStatus.Available, 10, 160, 1, 1, new string('a', 64),
            [new("PLA", "#000000", null, null, null, EstimatedGrams: 120m), new("PLA", "#FFFFFF", null, null, null, EstimatedGrams: 40m)], [], []);

        var restored = Part.FromProductionSnapshot(ProductionPartSnapshot.Capture(part));

        restored.FilamentRequirements.Select(r => r.EstimatedGrams).Should().Equal(120m, 40m);
    }
}
