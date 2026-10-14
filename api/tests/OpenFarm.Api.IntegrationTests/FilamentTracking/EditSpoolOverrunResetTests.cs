using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>FR-012 via PUT /api/v1/spools/{id} — a weight correction overrides the estimate and resets the overrun flag. Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class EditSpoolOverrunResetTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Edit_CorrectedWeight_OverridesEstimateAndResetsOverrun()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId, 500);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });
        await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/usage", UriKind.Relative), new { gramsConsumed = 600m });

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}", UriKind.Relative), new { label = (string?)null, remainingWeightGrams = 150m });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        body!.RemainingWeightGrams.Should().Be(150);
        body.HasUsageOverrun.Should().BeFalse();
    }
}
