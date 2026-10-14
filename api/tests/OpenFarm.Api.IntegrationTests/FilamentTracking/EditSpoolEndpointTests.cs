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
