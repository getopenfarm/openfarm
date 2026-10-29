using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.Printers;

public sealed class PrinterCostRatesTests
{
    [Fact]
    public void CalculatesIndependentHourlyRates()
    {
        var rates = new PrinterCostRates(3000m, 500m, 1000m, 5000m, 150m);
        Assert.Equal(0.9m, rates.DepreciationPerHour);
        Assert.Equal(0.075m, rates.ElectricityPerHour(0.5m));
        Assert.Null(rates.ElectricityPerHour(null));
    }

    [Fact]
    public void MissingCostsRemainUnknownAndExplicitZeroIsValid()
    {
        Assert.Null(new PrinterCostRates(100m, null, 0m, 100m, 0m).DepreciationPerHour);
        var free = new PrinterCostRates(0m, 0m, 0m, 100m, 0m);
        Assert.Equal(0m, free.DepreciationPerHour);
        Assert.Equal(0m, free.ElectricityPerHour(0m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonpositiveLifetime(decimal hours) =>
        Assert.Throws<ArgumentException>(() => new PrinterCostRates(0m, 0m, 0m, hours, 0m));
}
