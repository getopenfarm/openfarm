using FluentAssertions;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Test-after, fixture-replay: which of a project's filament slots its objects print with.
public class ProjectUsedFilamentsTests
{
    private static string Settings(string objects, string plate = "") => $"<config>{objects}<plate>{plate}</plate></config>";

    private static string Obj(string id, string? extruder, string parts = "") =>
        $"<object id=\"{id}\">{(extruder is null ? "" : $"<metadata key=\"extruder\" value=\"{extruder}\"/>")}{parts}</object>";

    private static string OnPlate(string id) => $"<model_instance><metadata key=\"object_id\" value=\"{id}\"/></model_instance>";

    [Fact]
    public void ObjectOnSlotOne_UsesOnlySlotOne() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "1"), OnPlate("2")), false)!.Order().Should().Equal(0);

    [Fact]
    public void ObjectWithNoExtruder_DefaultsToSlotOne() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", null), OnPlate("2")), false)!.Order().Should().Equal(0);

    [Fact]
    public void PartExtruder_OverridesTheObjects()
    {
        var parts = "<part id=\"1\" subtype=\"normal_part\"><metadata key=\"extruder\" value=\"3\"/></part><part id=\"2\" subtype=\"normal_part\"/>";
        ProjectUsedFilaments.Read(Settings(Obj("2", "2", parts), OnPlate("2")), false)!.Order().Should().Equal(1, 2);
    }

    [Fact]
    public void NegativeVolume_IsNotPrinted()
    {
        var parts = "<part id=\"1\" subtype=\"normal_part\"/><part id=\"2\" subtype=\"negative_part\"><metadata key=\"extruder\" value=\"4\"/></part>";
        ProjectUsedFilaments.Read(Settings(Obj("2", "1", parts), OnPlate("2")), false)!.Order().Should().Equal(0);
    }

    [Fact]
    public void ObjectNotOnAnyPlate_IsIgnored() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "1") + Obj("3", "2"), OnPlate("2")), false)!.Order().Should().Equal(0);

    [Fact]
    public void NoPlateInstances_CountsEveryObject() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "1") + Obj("3", "2")), false)!.Order().Should().Equal(0, 1);

    [Fact]
    public void PaintedColours_CannotBeTold() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "1"), OnPlate("2")), paintedColours: true).Should().BeNull();

    [Theory]
    [InlineData("not xml")]
    [InlineData("<config><plate/></config>")]
    public void UnreadableOrWithoutObjects_CannotBeTold(string xml) =>
        ProjectUsedFilaments.Read(xml, false).Should().BeNull();
}
