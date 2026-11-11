using FluentAssertions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Tests.Production;

public sealed class FilamentForecastTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly ColourKey BlackPla = ColourKey.Of("PLA", "000000");

    private static VariantStock Colour(string vendor, decimal onHand, decimal used30Days = 0, string hex = "000000") =>
        new(Guid.NewGuid(), Guid.NewGuid(), vendor, "PLA", "PLA", "Black", hex, new FilamentStock(onHand, 1, 0, 0, used30Days), null);

    private static SpoolPurchase Bought(VariantStock colour, DateOnly on, decimal spoolGrams = 1000) =>
        new(colour.FilamentVariantId, spoolGrams, 22, "Shop", new DateTimeOffset(on.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static IReadOnlyList<FilamentForecast> Forecast(IEnumerable<VariantStock> stock, int coverDays,
        Dictionary<Guid, decimal>? own = null, decimal shared = 0, params SpoolPurchase[] purchases) =>
        FilamentForecasts.Compute([.. stock], new QueuedDemand(own ?? [], new Dictionary<ColourKey, decimal> { [BlackPla] = shared }),
            purchases.ToDictionary(p => p.FilamentVariantId), coverDays, Today);

    [Fact]
    public void AJobAnyBrandCanPrint_UsesTheBrandWithStock_InsteadOfBuyingAnother()
    {
        var bambu = Colour("Bambu Lab", onHand: 0);
        var polymaker = Colour("Polymaker", onHand: 5000);

        var forecast = Forecast([bambu, polymaker], coverDays: 30, shared: 1000);

        forecast.Should().OnlyContain(f => f.SuggestedSpools == 0 && f.ShortGrams == 0);
        forecast.Should().OnlyContain(f => f.SharedQueuedGrams == 1000 && f.QueuedGrams == 0);
    }

    [Fact]
    public void TwoEmptyBrands_BuyOnce_TheBrandBoughtLast()
    {
        var bambu = Colour("Bambu Lab", onHand: 0);
        var polymaker = Colour("Polymaker", onHand: 0);

        var forecast = Forecast([bambu, polymaker], coverDays: 30, shared: 1000,
            purchases: [Bought(bambu, new(2026, 10, 1)), Bought(polymaker, new(2026, 11, 1), spoolGrams: 500)]);

        forecast.Single(f => f.Stock == polymaker).Should().Match<FilamentForecast>(f => f.SuggestedSpools == 2 && f.ShortGrams == 1000 && f.NeededGrams == 1000);
        forecast.Single(f => f.Stock == bambu).Should().Match<FilamentForecast>(f => f.SuggestedSpools == 0 && f.ShortGrams == 0);
    }

    [Fact]
    public void RecentUseAndSharedJobs_AreCoveredByEveryBrandsStockTogether()
    {
        // 600 g of use over 30 days + 1,500 g queued = 2,100 g against 2,800 g on hand: nothing to buy.
        var forecast = Forecast([Colour("Bambu Lab", onHand: 800, used30Days: 600), Colour("Polymaker", onHand: 2000)], coverDays: 30, shared: 1500);

        forecast.Should().OnlyContain(f => f.SuggestedSpools == 0 && f.ShortGrams == 0);
    }

    [Fact]
    public void AJobTiedToOneBrand_NeedsThatBrand_EvenWhenAnotherHasStock()
    {
        var bambu = Colour("Bambu Lab", onHand: 0);
        var polymaker = Colour("Polymaker", onHand: 5000);

        var forecast = Forecast([bambu, polymaker], coverDays: 30, own: new() { [bambu.FilamentVariantId] = 1000 });

        forecast.Single(f => f.Stock == bambu).Should().Match<FilamentForecast>(f =>
            f.QueuedGrams == 1000 && f.ShortGrams == 1000 && f.SuggestedSpools == 1 && f.RunsOutOn == Today);
        forecast.Single(f => f.Stock == polymaker).SuggestedSpools.Should().Be(0);
    }

    [Fact]
    public void ABrandsOwnJobsUseItsStockFirst_ThenWhatIsLeftServesSharedWork()
    {
        // Bambu: 1,000 g, 700 g of it for its own jobs; 300 g left plus Polymaker's 200 g cannot cover 800 g of shared work.
        var bambu = Colour("Bambu Lab", onHand: 1000);
        var polymaker = Colour("Polymaker", onHand: 200);

        var forecast = Forecast([bambu, polymaker], coverDays: 0, own: new() { [bambu.FilamentVariantId] = 700 }, shared: 800,
            purchases: [Bought(bambu, new(2026, 11, 1))]);

        forecast.Sum(f => f.ShortGrams).Should().Be(300);
        forecast.Single(f => f.Stock == bambu).SuggestedSpools.Should().Be(1);
    }

    [Fact]
    public void OneBrand_KeepsThePlainFormula()
    {
        // 3000 g over 30 days = 100 g/day; 14 days of cover + 500 g queued - 600 g on hand = 1300 g.
        var only = Colour("Bambu Lab", onHand: 600, used30Days: 3000);

        var forecast = Forecast([only], coverDays: 14, shared: 500, purchases: Bought(only, new(2026, 11, 1))).Single();

        forecast.AveragePerDayGrams.Should().Be(100);
        forecast.NeededGrams.Should().Be(1300);
        forecast.SuggestedSpools.Should().Be(2);
        Forecast([only], 14, shared: 500, purchases: Bought(only, new(2026, 11, 1), spoolGrams: 250)).Single().SuggestedSpools.Should().Be(6);
        Forecast([only], 14, shared: 500).Single().SuggestedSpools.Should().Be(2, "1 kg spools are assumed when none was bought");
    }

    [Fact]
    public void RunsOutAfterQueuedWork_AndNeverWithoutRecentUse()
    {
        Forecast([Colour("Bambu Lab", onHand: 1000, used30Days: 3000)], 30, shared: 500).Single().RunsOutOn.Should().Be(Today.AddDays(5));
        Forecast([Colour("Bambu Lab", onHand: 100, used30Days: 3000)], 30, shared: 500).Single().RunsOutOn.Should().Be(Today);
        Forecast([Colour("Bambu Lab", onHand: 1000)], 30).Single().RunsOutOn.Should().BeNull();
        Forecast([Colour("Bambu Lab", onHand: 5000)], 30).Single().SuggestedSpools.Should().Be(0);
        Forecast([Colour("Bambu Lab", onHand: 10_000, used30Days: 0.1m)], 30).Single().RunsOutOn.Should().BeNull("millions of days away would overflow a date");
    }

    [Fact]
    public void ColoursWithDifferentHexCodes_AreSeparateGroups()
    {
        var forecast = Forecast([Colour("Bambu Lab", onHand: 0), Colour("Polymaker", onHand: 5000, hex: "0A0A0A")], coverDays: 30, shared: 1000);

        forecast.Single(f => f.Stock.VendorName == "Bambu Lab").SuggestedSpools.Should().Be(1);
        forecast.Single(f => f.Stock.VendorName == "Polymaker").SharedQueuedGrams.Should().Be(0);
    }
}
