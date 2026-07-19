using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class RecordSpoolUsageEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(Guid SpoolId, Guid SlotId, HttpClient Client)> LoadedSpoolAsync(decimal spoolWeightGrams = 1000)
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId, spoolWeightGrams);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        return (spoolId, slotId, client);
    }

    [Fact]
    public async Task RecordUsage_AgainstUsingSpool_DecrementsRemainingWeight()
    {
        var (spoolId, _, client) = await LoadedSpoolAsync(1000);

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/usage", UriKind.Relative), new { gramsConsumed = 250m });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var spoolsResponse = await client.GetAsync(new Uri("/api/v1/spools", UriKind.Relative));
        var spools = await spoolsResponse.Content.ReadFromJsonAsync<ListSpoolsResponse>();
        spools!.Spools.Should().ContainSingle(s => s.Id == spoolId && s.RemainingWeightGrams == 750);
    }

    [Fact]
    public async Task RecordUsage_CumulativeExceedingRemaining_FloorsAtZeroAndFlagsOverrun()
    {
        var (spoolId, _, client) = await LoadedSpoolAsync(500);

        await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/usage", UriKind.Relative), new { gramsConsumed = 400m });
        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/usage", UriKind.Relative), new { gramsConsumed = 300m });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RecordSpoolUsageResponse>();
        body!.RemainingWeightGrams.Should().Be(0);
        body.HasUsageOverrun.Should().BeTrue();
    }

    [Fact]
    public async Task RecordUsage_AgainstNonUsingSpool_Returns422()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/usage", UriKind.Relative), new { gramsConsumed = 10m });

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }
}
