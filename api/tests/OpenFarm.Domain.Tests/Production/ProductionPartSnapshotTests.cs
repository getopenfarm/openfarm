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

    [Fact]
    public void Capture_KeepsThePartsPlate()
    {
        var part = Part.Create("Part", PartFileFormat.ThreeMf, false, "part.gcode.3mf", "/part.gcode.3mf",
            10, 0.4m, null, null, EstimateStatus.Available, 10, 20, 4, 1, new string('a', 64),
            [new("PLA", null, null, null, null)], [], [], plateName: "Cover");

        Part.FromProductionSnapshot(ProductionPartSnapshot.Capture(part)).PlateNumber.Should().Be(4);
    }

    [Fact]
    public void ASnapshotTakenBeforePlates_IsALegacyPart()
    {
        var part = Part.Create("Part", PartFileFormat.GCode, false, "part.gcode", "/part.gcode",
            10, 0.4m, null, null, EstimateStatus.Available, 10, 20, null, 1, new string('a', 64),
            [new("PLA", null, null, null, null)], [], []);
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(ProductionPartSnapshot.Capture(part)))!.AsObject();
        json.Remove("PlateNumber");
        json["PlateCount"] = 2;

        var restored = Part.FromProductionSnapshot(System.Text.Json.JsonSerializer.Deserialize<ProductionPartSnapshot>(json.ToJsonString())!);

        restored.PlateNumber.Should().BeNull();
    }
}
