using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class UnloadSpoolEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Unload_WithMarkFinished_MovesToFinishedAndExcludesFromInStorageListing()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "MarkFinished" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        body!.State.Should().Be("Finished");

        var listResponse = await client.GetAsync(new Uri("/api/v1/spools?state=InStorage", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<ListSpoolsResponse>();
        list!.Spools.Should().NotContain(s => s.Id == spoolId);
    }

    [Fact]
    public async Task Unload_WithReturnToStorage_RevertsToInStorageAndRetainsRemainingWeight()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId, spoolWeightGrams: 800);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "ReturnToStorage" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        body!.State.Should().Be("InStorage");
        body.FeedSlotId.Should().BeNull();
        body.RemainingWeightGrams.Should().Be(800);
    }

    [Fact]
    public async Task Unload_WhenNotUsing_Returns409()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "ReturnToStorage" });

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }
}
