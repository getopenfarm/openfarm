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
}
