using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.FeedUnits;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>FR-022, FR-024, FR-025, FR-026. Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class RevealExternalSpoolTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Reveal_IsIdempotent_AndReHide_SafetyNetKeepsALoadedSpoolVisible()
    {
        var client = await AuthenticatedClientAsync(_client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        await WaitForFeedUnitsAsync(client, printerId);

        var revealResponse1 = await client.PostAsJsonAsync(new Uri($"/api/v1/printers/{printerId}/feed-units", UriKind.Relative), new { kind = "ExternalSpool" });
        revealResponse1.StatusCode.Should().Be(HttpStatusCode.OK);
        var revealed1 = await revealResponse1.Content.ReadFromJsonAsync<AddFeedUnitResponse>();
        revealed1!.IsRevealed.Should().BeTrue();

        // Idempotent — revealing again doesn't error.
        var revealResponse2 = await client.PostAsJsonAsync(new Uri($"/api/v1/printers/{printerId}/feed-units", UriKind.Relative), new { kind = "ExternalSpool" });
        revealResponse2.StatusCode.Should().Be(HttpStatusCode.OK);

        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = revealed1.FeedSlots[0].Id });

        var deleteResponse = await client.DeleteAsync(new Uri($"/api/v1/feed-units/{revealed1.Id}", UriKind.Relative));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var feedUnitsResponse = await client.GetAsync(new Uri($"/api/v1/printers/{printerId}/feed-units", UriKind.Relative));
        var body = await feedUnitsResponse.Content.ReadFromJsonAsync<ListFeedUnitsResponse>();
        body!.FeedUnits.Should().Contain(u => u.Kind == "ExternalSpool" && !u.IsRevealed, "FR-025's safety net keeps it visible while a spool is still Using it");
    }

    [Fact]
    public async Task Reveal_OnAPrinterThatNeverReportedAnExternalSpool_Returns404()
    {
        var client = await AuthenticatedClientAsync(_client);

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/printers/{Guid.NewGuid()}/feed-units", UriKind.Relative), new { kind = "ExternalSpool" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
