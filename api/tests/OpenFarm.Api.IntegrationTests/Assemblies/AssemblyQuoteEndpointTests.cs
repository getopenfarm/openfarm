using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.IntegrationTests.Mcp;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Assemblies;

/// <summary>
/// The quote the Build panel and Pricing table show, against totals worked out by hand (dev-notes/PRICING.md). Its own factory,
/// so the farm-wide failure allowance, model costs and fee profiles it sets do not leak into other classes.
/// </summary>
public sealed class AssemblyQuoteEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DraftQuote_AddsFilamentMachineHardwareCostItemsInheritedCostsAndFailure_ThenPricesEachProfile()
    {
        var browser = await McpTestClient.BrowserAsync(factory);
        var farm = await SeedAsync(browser);

        var quote = await QuoteAsync(browser, new
        {
            lines = new object[]
            {
                new { targetKind = "Part", targetId = farm.Part, quantity = 2 },
                new { targetKind = "NonPrintedComponent", targetId = farm.Magnet, quantity = 4 },
                new { targetKind = "Assembly", targetId = farm.Hinge, quantity = 1 },
            },
            additionalCosts = new object[]
            {
                new { catalogItemId = farm.Box, purpose = "Production", quantity = 1 },
                new { catalogItemId = farm.Courier, purpose = "FinalProduct", quantity = 1 },
            },
            pricing = new { mode = "Margin", value = 30 },
        });

        // 2 direct + 2 from the hinge = 4 units = 2 plates: 120 min, 40 g of Acme PLA Matte · Black at 20/kg.
        quote.Parts.Should().Be(1);
        quote.Print.Time.Value.Should().Be(120);
        quote.Colors.Should().ContainSingle().Which.Should().Match<QuoteColor>(c =>
            c.Name == $"{farm.Vendor} PLA Matte · Black" && c.Grams == 40 && c.Cost == 0.8m && c.CostMissing == 0);
        quote.Filament.Total.Should().Be(0.8m);
        // 2 h at 1/h depreciation and 200 W x 0.5/kWh.
        quote.Depreciation.Total.Should().Be(2m);
        quote.Electricity.Total.Should().Be(0.2m);
        // 10% of filament + machine.
        quote.Failure.Should().Be(0.3m);
        // Labour is 10 min at 12/h, so decimals carry a remainder far below a cent.
        quote.Breakdown.ToDictionary(b => b.Category, b => Math.Round(b.Amount, 10)).Should().Equal(new Dictionary<string, decimal>
        {
            ["Hardware"] = 1m,
            ["Packaging"] = 0.5m,
            ["Shipping"] = 3m,
            ["Labour"] = 2m,
        });
        quote.Shipping.Should().Be(3m);
        quote.Total.Amount.Should().BeApproximately(9.8m, 0.0000001m);
        (quote.Total.Partial, quote.Total.Available).Should().Be((false, true));
        quote.Notes.Should().BeEmpty();

        var shop = quote.Profiles!.Single();
        shop.Name.Should().Be(farm.Profile);
        shop.Pricing.Price.Should().Be(17.17m);
        shop.Pricing.Fees.Should().Be(2.217m);
        shop.Pricing.Profit.Should().BeApproximately(5.153m, 0.0000001m);
        shop.Pricing.BreakEven.Should().BeApproximately(11.4444m, 0.0001m);
    }

    [Fact]
    public async Task SavedQuote_UsesTheSavedRule_AndShowsNoFeesWithoutFeeAccess_AndCountsWhatIsMissing()
    {
        var browser = await McpTestClient.BrowserAsync(factory);
        var farm = await SeedAsync(browser);
        var unpriced = await ItemAsync(new CatalogPricing("Physical", "Hardware", "Piece", "Direct", null));

        var saved = await QuoteAsync(browser, new { assemblyId = farm.Hinge });
        saved.Total.Amount.Should().BeApproximately(0.4m + 1m + 0.1m + 2m + 0.15m, 0.0000001m, "the hinge's 2 parts make 1 plate: 0.4 filament, 1 h machine, 2 labour, 10% failure");
        saved.Profiles!.Single().Pricing.Price.Should().Be(10m, "the hinge saves a fixed price");

        var token = await McpTestClient.TokenAsync(browser, "assemblies:view");
        using var asToken = factory.CreateClient();
        asToken.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Credential);
        var draft = await QuoteAsync(asToken, new { lines = new object[] { new { targetKind = "NonPrintedComponent", targetId = unpriced, quantity = 1 } } });
        draft.Profiles.Should().ContainSingle().Which.Should().Match<QuoteProfile>(p => p.ProfileId == null && p.Name == "No fees");
        draft.CostsPartial.Should().BeTrue();
        draft.Total.Available.Should().BeFalse();
        draft.Notes.Should().Equal("1 component without cost.");
    }

    [Fact]
    public async Task ZeroPricesArePriced_MissingOnesAreNot_AndDepreciationNeedsNoElectricity()
    {
        var browser = await McpTestClient.BrowserAsync(factory);
        (await browser.PutAsJsonAsync("/api/v1/settings/printer-costs/electricity", new { electricityPricePerKwh = (decimal?)null })).EnsureSuccessStatusCode();
        (await browser.PutAsJsonAsync("/api/v1/settings/printer-costs/models", new
        {
            brand = "Bambu",
            modelCode = "X1C",
            printerPurchasePrice = 1000,
            printerUpfrontCost = 0,
            lifetimeServiceCost = 0,
            lifetimePrintingHours = 1000,
            averagePrintingWatts = 200,
        })).EnsureSuccessStatusCode();
        var unpriced = await PartAsync(await VariantAsync(null, "FF0000"), "FF0000");
        var free = await PartAsync(await VariantAsync(0, "00FF00"), "00FF00");
        var unselected = await PartAsync(null, "0000FF");

        var quote = await QuoteAsync(browser, new
        {
            lines = new[] { unpriced, free, unselected }.Select(id => new { targetKind = "Part", targetId = id, quantity = 1 }),
        });

        quote.Filament.Should().Be(new QuoteFilament(0, 1, 1, 1, 0));
        quote.Colors.Should().HaveCount(3);
        quote.Colors.Single(c => c.ColorHex == "00FF00").Should().Match<QuoteColor>(c => c.CostPriced == 1 && c.CostMissing == 0);
        quote.Colors.Single(c => c.ColorHex == "FF0000").Should().Match<QuoteColor>(c => c.CostPriced == 0 && c.CostMissing == 1);
        quote.Colors.Single(c => c.ColorHex == "0000FF").Name.Should().Be("Any PLA · #0000FF");
        quote.Depreciation.Should().Be(new QuoteMetric(3, 3, 0));
        quote.Electricity.Should().Be(new QuoteMetric(0, 0, 3));
        quote.Total.Should().Be(new QuoteTotal(quote.Total.Amount, true, true));
        quote.Notes.Should().Contain(["Some filament weight has no selected catalog variant or color attribution.",
            "Selected filament has no price. Filament cost incomplete."]);
        quote.Notes.Should().Contain(n => n.StartsWith("Electricity incomplete.", StringComparison.Ordinal));
    }

    private async Task<Guid> VariantAsync(decimal? pricePerKg, string hex)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var vendor = Vendor.Create($"V{Guid.NewGuid():N}"[..10]);
        db.Vendors.Add(vendor);
        var filament = Filament.Create(vendor.Id, "PLA", "PLA");
        filament.SetPricePerKg(pricePerKg);
        var variant = filament.AddVariant(hex, hex);
        db.Filaments.Add(filament);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    private async Task<Guid> PartAsync(Guid? variantId, string hex)
    {
        using var scope = factory.Services.CreateScope();
        var part = Part.Create($"Part {Guid.NewGuid():N}", PartFileFormat.ThreeMf, false, "p.3mf", $"/tmp/{Guid.NewGuid():N}.3mf", 1024, 0.4m,
            null, null, EstimateStatus.Available, 60, 20, 1, 1, $"{Guid.NewGuid():N}",
            [new FilamentRequirementSpec("PLA", hex, null, variantId, null, 20)], [], []);
        part.SetEstimation(new EstimationDetails("Sliced", "Bambu Lab X1 Carbon"));
        await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, CancellationToken.None);
        return part.Id;
    }

    private sealed record Farm(Guid Part, Guid Magnet, Guid Box, Guid Courier, Guid Hinge, string Profile, string Vendor);

    private async Task<Farm> SeedAsync(HttpClient browser)
    {
        Guid part;
        var vendorName = $"Acme{Guid.NewGuid():N}"[..10];
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var vendor = Vendor.Create(vendorName);
            db.Vendors.Add(vendor);
            var filament = Filament.Create(vendor.Id, "PLA Matte", "PLA");
            filament.SetPricePerKg(20);
            var black = filament.AddVariant("Black", "000000");
            db.Filaments.Add(filament);
            await db.SaveChangesAsync();
            var bracket = Part.Create($"Bracket {Guid.NewGuid():N}", PartFileFormat.ThreeMf, false, "b.3mf", $"/tmp/{Guid.NewGuid():N}.3mf", 1024, 0.4m,
                null, null, EstimateStatus.Available, 60, 20, 1, 2, $"{Guid.NewGuid():N}",
                [new FilamentRequirementSpec("PLA", "000000", null, black.Id, null, 20)], [], []);
            bracket.SetEstimation(new EstimationDetails("Sliced", "Bambu Lab X1 Carbon"));
            await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(bracket, CancellationToken.None);
            part = bracket.Id;
            var settings = scope.ServiceProvider.GetRequiredService<IFarmSettingsRepository>();
            (await settings.GetAsync(CancellationToken.None)).SetFailureAllowance(10);
            await settings.SaveChangesAsync(CancellationToken.None);
        }
        (await browser.PutAsJsonAsync("/api/v1/settings/printer-costs/electricity", new { electricityPricePerKwh = 0.5 })).EnsureSuccessStatusCode();
        (await browser.PutAsJsonAsync("/api/v1/settings/printer-costs/models", new
        {
            brand = "Bambu",
            modelCode = "X1C",
            printerPurchasePrice = 1000,
            printerUpfrontCost = 0,
            lifetimeServiceCost = 0,
            lifetimePrintingHours = 1000,
            averagePrintingWatts = 200,
        })).EnsureSuccessStatusCode();
        var magnet = await ItemAsync(new CatalogPricing("Physical", "Hardware", "Piece", "Direct", 0.25m));
        var box = await ItemAsync(new CatalogPricing("Physical", "Packaging", "Piece", "Direct", 0.5m));
        var courier = await ItemAsync(new CatalogPricing("Service", "Shipping", "Application", "Direct", 3m));
        var labour = await ItemAsync(new CatalogPricing("Service", "Labour", "Hour", "Direct", 12m));
        var hinge = await browser.PostAsJsonAsync("/api/v1/assemblies", new
        {
            name = $"Hinge {Guid.NewGuid():N}",
            lines = new[] { new { targetKind = "Part", targetId = part, quantity = 2 } },
            additionalCosts = new[] { new { catalogItemId = labour, purpose = "Production", quantity = 10 } },
            pricing = new { mode = "Price", value = 10 },
        });
        hinge.EnsureSuccessStatusCode();
        var profile = $"Shop {Guid.NewGuid():N}";
        foreach (var existing in (await browser.GetFromJsonAsync<JsonElement>("/api/v1/settings/fees")).GetProperty("profiles").EnumerateArray())
            (await browser.DeleteAsync($"/api/v1/settings/fees/profiles/{existing.GetProperty("id").GetGuid()}")).EnsureSuccessStatusCode();
        (await browser.PostAsJsonAsync("/api/v1/settings/fees/profiles", new
        {
            name = profile,
            fees = new object[] { new { name = "Commission", type = "Percent", amount = 10 }, new { name = "Order", type = "Fixed", amount = 0.5 } },
        })).EnsureSuccessStatusCode();
        return new Farm(part, magnet, box, courier, (await hinge.Content.ReadFromJsonAsync<AssemblyResponse>())!.Id, profile, vendorName);
    }

    private async Task<Guid> ItemAsync(CatalogPricing pricing)
    {
        using var scope = factory.Services.CreateScope();
        var item = NonPrintedComponent.Create($"Item {Guid.NewGuid():N}", null);
        item.Configure(pricing);
        await scope.ServiceProvider.GetRequiredService<INonPrintedComponentRepository>().AddAsync(item, CancellationToken.None);
        return item.Id;
    }

    private static async Task<AssemblyQuote> QuoteAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/assemblies/quote", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssemblyQuote>(Json))!;
    }
}
