using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>
/// Test-after: GET /api/v1/spools/{id} and /history, and the entries a weight edit and a manual usage entry write.
/// Requires Docker (Testcontainers.PostgreSql) — exercised by CI.
/// </summary>
public class SpoolHistoryEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(HttpClient Client, Guid SpoolId)> NewSpoolAsync(decimal weight = 1000)
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        return (client, await RegisterSpoolAsync(client, variantId, weight));
    }

    private static Task<HttpResponseMessage> EditAsync(HttpClient client, Guid id, decimal weight, string? reason = null, string? label = null) =>
        client.PutAsJsonAsync($"/api/v1/spools/{id}", new { label, remainingWeightGrams = weight, reason });

    private static async Task<SpoolHistoryResponse> HistoryAsync(HttpClient client, Guid id, string query = "") =>
        (await client.GetFromJsonAsync<SpoolHistoryResponse>($"/api/v1/spools/{id}/history{query}"))!;

    [Fact]
    public async Task GetSpool_ReturnsThatSpool_AndAnUnknownIdIs404()
    {
        var (client, spoolId) = await NewSpoolAsync(800);

        var spool = (await client.GetFromJsonAsync<SpoolResponse>($"/api/v1/spools/{spoolId}"))!;
        var unknown = await client.GetAsync($"/api/v1/spools/{Guid.NewGuid()}");

        spool.Id.Should().Be(spoolId);
        spool.SpoolWeightGrams.Should().Be(800m);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ANewSpool_HasNoHistory_AndAnUnknownSpoolIs404()
    {
        var (client, spoolId) = await NewSpoolAsync();

        var history = await HistoryAsync(client, spoolId);
        var unknown = await client.GetAsync($"/api/v1/spools/{Guid.NewGuid()}/history");

        history.Entries.Should().BeEmpty();
        history.TotalCount.Should().Be(0);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EditingTheWeight_RecordsACorrectionWithItsReason()
    {
        var (client, spoolId) = await NewSpoolAsync();

        var response = await EditAsync(client, spoolId, 420m, "weighed on scale");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var history = await HistoryAsync(client, spoolId);
        history.TotalCount.Should().Be(1);
        var entry = history.Entries.Single();
        entry.Source.Should().Be("Correction");
        entry.ChangeGrams.Should().Be(-580m);
        entry.RemainingAfterGrams.Should().Be(420m);
        entry.Reason.Should().Be("weighed on scale");
        entry.PrintId.Should().BeNull();
    }

    [Fact]
    public async Task EditingOnlyTheLabel_RecordsNothing_EvenWithAReason()
    {
        var (client, spoolId) = await NewSpoolAsync();

        var response = await EditAsync(client, spoolId, 1000m, reason: "ignored", label: "Shelf B");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await HistoryAsync(client, spoolId)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ARaisedWeight_IsAnIncrease_AndHistoryIsNewestFirst()
    {
        var (client, spoolId) = await NewSpoolAsync();
        await EditAsync(client, spoolId, 400m);
        await EditAsync(client, spoolId, 650m);

        var entries = (await HistoryAsync(client, spoolId)).Entries;

        entries.Select(e => e.ChangeGrams).Should().Equal(250m, -600m);
        entries.Select(e => e.RemainingAfterGrams).Should().Equal(650m, 400m);
    }

    [Fact]
    public async Task AReasonOverTheLimit_Is400_AndChangesNothing()
    {
        var (client, spoolId) = await NewSpoolAsync();

        var response = await EditAsync(client, spoolId, 500m, new string('x', 201));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await HistoryAsync(client, spoolId)).TotalCount.Should().Be(0);
        (await client.GetFromJsonAsync<SpoolResponse>($"/api/v1/spools/{spoolId}"))!.RemainingWeightGrams.Should().Be(1000m);
    }

    [Fact]
    public async Task ManualUsage_IsRecordedWithTheWeightLeft_AheadOfOlderEntries()
    {
        var (client, spoolId) = await NewSpoolAsync();
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var slotId = (await WaitForFeedUnitsAsync(client, printerId)).FeedUnits[0].FeedSlots[0].Id;
        await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}/load", new { feedSlotId = slotId });
        await EditAsync(client, spoolId, 900m);

        var usage = await client.PostAsJsonAsync($"/api/v1/spools/{spoolId}/usage", new { gramsConsumed = 250m });

        usage.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = (await HistoryAsync(client, spoolId)).Entries;
        entries.Select(e => e.Source).Should().Equal("Manual", "Correction");
        entries[0].ChangeGrams.Should().Be(-250m);
        entries[0].RemainingAfterGrams.Should().Be(650m);
    }

    [Fact]
    public async Task History_Pages_AndReportsTheTotal()
    {
        var (client, spoolId) = await NewSpoolAsync();
        foreach (var weight in new[] { 900m, 800m, 700m }) await EditAsync(client, spoolId, weight);

        var first = await HistoryAsync(client, spoolId, "?pageSize=2");
        var second = await HistoryAsync(client, spoolId, "?page=2&pageSize=2");

        first.TotalCount.Should().Be(3);
        first.Entries.Select(e => e.RemainingAfterGrams).Should().Equal(700m, 800m);
        second.TotalCount.Should().Be(3);
        second.Entries.Select(e => e.RemainingAfterGrams).Should().Equal(900m);
    }
}
