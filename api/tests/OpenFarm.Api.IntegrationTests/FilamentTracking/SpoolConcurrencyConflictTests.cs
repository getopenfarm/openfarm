using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class SpoolConcurrencyConflictTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task TwoNearSimultaneousUnloadCalls_OneSucceedsOneConflicts()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        var first = client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "MarkFinished" });
        var second = client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "MarkFinished" });

        var responses = await Task.WhenAll(first, second);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        statusCodes.Should().Contain(HttpStatusCode.OK);
        statusCodes.Should().Contain(code => code == HttpStatusCode.Conflict || code == (HttpStatusCode)422);
    }
}
