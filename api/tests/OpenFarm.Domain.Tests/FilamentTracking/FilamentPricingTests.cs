using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class FilamentPricingTests
{
    [Fact]
    public void ThePricePaidForTheSpool_BeatsTheColourAndTheFilament()
    {
        var price = FilamentPricing.Resolve(spoolPrice: 20m, spoolWeightGrams: 1000, variantPricePerKg: 30m, filamentPricePerKg: 25m);

        price.Should().Be(new ResolvedFilamentPrice(20m, FilamentPriceSource.Spool));
    }

    [Theory]
    [InlineData(10, 500, 20)]
    [InlineData(20, 1000, 20)]
    [InlineData(18.5, 750, 24.6667)]
    public void ThePriceIsPerKilogram_OfTheSpoolsFullWeight(double paid, double weightGrams, double expectedPerKg)
    {
        var price = FilamentPricing.Resolve((decimal)paid, (decimal)weightGrams, null, null);

        price!.Value.Source.Should().Be(FilamentPriceSource.Spool);
        price.Value.PricePerKg.Should().BeApproximately((decimal)expectedPerKg, 0.0001m);
    }

    [Fact]
    public void WithoutAPriceOnTheSpool_TheColoursPriceIsUsed()
    {
        var price = FilamentPricing.Resolve(null, 1000, variantPricePerKg: 30m, filamentPricePerKg: 25m);

        price.Should().Be(new ResolvedFilamentPrice(30m, FilamentPriceSource.Variant));
    }

    [Fact]
    public void WithoutASpoolOrColourPrice_TheFilamentsPriceIsUsed()
    {
        var price = FilamentPricing.Resolve(null, 1000, null, filamentPricePerKg: 25m);

        price.Should().Be(new ResolvedFilamentPrice(25m, FilamentPriceSource.Filament));
    }

    [Fact]
    public void WhenNothingIsPriced_ThereIsNoPrice()
    {
        FilamentPricing.Resolve(null, 1000, null, null).Should().BeNull();
    }

    [Fact]
    public void ZeroIsAPriceAtEveryLevel_NotAGapToFallThrough()
    {
        FilamentPricing.Resolve(0m, 1000, 30m, 25m).Should().Be(new ResolvedFilamentPrice(0m, FilamentPriceSource.Spool));
        FilamentPricing.Resolve(null, 1000, 0m, 25m).Should().Be(new ResolvedFilamentPrice(0m, FilamentPriceSource.Variant));
        FilamentPricing.Resolve(null, 1000, null, 0m).Should().Be(new ResolvedFilamentPrice(0m, FilamentPriceSource.Filament));
    }

    [Fact]
    public void ForASpoolAndItsCatalogEntries_ReadsTheirPrices()
    {
        var filament = Filament.Create(Guid.NewGuid(), "PLA Basic", "PLA");
        var variant = filament.AddVariant("Black", "#000000");
        filament.SetPricePerKg(25m);
        variant.SetPricePerKg(30m);
        var spool = Spool.Create(variant.Id, null, 500, 500);

        FilamentPricing.Resolve(spool, variant, filament).Should().Be(new ResolvedFilamentPrice(30m, FilamentPriceSource.Variant));

        spool.SetPurchase(10m, null, null, null);
        FilamentPricing.Resolve(spool, variant, filament).Should().Be(new ResolvedFilamentPrice(20m, FilamentPriceSource.Spool));

        FilamentPricing.Resolve(spool, null, null).Should().Be(new ResolvedFilamentPrice(20m, FilamentPriceSource.Spool));
    }
}
