using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.ActivityLog;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.ActivityLog;

/// <summary>FR-014/SC-005 — FeedUnitAutoContinued and FeedUnitNoQualifyingReload entries appear where triggered. Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class FeedUnitAutoContinuationActivityLogTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task AutoContinuation_WritesFeedUnitAutoContinuedEntry()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId, r => r.FeedUnits.Any(u => u.FeedSlots.Count >= 2));
        var slot0 = feedUnits.FeedUnits[0].FeedSlots[0].Id;
        var slot1 = feedUnits.FeedUnits[0].FeedSlots[1].Id;
        var feedUnitId = feedUnits.FeedUnits[0].Id;

        var spool1 = await RegisterSpoolAsync(client, variantId, 400);
        var spool2 = await RegisterSpoolAsync(client, variantId, 400);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spool1}/load", UriKind.Relative), new { feedSlotId = slot0 });
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spool2}/load", UriKind.Relative), new { feedSlotId = slot1 });

        await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spool1}/usage", UriKind.Relative), new { gramsConsumed = 400m });

        var logResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=100", UriKind.Relative));
        var page = await logResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();

        page!.Items.Should().ContainSingle(e => e.EntityId == feedUnitId.ToString() && e.EntityType == "FeedUnit" && e.EventType == "FeedUnitAutoContinued");
    }

    [Fact]
    public async Task NoQualifyingSibling_WritesFeedUnitNoQualifyingReloadEntry()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slot0 = feedUnits.FeedUnits[0].FeedSlots[0].Id;
        var feedUnitId = feedUnits.FeedUnits[0].Id;

        var spool1 = await RegisterSpoolAsync(client, variantId, 400);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spool1}/load", UriKind.Relative), new { feedSlotId = slot0 });

        await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spool1}/usage", UriKind.Relative), new { gramsConsumed = 400m });

        var logResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=100", UriKind.Relative));
        var page = await logResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();

        page!.Items.Should().ContainSingle(e => e.EntityId == feedUnitId.ToString() && e.EntityType == "FeedUnit" && e.EventType == "FeedUnitNoQualifyingReload");
    }
}
