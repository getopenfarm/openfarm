using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Mcp;

/// <summary>Brands of one material and colour share stock for jobs that name only the colour; a need is suggested once.</summary>
public sealed class McpFilamentForecastTests(McpTestFactory factory) : ProductionTestContext(factory), IClassFixture<McpTestFactory>
{
    private const string Hex = "12AB34";

    [Fact]
    public async Task TwoBrandsOfOneColour_CoverSharedQueuedWorkTogether_AndAPlanAddsOnlyWhatTheyCannot()
    {
        var browser = await ClientAsync();
        var (first, second) = (await BrandAsync("Brand A"), await BrandAsync("Brand B"));
        var part = await PartAsync(grams: 1500, requirement: new FilamentRequirementSpec("PLA", Hex, null, null, null, 1500));
        await RunAsync(browser, part.Id);
        await using var client = await McpTestClient.ConnectAsync(Factory, (await McpTestClient.TokenAsync(browser,
            "filaments:list", "spools:list", "print-jobs:view", "production-runs:create", "printers:list")).Credential);

        var colours = (await client.CallToolAsync("list_filaments", new Dictionary<string, object?> { ["coverDays"] = 30 })).StructuredContent!.Value
            .GetProperty("colours").EnumerateArray().Where(c => c.GetProperty("colorHex").GetString() == Hex).ToList();

        colours.Select(c => c.GetProperty("filamentVariantId").GetGuid()).Should().BeEquivalentTo([first, second]);
        colours.Should().OnlyContain(c => c.GetProperty("sharedQueuedGrams").GetDecimal() == 1500 && c.GetProperty("queuedGrams").GetDecimal() == 0);
        colours.Sum(c => c.GetProperty("suggestedSpools").GetInt32()).Should().Be(0, "2,000 g across both brands covers 1,500 g");
        colours.Sum(c => c.GetProperty("shortGrams").GetDecimal()).Should().Be(0);

        var plan = (await client.CallToolAsync("plan_production", new Dictionary<string, object?>
        {
            ["lines"] = new object[] { new { kind = "Part", id = (await PartAsync(grams: 1000, requirement: new FilamentRequirementSpec("PLA", Hex, null, null, null, 1000))).Id, quantity = 1 } },
        })).StructuredContent!.Value;
        var row = plan.GetProperty("filament").EnumerateArray().Single(f => f.GetProperty("colorHex").GetString() == Hex);
        row.GetProperty("onHandGrams").GetDecimal().Should().Be(2000);
        row.GetProperty("alreadyQueuedGrams").GetDecimal().Should().Be(1500);
        row.GetProperty("shortGrams").GetDecimal().Should().Be(500, "1,500 g queued + 1,000 g planned against 2,000 g on hand");
    }

    /// <summary>A vendor's PLA in the test colour with one full 1 kg spool.</summary>
    private async Task<Guid> BrandAsync(string vendorName)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var vendor = Vendor.Create($"{vendorName} {Guid.NewGuid():N}");
        db.Vendors.Add(vendor);
        var filament = Filament.Create(vendor.Id, "PLA", "PLA");
        var variant = filament.AddVariant("Forest", Hex);
        db.Filaments.Add(filament);
        db.Spools.Add(Spool.Create(variant.Id, null, 1000, 1000));
        await db.SaveChangesAsync();
        return variant.Id;
    }
}
