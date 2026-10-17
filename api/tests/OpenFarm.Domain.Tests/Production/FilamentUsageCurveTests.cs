using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class FilamentUsageCurveTests
{
    // Colour A is used only in the first two layers, colour B only in the last two.
    private static readonly double[] A = [10, 20, 20, 20];
    private static readonly double[] B = [0, 0, 5, 10];

    [Fact]
    public void ShareAt_FollowsTheColoursOwnUsage_NotAnEvenSpread()
    {
        var a = FilamentUsageCurve.Create([A])!;
        var b = FilamentUsageCurve.Create([B])!;

        // Stopped in layer 3 of 4: colour A is fully used, colour B has barely started.
        a.ShareAt(3).Should().Be(1);
        b.ShareAt(3).Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void ShareAt_IsEndOfPreviousLayerPlusHalfTheCurrentOne()
    {
        var curve = FilamentUsageCurve.Create([A])!;

        curve.ShareAt(1).Should().BeApproximately(0.25, 1e-9);
        curve.ShareAt(2).Should().BeApproximately(0.75, 1e-9);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    [InlineData(5, 1)]
    public void ShareAt_ClampsOutOfRangeLayers(int layer, double expected) =>
        FilamentUsageCurve.Create([A])!.ShareAt(layer).Should().Be(expected);

    [Fact]
    public void Create_SumsTheToolsOfOneLine()
    {
        var both = FilamentUsageCurve.Create([A, B])!;

        // Summed cumulative is 10, 20, 25, 30: two thirds through layer 2, then 5/6 by the end of layer 3.
        both.ShareAt(3).Should().BeApproximately(0.75, 1e-5);
        both.LayerCount.Should().Be(4);
    }

    [Fact]
    public void Create_ToolsThatExtrudedNothing_HaveNoCurve()
    {
        FilamentUsageCurve.Create([[0, 0, 0]]).Should().BeNull();
        FilamentUsageCurve.Create([]).Should().BeNull();
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var curve = FilamentUsageCurve.Create([A])!;

        FilamentUsageCurve.FromJson(curve.ToJson())!.ShareAt(2).Should().Be(curve.ShareAt(2));
        FilamentUsageCurve.FromJson(null).Should().BeNull();
    }

    [Fact]
    public void FromJson_ReadsTheOriginalBareArray()
    {
        var curve = FilamentUsageCurve.FromJson("[0.25,0.75,1]")!;

        curve.LayerCount.Should().Be(3);
        curve.PrimeShare.Should().Be(0);
        curve.ShareAt(2).Should().BeApproximately(0.5, 1e-9);
    }

    // Layers end at 10, 20, 30, 40 mm after a 10 mm purge (total 50): the purge is used before layer 1 starts.
    private static FilamentUsageCurve WithPurge(params ProgressMark[] marks) =>
        FilamentUsageCurve.FromExtrusion(marks, [new ToolExtrusion(10, [10, 20, 30, 40], marks.Length == 0 ? [] : [0, 5, 10, 20])])!;

    [Fact]
    public void Purge_CountsFromTheMomentThePrintStarts()
    {
        var curve = WithPurge();

        curve.PrimeShare.Should().BeApproximately(0.2, 1e-9);
        curve.ShareAt(0).Should().BeApproximately(0.2, 1e-9);
        curve.ShareAt(1).Should().BeApproximately(0.3, 1e-9); // 10 mm purge + half of layer 1 (5 of 50)
        curve.ShareAt(5).Should().Be(1);
    }

    [Fact]
    public void ShareAt_PercentStretchMidpoint_SitsBetweenItsStartAndTheNextMark()
    {
        // Marks at 0, 5, 10 and 20 mm (layer 1 start, layer 2 start, 50% inside layer 2, layer 3 start); purge 10, total 50.
        var curve = WithPurge(new(1, 0), new(2, 0), new(2, 50), new(3, 50));

        // Stretch (2, 50): from (10+10)/50 = 0.4 to the layer-3 mark (10+20)/50 = 0.6 -> 0.5.
        curve.ShareAt(2, 50).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void ShareAt_PercentTheFileDoesNotKnow_FallsBackToHalfTheLayer()
    {
        var curve = WithPurge(new(1, 0), new(2, 0), new(2, 50), new(3, 50));

        curve.ShareAt(2, 99).Should().Be(curve.ShareAt(2));
    }

    [Fact]
    public void Json_RoundTripsPurgeAndMarks()
    {
        var curve = WithPurge(new(1, 0), new(2, 0), new(2, 50), new(3, 50));

        var back = FilamentUsageCurve.FromJson(curve.ToJson())!;

        back.PrimeShare.Should().Be(curve.PrimeShare);
        back.ShareAt(2, 50).Should().Be(curve.ShareAt(2, 50));
    }
}
