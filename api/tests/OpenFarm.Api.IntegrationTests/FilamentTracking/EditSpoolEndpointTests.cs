using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Api.IntegrationTests.Production;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

// Test-after: PUT /api/v1/spools/{id} — label + remaining weight in one save.
public class EditSpoolEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static async Task<SpoolResponse> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ListSpoolsResponse>("/api/v1/spools"))!.Spools.Single(s => s.Id == id);

    [Fact]
    public async Task Edit_UpdatesLabelAndWeight_AndLogsSpoolEdited()
    {
        var client = await ClientAsync();
        var variantId = await FilamentTrackingTestHelpers.CreateFilamentVariantAsync(client);
        var spoolId = await FilamentTrackingTestHelpers.RegisterSpoolAsync(client, variantId, 1000);

        var response = await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { label = "Shelf C-3", remainingWeightGrams = 420m });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<SpoolResponse>())!;
        body.Label.Should().Be("Shelf C-3");
        body.RemainingWeightGrams.Should().Be(420m);
        (await GetAsync(client, spoolId)).Label.Should().Be("Shelf C-3");
        (await ActivityEventTypesAsync(client, spoolId)).Should().Contain("SpoolEdited");
    }

    [Fact]
    public async Task Edit_ReplacesPurchaseInfo_AndClearsItWhenOmitted()
    {
        var client = await ClientAsync();
        var variantId = await FilamentTrackingTestHelpers.CreateFilamentVariantAsync(client);
        var spoolId = await FilamentTrackingTestHelpers.RegisterSpoolAsync(client, variantId, 1000);

        var saved = await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new
        {
            label = "Shelf B",
            remainingWeightGrams = 1000m,
            price = 20m,
            supplier = " Filament Shop ",
            orderNumber = "PO-1",
            receivedOn = "2026-10-20",
        });

        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var body = (await saved.Content.ReadFromJsonAsync<SpoolResponse>())!;
        body.Price.Should().Be(20m);
        body.PricePerKg.Should().Be(20m);
        body.Supplier.Should().Be("Filament Shop");
        body.OrderNumber.Should().Be("PO-1");
        body.ReceivedOn.Should().Be(new DateOnly(2026, 10, 20));
        (await GetAsync(client, spoolId)).Price.Should().Be(20m);

        await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { label = "Shelf B", remainingWeightGrams = 1000m });

        var cleared = await GetAsync(client, spoolId);
        cleared.Price.Should().BeNull();
        cleared.Supplier.Should().BeNull();
        cleared.ReceivedOn.Should().BeNull();
    }

    [Fact]
    public async Task Edit_NegativePrice_Returns400_AndKeepsThePrice()
    {
        var client = await ClientAsync();
        var variantId = await FilamentTrackingTestHelpers.CreateFilamentVariantAsync(client);
        var spoolId = await FilamentTrackingTestHelpers.RegisterSpoolAsync(client, variantId, 1000);
        await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { label = "x", remainingWeightGrams = 1000m, price = 20m });

        var response = await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { label = "x", remainingWeightGrams = 1000m, price = -5m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAsync(client, spoolId)).Price.Should().Be(20m);
    }

    [Fact]
    public async Task Edit_NegativeWeight_Returns400()
    {
        var client = await ClientAsync();
        var variantId = await FilamentTrackingTestHelpers.CreateFilamentVariantAsync(client);
        var spoolId = await FilamentTrackingTestHelpers.RegisterSpoolAsync(client, variantId);

        var response = await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { label = "x", remainingWeightGrams = -1m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edit_WeightAboveCapacity_Returns422AndKeepsLabel()
    {
        var client = await ClientAsync();
        var variantId = await FilamentTrackingTestHelpers.CreateFilamentVariantAsync(client);
        var spoolId = await FilamentTrackingTestHelpers.RegisterSpoolAsync(client, variantId, 1000);

        var response = await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { label = "Should not stick", remainingWeightGrams = 1500m });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var after = await GetAsync(client, spoolId);
        after.Label.Should().BeNull();
        after.RemainingWeightGrams.Should().Be(1000m);
    }
}
