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
}
