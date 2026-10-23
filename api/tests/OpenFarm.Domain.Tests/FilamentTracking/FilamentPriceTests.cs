using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class FilamentPriceTests
{
    [Fact]
    public void Prices_StartUnknown_AndAllowZeroAndClearing()
    {
        var filament = Filament.Create(Guid.NewGuid(), "PLA Basic", "PLA");
        var variant = filament.AddVariant("Black", "#000000");
        filament.PricePerKg.Should().BeNull();
        variant.PricePerKg.Should().BeNull();
        filament.SetPricePerKg(25.50m);
        variant.SetPricePerKg(0m);
        (variant.PricePerKg ?? filament.PricePerKg).Should().Be(0m);
        variant.SetPricePerKg(null);
        (variant.PricePerKg ?? filament.PricePerKg).Should().Be(25.50m);
        filament.SetPricePerKg(null);
        filament.PricePerKg.Should().BeNull();
    }

    [Fact]
    public void NegativePrices_AreRejected_WithoutChangingExistingPrice()
    {
        var filament = Filament.Create(Guid.NewGuid(), "PLA", "PLA");
        var variant = filament.AddVariant("Black", "#000000");
        filament.SetPricePerKg(20);
        variant.SetPricePerKg(30);
        var updateFilament = () => filament.SetPricePerKg(-1);
        var updateVariant = () => variant.SetPricePerKg(-1);
        updateFilament.Should().Throw<ArgumentOutOfRangeException>();
        updateVariant.Should().Throw<ArgumentOutOfRangeException>();
        filament.PricePerKg.Should().Be(20);
        variant.PricePerKg.Should().Be(30);
    }

    [Fact]
    public void LowStockThresholds_StartUnset_AndAllowZeroAndClearing()
    {
        var filament = Filament.Create(Guid.NewGuid(), "PLA Basic", "PLA");
        var variant = filament.AddVariant("Black", "#000000");
        filament.LowStockGrams.Should().BeNull();
        variant.LowStockGrams.Should().BeNull();
        filament.SetLowStockGrams(500m);
        variant.SetLowStockGrams(0m);
        variant.LowStockGrams.Should().Be(0m);
        variant.SetLowStockGrams(null);
        filament.SetLowStockGrams(null);
        filament.LowStockGrams.Should().BeNull();
        variant.LowStockGrams.Should().BeNull();
    }

    [Fact]
    public void NegativeLowStockThresholds_AreRejected_WithoutChangingTheExistingOne()
    {
        var filament = Filament.Create(Guid.NewGuid(), "PLA", "PLA");
        var variant = filament.AddVariant("Black", "#000000");
        filament.SetLowStockGrams(500m);
        variant.SetLowStockGrams(250m);
        var updateFilament = () => filament.SetLowStockGrams(-1);
        var updateVariant = () => variant.SetLowStockGrams(-1);
        updateFilament.Should().Throw<ArgumentOutOfRangeException>();
        updateVariant.Should().Throw<ArgumentOutOfRangeException>();
        filament.LowStockGrams.Should().Be(500m);
        variant.LowStockGrams.Should().Be(250m);
    }

    [Fact]
    public void DaysThresholds_StartUnset_AndAllowZeroAndClearing()
    {
        var filament = Filament.Create(Guid.NewGuid(), "PLA Basic", "PLA");
        var variant = filament.AddVariant("Black", "#000000");
        filament.LowStockDays.Should().BeNull();
        variant.LowStockDays.Should().BeNull();
        filament.SetLowStockDays(14);
        variant.SetLowStockDays(0);
        variant.LowStockDays.Should().Be(0);
        variant.SetLowStockDays(null);
        filament.SetLowStockDays(null);
        filament.LowStockDays.Should().BeNull();
        variant.LowStockDays.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void DaysThresholds_OutsideAYear_AreRejected_WithoutChangingTheExistingOne(int days)
    {
        var filament = Filament.Create(Guid.NewGuid(), "PLA", "PLA");
        var variant = filament.AddVariant("Black", "#000000");
        filament.SetLowStockDays(14);
        variant.SetLowStockDays(7);
        var updateFilament = () => filament.SetLowStockDays(days);
        var updateVariant = () => variant.SetLowStockDays(days);
        updateFilament.Should().Throw<ArgumentOutOfRangeException>();
        updateVariant.Should().Throw<ArgumentOutOfRangeException>();
        filament.LowStockDays.Should().Be(14);
        variant.LowStockDays.Should().Be(7);
    }
}
