using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class DetectedMaterialWarningTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // The simulator's AMS slot 0 always reports PLA, #E53935, sub-brand "Generic" (SimulatedPrinterState). The sub-brand is
    // the product line, compared with the filament's name; the colour may drift a little and still be the same.
    [Theory]
    [InlineData("Generic PLA", "#E33B37", false)]
    [InlineData("Generic PLA", "#0000FF", true)]
    [InlineData("PLA Matte", "#E53935", true)]
    public async Task Load_WarnsOnlyWhenTheDetectedFilamentDiffers_AndAlwaysCompletes(string material, string colorHex, bool warns)
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client, "Red", colorHex, material);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoadSpoolResponse>();
        body!.RfidMismatchWarning.Should().Be(warns);
        body.State.Should().Be("Using", "the load always completes regardless of the comparison's outcome");
    }
}
