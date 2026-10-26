using FluentAssertions;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Test-after, fixture-replay: which of a project's filament slots its objects print with.
public class ProjectUsedFilamentsTests
{
    private static string Settings(string objects, string plate = "") => $"<config>{objects}<plate>{plate}</plate></config>";

    private static string Obj(string id, string? extruder, string parts = "") =>
        $"<object id=\"{id}\">{(extruder is null ? "" : $"<metadata key=\"extruder\" value=\"{extruder}\"/>")}{parts}</object>";

    private static readonly HashSet<int> NoPaint = [];

    private static string OnPlate(string id) => $"<model_instance><metadata key=\"object_id\" value=\"{id}\"/></model_instance>";

    [Fact]
    public void ObjectOnSlotOne_UsesOnlySlotOne() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "1"), OnPlate("2")), NoPaint)!.Order().Should().Equal(0);

    [Fact]
    public void ObjectWithNoExtruder_DefaultsToSlotOne() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", null), OnPlate("2")), NoPaint)!.Order().Should().Equal(0);

    [Fact]
    public void PartExtruder_OverridesTheObjects()
    {
        var parts = "<part id=\"1\" subtype=\"normal_part\"><metadata key=\"extruder\" value=\"3\"/></part><part id=\"2\" subtype=\"normal_part\"/>";
        ProjectUsedFilaments.Read(Settings(Obj("2", "2", parts), OnPlate("2")), NoPaint)!.Order().Should().Equal(1, 2);
    }

    [Fact]
    public void NegativeVolume_IsNotPrinted()
    {
        var parts = "<part id=\"1\" subtype=\"normal_part\"/><part id=\"2\" subtype=\"negative_part\"><metadata key=\"extruder\" value=\"4\"/></part>";
        ProjectUsedFilaments.Read(Settings(Obj("2", "1", parts), OnPlate("2")), NoPaint)!.Order().Should().Equal(0);
    }

    [Fact]
    public void ObjectNotOnAnyPlate_IsIgnored() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "1") + Obj("3", "2"), OnPlate("2")), NoPaint)!.Order().Should().Equal(0);

    [Fact]
    public void NoPlateInstances_CountsEveryObject() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "1") + Obj("3", "2")), NoPaint)!.Order().Should().Equal(0, 1);

    [Fact]
    public void PaintedSlots_AreAddedToTheVolumeExtruders() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "4"), OnPlate("2")), new HashSet<int> { 2 })!.Order().Should().Equal(2, 3);

    [Fact]
    public void UndecodablePaint_CannotBeTold() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "1"), OnPlate("2")), null).Should().BeNull();

    // Nibbles are read from the end of the string: "841" is a node splitting in two, then its children "4" and "8".
    [Theory]
    [InlineData("4", new[] { 0 })]
    [InlineData("8", new[] { 1 })]
    [InlineData("0C", new[] { 2 })]
    [InlineData("1C", new[] { 3 })]
    [InlineData("0FC", new[] { 17 })]
    [InlineData("841", new[] { 0, 1 })]
    [InlineData("801", new[] { 1 })]
    [InlineData("0", new int[0])]
    public void PaintedSlots_DecodesTheTriangleSelectorString(string hex, int[] expected) =>
        ProjectUsedFilaments.PaintedSlots(hex)!.Order().Should().Equal(expected);

    [Theory]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("1")]
    [InlineData("C")]
    [InlineData("44")]
    public void PaintedSlots_RejectsInvalidStrings(string hex) =>
        ProjectUsedFilaments.PaintedSlots(hex).Should().BeNull();

    [Theory]
    [InlineData("not xml")]
    [InlineData("<config><plate/></config>")]
    public void UnreadableOrWithoutObjects_CannotBeTold(string xml) =>
        ProjectUsedFilaments.Read(xml, NoPaint).Should().BeNull();

    [Fact]
    public void Plates_AreReadInOrderWithTheirNumbersNamesAndObjects()
    {
        const string xml = """
            <config>
              <plate><metadata key="plater_id" value="1"/><metadata key="plater_name" value="Cover"/>
                <model_instance><metadata key="object_id" value="2"/></model_instance></plate>
              <plate><metadata key="plater_id" value="3"/><metadata key="plater_name" value=""/></plate>
            </config>
            """;

        ProjectUsedFilaments.Plates(xml).Select(p => (p.Number, p.Name, string.Join(",", p.ObjectIds)))
            .Should().Equal((1, "Cover", "2"), (3, null, ""));
    }

    [Fact]
    public void OnePlatesObjects_UseOnlyTheirOwnSlots() =>
        ProjectUsedFilaments.Read(Settings(Obj("2", "1") + Obj("3", "2"), OnPlate("2") + OnPlate("3")), NoPaint, new HashSet<string> { "3" })!
            .Order().Should().Equal(1);
}
