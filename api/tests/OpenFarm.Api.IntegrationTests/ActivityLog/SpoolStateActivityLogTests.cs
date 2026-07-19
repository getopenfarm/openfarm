using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.ActivityLog;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.ActivityLog;

/// <summary>FR-014/SC-005 — every spool state transition (register, load, unload) is retrievable in the activity log. Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class SpoolStateActivityLogTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task RegisterLoadAndUnloadASpool_EachWritesAnActivityLogEntry()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "MarkFinished" });

        var logResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=100", UriKind.Relative));
        var page = await logResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();

        page!.Items.Should().ContainSingle(e => e.EntityId == spoolId.ToString() && e.EntityType == "Spool" && e.EventType == "SpoolRegistered");
        page.Items.Should().ContainSingle(e => e.EntityId == spoolId.ToString() && e.EntityType == "Spool" && e.EventType == "SpoolLoaded");
        page.Items.Should().ContainSingle(e => e.EntityId == spoolId.ToString() && e.EntityType == "Spool" && e.EventType == "SpoolFinished");
    }
}
