using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

// Test-after: GET /api/v1/filaments/shortages — queued finite work against the grams on hand.
public class FilamentShortagesTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private async Task<Guid> ColorWithSpoolAsync(string hex, decimal remainingGrams)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var vendor = Vendor.Create($"Vendor {Guid.NewGuid():N}");
        var filament = Filament.Create(vendor.Id, "PLA Matte", "PLA");
        var variant = filament.AddVariant("Teal", hex);
        db.Vendors.Add(vendor);
        db.Filaments.Add(filament);
        db.Spools.Add(Spool.Create(variant.Id, null, 1000, remainingGrams));
        await db.SaveChangesAsync();
        return variant.Id;
    }

    private static async Task<IReadOnlyList<FilamentShortageResponse>> ShortagesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListFilamentShortagesResponse>("/api/v1/filaments/shortages"))!.Shortages;

    [Fact]
    public async Task QueuedFiniteWork_NeedingMoreThanIsOnHand_IsAShortage()
    {
        var client = await ClientAsync();
        var teal = await ColorWithSpoolAsync("#0F766E", 100);
        var part = await PartAsync(requirement: new FilamentRequirementSpec("PLA", "#0F766E", null, null, null, EstimatedGrams: 80));
        await RunAsync(client, part.Id, quantity: 2);
        await RunAsync(client, part.Id, quantity: null); // unlimited work is never queued ahead, so it is not counted

        var shortage = (await ShortagesAsync(client)).Single(s => s.FilamentVariantId == teal);

        shortage.QueuedGrams.Should().Be(160);
        shortage.OnHandGrams.Should().Be(100);
    }

    [Fact]
    public async Task EnoughOnHand_IsNoShortage()
    {
        var client = await ClientAsync();
        var teal = await ColorWithSpoolAsync("#115E59", 1000);
        var part = await PartAsync(requirement: new FilamentRequirementSpec("PLA", "#115E59", null, null, null, EstimatedGrams: 80));
        await RunAsync(client, part.Id, quantity: 2);

        (await ShortagesAsync(client)).Should().NotContain(s => s.FilamentVariantId == teal);
    }
}
