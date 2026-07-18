using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class LoadSpoolEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Load_OntoNeverConnectedPrinter_IsRejected()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Load_OntoConnectedPrintersFeedSlot_MovesSpoolToUsing()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        var printerId = await RegisterSimulatorPrinterAsync(client);

        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoadSpoolResponse>();
        body!.State.Should().Be("Using");
        body.FeedSlotId.Should().Be(slotId);
    }

    [Fact]
    public async Task Load_OntoAlreadyOccupiedSlot_Returns409()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        var firstSpoolId = await RegisterSpoolAsync(client, variantId);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{firstSpoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        var secondSpoolId = await RegisterSpoolAsync(client, variantId);
        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{secondSpoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
