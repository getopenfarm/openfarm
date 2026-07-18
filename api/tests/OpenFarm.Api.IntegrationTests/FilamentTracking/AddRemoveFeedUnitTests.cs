using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.FeedUnits;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>FR-005c, spec.md's Edge Case. Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class AddRemoveFeedUnitTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task AddAutomaticFilamentReloader_CreatesUnitWithSlots_AndRemoveDeactivatesWithoutDisturbingLoadedSpool()
    {
        var client = await AuthenticatedClientAsync(_client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        await WaitForFeedUnitsAsync(client, printerId);

        var addResponse = await client.PostAsJsonAsync(new Uri($"/api/v1/printers/{printerId}/feed-units", UriKind.Relative), new { kind = "AutomaticFilamentReloader", name = $"Splicer {Guid.NewGuid():N}", slotCount = 2 });
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var afr = await addResponse.Content.ReadFromJsonAsync<AddFeedUnitResponse>();
        afr!.FeedSlots.Should().HaveCount(2);

        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = afr.FeedSlots[0].Id });

        var deleteResponse = await client.DeleteAsync(new Uri($"/api/v1/feed-units/{afr.Id}", UriKind.Relative));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var spoolsResponse = await client.GetAsync(new Uri("/api/v1/spools", UriKind.Relative));
        var spools = await spoolsResponse.Content.ReadFromJsonAsync<ListSpoolsResponse>();
        spools!.Spools.Should().Contain(s => s.Id == spoolId && s.State == "Using", "the spool's own state/history stays undisturbed");
    }

    [Fact]
    public async Task RemoveFeedUnit_AgainstMultiMaterialUnit_Returns422()
    {
        var client = await AuthenticatedClientAsync(_client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var mmuId = feedUnits.FeedUnits[0].Id;

        var response = await client.DeleteAsync(new Uri($"/api/v1/feed-units/{mmuId}", UriKind.Relative));

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }
}
