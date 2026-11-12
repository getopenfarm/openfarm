using FluentAssertions;
using OpenFarm.Application.Parts;

namespace OpenFarm.Application.Tests.Parts;

public class PrintEstimatesTests
{
    [Theory]
    [InlineData(1, 3, 2, true)]
    [InlineData(20, 60, 40, false)]
    [InlineData(21, 63, 42, true)]
    [InlineData(0, 0, 0, false)]
    public void ExactUnits_ScaleBothMetricsWithoutRoundingToFullPlates(int units, decimal minutes, decimal grams, bool approximate)
    {
        var result = PrintEstimates.Calculate(units, 20, 60, 40, []);
        result.Time.Should().Be(new EstimateMetric(minutes, 0));
        result.Filament.Should().Be(new EstimateMetric(grams, 0));
        result.Approximate.Should().Be(approximate);
    }

    [Fact]
    public void CompleteColorWeights_TakePrecedenceOverScalar_AndPreserveVariants()
    {
        var variant = Guid.NewGuid();
        var result = PrintEstimates.Calculate(3, 2, 60, 100,
            [new("PLA", "#000000", variant, 8), new("PLA", "#FFFFFF", null, 4)]);
        result.Time.Value.Should().Be(90);
        result.Filament.Value.Should().Be(18);
        result.Colors.Sum(c => c.Grams).Should().Be(18);
        result.Colors[0].VariantId.Should().Be(variant);
    }

    [Fact]
    public void ScalarFallback_IsAttributedOnlyWhenThereIsASingleColor()
    {
        var single = PrintEstimates.Calculate(1, 2, null, 10, [new("PLA", "#000000", Guid.NewGuid(), null)]);
        single.Filament.Should().Be(new EstimateMetric(5, 0));
        single.Colors.Single().ColorHex.Should().Be("#000000");
        var multi = PrintEstimates.Calculate(1, 2, null, 10, [new("PLA", "#000000", null, 3), new("PLA", "#FFFFFF", null, null)]);
        multi.Colors.Should().ContainSingle();
        multi.Colors.Single().ColorHex.Should().BeNull();
        multi.Filament.Value.Should().Be(5);
    }

    [Fact]
    public void MissingMetricsRemainIndependent_AndKnownColorSubtotalsArePartial()
    {
        var result = PrintEstimates.Calculate(1, 2, null, null, [new("PLA", "#000000", null, 8), new("PLA", "#FFFFFF", null, null)]);
        result.Time.Should().Be(new EstimateMetric(null, 1));
        result.Filament.Should().Be(new EstimateMetric(4, 1));
        var total = PrintEstimates.Sum([result, PrintEstimates.Calculate(2, 2, 0, 0, [])]);
        total.Time.Should().Be(new EstimateMetric(0, 1));
        total.Filament.Should().Be(new EstimateMetric(4, 1));
    }

    [Fact]
    public void MissingAndEmptySources_AreDifferentFromKnownZero()
    {
        PrintEstimates.ForPart(null, 1).Filament.Should().Be(new EstimateMetric(null, 1));
        PrintEstimates.Sum([]).Filament.Should().Be(new EstimateMetric(0, 0));
        PrintEstimates.Calculate(1, 1, 0, 0, []).Time.Should().Be(new EstimateMetric(0, 0));
    }

    [Fact]
    public void GroupingAColor_DoesNotHideMissingContributions()
    {
        var total = PrintEstimates.Sum([
            PrintEstimates.Calculate(1, 1, 10, null, [new("PLA", "#000000", null, 2)]),
            PrintEstimates.Calculate(1, 1, 10, null, [new("PLA", "#000000", null, null)])]);
        total.Filament.Should().Be(new EstimateMetric(2, 1));
        total.Colors.Single().Missing.Should().Be(1);
    }

    [Fact]
    public void AColourFreeChoice_IsOneRowAcrossFileColours()
    {
        var aliz = Guid.NewGuid();
        var total = PrintEstimates.Sum([
            PrintEstimates.Calculate(1, 1, 10, null, [new("PETG", "#000000", null, 2, FilamentId: aliz), new("PLA", "#000000", null, 1, AnyColor: true)]),
            PrintEstimates.Calculate(1, 1, 10, null, [new("PETG", "#FFFFFF", null, 3, FilamentId: aliz), new("PLA", "#FFFFFF", null, 4, AnyColor: true)])]);

        total.Colors.Should().BeEquivalentTo([
            new EstimateColor("PETG", null, null, 5, FilamentId: aliz),
            new EstimateColor("PLA", null, null, 5, AnyColor: true)]);
    }
}
