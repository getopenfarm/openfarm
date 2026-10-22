using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class FilamentStockTests
{
    [Fact]
    public void Of_SumsGramsAndValuesEachSpoolAtItsOwnPrice()
    {
        var stock = FilamentStock.Of([(800m, 20m), (500m, 30m)], usedGrams: 120m);

        stock.OnHandGrams.Should().Be(1300m);
        stock.ActiveSpools.Should().Be(2);
        stock.StockValue.Should().Be(31m); // 0.8 kg × 20 + 0.5 kg × 30
        stock.UnpricedSpools.Should().Be(0);
        stock.UsedGrams.Should().Be(120m);
    }

    [Fact]
    public void Of_CountsAnUnpricedSpoolInsteadOfValuingItAtZero()
    {
        var stock = FilamentStock.Of([(800m, 20m), (500m, null)]);

        stock.StockValue.Should().Be(16m);
        stock.UnpricedSpools.Should().Be(1);
    }

    [Fact]
    public void Of_TreatsAZeroPriceAsAPrice()
    {
        var stock = FilamentStock.Of([(500m, 0m)]);

        stock.StockValue.Should().Be(0m);
        stock.UnpricedSpools.Should().Be(0);
    }

    [Fact]
    public void Of_AnEmptyUnpricedSpoolLeavesTheValueWhole()
    {
        var stock = FilamentStock.Of([(0m, null)]);

        stock.ActiveSpools.Should().Be(1);
        stock.UnpricedSpools.Should().Be(0);
    }

    [Fact]
    public void Of_NoSpools_IsEmpty()
    {
        FilamentStock.Of([]).Should().Be(new FilamentStock(0m, 0, 0m, 0, 0m));
    }

    [Theory]
    [InlineData(499, 500, true)]
    [InlineData(500, 500, false)]
    [InlineData(0, 0, false)]
    public void IsBelow_ComparesOnHandWithTheThreshold(decimal onHand, decimal threshold, bool low)
    {
        FilamentStock.Of([(onHand, null)]).IsBelow(threshold).Should().Be(low);
    }

    [Fact]
    public void IsBelow_WithoutAThreshold_IsNeverLow()
    {
        FilamentStock.Of([]).IsBelow(null).Should().BeFalse();
    }

    [Fact]
    public void Threshold_TheColorsOwnWinsOverTheFilamentDefault()
    {
        FilamentStock.Threshold(variantGrams: 2000m, filamentGrams: 500m, everStocked: false).Should().Be(2000m);
        FilamentStock.Threshold(variantGrams: 0m, filamentGrams: 500m, everStocked: true).Should().Be(0m);
    }

    [Fact]
    public void Threshold_TheFilamentDefaultOnlyCoversColorsEverStocked()
    {
        FilamentStock.Threshold(variantGrams: null, filamentGrams: 500m, everStocked: true).Should().Be(500m);
        FilamentStock.Threshold(variantGrams: null, filamentGrams: 500m, everStocked: false).Should().BeNull();
        FilamentStock.Threshold(variantGrams: null, filamentGrams: null, everStocked: true).Should().BeNull();
    }
}
