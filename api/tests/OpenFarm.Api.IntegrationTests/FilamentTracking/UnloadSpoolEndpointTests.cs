using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class UnloadSpoolEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Unload_WithMarkFinished_MovesToFinishedAndExcludesFromInStorageListing()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "MarkFinished" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        body!.State.Should().Be("Finished");

        var listResponse = await client.GetAsync(new Uri("/api/v1/spools?state=InStorage", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<ListSpoolsResponse>();
        list!.Spools.Should().NotContain(s => s.Id == spoolId);
    }

    [Fact]
    public async Task Unload_WithReturnToStorage_RevertsToInStorageAndRetainsRemainingWeight()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId, spoolWeightGrams: 800);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "ReturnToStorage" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        body!.State.Should().Be("InStorage");
        body.FeedSlotId.Should().BeNull();
        body.RemainingWeightGrams.Should().Be(800);
    }

    [Fact]
    public async Task Unload_WithMarkFinishedFromStorage_WritesOffWhatIsLeftWithTheReason()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId, spoolWeightGrams: 800);

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "MarkFinished", reason = "Wet" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        body!.State.Should().Be("Finished");
        body.RemainingWeightGrams.Should().Be(0);

        var history = await GetHistoryAsync(client, spoolId);
        var writeOff = history.Entries.Should().ContainSingle().Subject;
        writeOff.Source.Should().Be("WriteOff");
        writeOff.ChangeGrams.Should().Be(-800);
        writeOff.RemainingAfterGrams.Should().Be(0);
        writeOff.Reason.Should().Be("Wet");
    }

    [Fact]
    public async Task Unload_WithAReasonLongerThan200_Returns400()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "MarkFinished", reason = new string('x', 201) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Restore_PutsTheWrittenOffGramsBackInStorage()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId, spoolWeightGrams: 800);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "MarkFinished" });

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/restore", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        body!.State.Should().Be("InStorage");
        body.FeedSlotId.Should().BeNull();
        body.RemainingWeightGrams.Should().Be(800);

        var history = await GetHistoryAsync(client, spoolId);
        history.Entries.Select(e => (e.Source, e.ChangeGrams, e.RemainingAfterGrams))
            .Should().Equal(("Restore", 800m, 800m), ("WriteOff", -800m, 0m));
    }

    [Fact]
    public async Task Restore_AfterAReweigh_KeepsTheCorrectedWeight()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId, spoolWeightGrams: 800);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "MarkFinished" });
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}", UriKind.Relative), new { remainingWeightGrams = 120, reason = "weighed on scale" });

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/restore", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        body!.State.Should().Be("InStorage");
        body.RemainingWeightGrams.Should().Be(120);
    }

    [Fact]
    public async Task Restore_WhenNotFinished_Returns422()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/restore", UriKind.Relative), new { });

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Restore_UnknownSpool_Returns404()
    {
        var client = await AuthenticatedClientAsync(_client);

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{Guid.NewGuid()}/restore", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<SpoolHistoryResponse> GetHistoryAsync(HttpClient client, Guid spoolId)
    {
        var response = await client.GetAsync(new Uri($"/api/v1/spools/{spoolId}/history", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SpoolHistoryResponse>())!;
    }

    [Fact]
    public async Task Unload_WhenNotUsing_Returns409()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);

        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "ReturnToStorage" });

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }
}
