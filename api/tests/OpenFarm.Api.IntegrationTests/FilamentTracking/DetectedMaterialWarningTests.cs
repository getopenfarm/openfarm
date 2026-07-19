using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class DetectedMaterialWarningTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Load_OntoSlotReportingMismatchedDetectedMaterial_ReturnsWarningTrueAndStillCompletes()
    {
        var client = await AuthenticatedClientAsync(_client);
        // The simulator's AMS slot 0 always reports PLA/#E53935 (SimulatedPrinterState) — pick a
        // deliberately different color so the comparison mismatches.
        var variantId = await CreateFilamentVariantAsync(client, colorName: "Blue", colorHex: "#0000FF");
        var spoolId = await RegisterSpoolAsync(client, variantId);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoadSpoolResponse>();
        body!.RfidMismatchWarning.Should().BeTrue();
        body.State.Should().Be("Using", "the load always completes regardless of the comparison's outcome");
    }
}
