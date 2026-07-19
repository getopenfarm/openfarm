using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.FeedUnits;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>US4 Acceptance Scenarios 3–4 (FR-011), spec.md's Edge Case. Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class AutomaticFilamentReloaderTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task TwoLoadedSpoolsWithDifferentMaterial_AutoContinuesRegardlessAndFlagsMismatch()
    {
        var client = await AuthenticatedClientAsync(_client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        await WaitForFeedUnitsAsync(client, printerId);

        var afrResponse = await client.PostAsJsonAsync(new Uri($"/api/v1/printers/{printerId}/feed-units", UriKind.Relative), new { kind = "AutomaticFilamentReloader", name = $"Splicer {Guid.NewGuid():N}", slotCount = 2 });
        var afr = await afrResponse.Content.ReadFromJsonAsync<AddFeedUnitResponse>();

        var variantA = await CreateFilamentVariantAsync(client, colorName: "A", colorHex: "#111111");
        var variantB = await CreateFilamentVariantAsync(client, colorName: "B", colorHex: "#222222");
        var spoolA = await RegisterSpoolAsync(client, variantA, 200);
        var spoolB = await RegisterSpoolAsync(client, variantB, 200);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolA}/load", UriKind.Relative), new { feedSlotId = afr!.FeedSlots[0].Id });
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolB}/load", UriKind.Relative), new { feedSlotId = afr.FeedSlots[1].Id });

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spoolA}/usage", UriKind.Relative), new { gramsConsumed = 200m });
        var body = await response.Content.ReadFromJsonAsync<RecordSpoolUsageResponse>();

        body!.AutoContinuation.Occurred.Should().BeTrue();
        body.AutoContinuation.ContinuedSpoolId.Should().Be(spoolB);
        body.AutoContinuation.MaterialMismatch.Should().BeTrue();
    }

    [Fact]
    public async Task NoSecondSpoolLoaded_DoesNotAutoContinue()
    {
        var client = await AuthenticatedClientAsync(_client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        await WaitForFeedUnitsAsync(client, printerId);

        var afrResponse = await client.PostAsJsonAsync(new Uri($"/api/v1/printers/{printerId}/feed-units", UriKind.Relative), new { kind = "AutomaticFilamentReloader", name = $"Splicer {Guid.NewGuid():N}", slotCount = 2 });
        var afr = await afrResponse.Content.ReadFromJsonAsync<AddFeedUnitResponse>();

        var variantA = await CreateFilamentVariantAsync(client);
        var spoolA = await RegisterSpoolAsync(client, variantA, 200);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolA}/load", UriKind.Relative), new { feedSlotId = afr!.FeedSlots[0].Id });

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spoolA}/usage", UriKind.Relative), new { gramsConsumed = 200m });
        var body = await response.Content.ReadFromJsonAsync<RecordSpoolUsageResponse>();

        body!.AutoContinuation.Occurred.Should().BeFalse();
    }
}
