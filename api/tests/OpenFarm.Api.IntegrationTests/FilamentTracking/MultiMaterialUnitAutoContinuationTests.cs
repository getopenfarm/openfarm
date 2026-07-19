using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>US4 Acceptance Scenarios 1–2 (FR-009, FR-010). Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class MultiMaterialUnitAutoContinuationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task DepletingSpoolWithMatchingSiblingLoaded_AutoContinuesToLowestSlotPosition()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId, r => r.FeedUnits.Any(u => u.FeedSlots.Count >= 2));
        var slot0 = feedUnits.FeedUnits[0].FeedSlots[0].Id;
        var slot1 = feedUnits.FeedUnits[0].FeedSlots[1].Id;

        var spool1 = await RegisterSpoolAsync(client, variantId, 500);
        var spool2 = await RegisterSpoolAsync(client, variantId, 500);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spool1}/load", UriKind.Relative), new { feedSlotId = slot0 });
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spool2}/load", UriKind.Relative), new { feedSlotId = slot1 });

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spool1}/usage", UriKind.Relative), new { gramsConsumed = 500m });
        var body = await response.Content.ReadFromJsonAsync<RecordSpoolUsageResponse>();

        body!.AutoContinuation.Occurred.Should().BeTrue();
        body.AutoContinuation.ContinuedSpoolId.Should().Be(spool2);
        body.State.Should().Be("Finished");

        var spoolsResponse = await client.GetAsync(new Uri("/api/v1/spools", UriKind.Relative));
        var spools = await spoolsResponse.Content.ReadFromJsonAsync<ListSpoolsResponse>();
        spools!.Spools.Should().Contain(s => s.Id == spool2 && s.State == "Using", "the continuation target never moves — it was already Using its own slot");
    }

    [Fact]
    public async Task DepletingSpoolWithNonMatchingSibling_DoesNotAutoContinue()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client, colorName: "Red", colorHex: "#FF0000");
        var otherVariantId = await CreateFilamentVariantAsync(client, colorName: "Green", colorHex: "#00FF00");
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId, r => r.FeedUnits.Any(u => u.FeedSlots.Count >= 2));
        var slot0 = feedUnits.FeedUnits[0].FeedSlots[0].Id;
        var slot1 = feedUnits.FeedUnits[0].FeedSlots[1].Id;

        var spool1 = await RegisterSpoolAsync(client, variantId, 500);
        var spool2 = await RegisterSpoolAsync(client, otherVariantId, 500);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spool1}/load", UriKind.Relative), new { feedSlotId = slot0 });
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spool2}/load", UriKind.Relative), new { feedSlotId = slot1 });

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spool1}/usage", UriKind.Relative), new { gramsConsumed = 500m });
        var body = await response.Content.ReadFromJsonAsync<RecordSpoolUsageResponse>();

        body!.AutoContinuation.Occurred.Should().BeFalse();
        body.State.Should().Be("Finished");
    }
}
