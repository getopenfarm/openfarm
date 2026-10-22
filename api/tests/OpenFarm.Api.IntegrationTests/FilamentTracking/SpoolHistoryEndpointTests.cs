using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>
/// Test-after: GET /api/v1/spools/{id} and /history, the entries a weight edit and a manual usage entry write, and every spool's
/// history as a page and as a CSV file.
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

    private static async Task<SpoolHistoryResponse> AllHistoryAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<SpoolHistoryResponse>($"/api/v1/spools/history{query}"))!;

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

    [Fact]
    public async Task EverySpoolsHistory_FiltersByColorSourceAndTime_AndSaysWhichSpool()
    {
        var client = await AuthenticatedClientAsync(_client);
        var redId = await CreateFilamentVariantAsync(client, "Red", "#ff0000");
        var red = await RegisterSpoolAsync(client, redId);
        var other = await RegisterSpoolAsync(client, await CreateFilamentVariantAsync(client));
        await EditAsync(client, red, 900m, label: "Shelf A");
        (await client.PutAsJsonAsync($"/api/v1/spools/{red}/unload", new { action = "MarkFinished", reason = "Wet" })).EnsureSuccessStatusCode();
        await EditAsync(client, other, 800m);
        var later = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));

        var all = await AllHistoryAsync(client, $"?filamentVariantId={redId}");
        var writeOffs = await AllHistoryAsync(client, $"?filamentVariantId={redId}&source=WriteOff");
        var future = await AllHistoryAsync(client, $"?filamentVariantId={redId}&from={later}");
        var unknownSource = await client.GetAsync("/api/v1/spools/history?source=Nope");

        all.Entries.Select(e => e.Source).Should().Equal("WriteOff", "Correction");
        all.Entries.Should().AllSatisfy(e =>
        {
            e.SpoolId.Should().Be(red);
            e.SpoolLabel.Should().Be("Shelf A");
            e.Material.Should().Be("PLA Basic");
            e.ColorName.Should().Be("Red");
        });
        writeOffs.Entries.Should().ContainSingle().Which.ChangeGrams.Should().Be(-900m);
        future.TotalCount.Should().Be(0);
        unknownSource.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HistoryCsv_QuotesFields_AndKeepsFormulasAsText()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client, "Red, dark");
        var spoolId = await RegisterSpoolAsync(client, variantId);
        await EditAsync(client, spoolId, 900m, "=HYPERLINK(\"x\")", "Shelf \"A\"");

        var response = await client.GetAsync($"/api/v1/spools/history.csv?filamentVariantId={variantId}");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        var unknownSource = await client.GetAsync("/api/v1/spools/history.csv?source=3");

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        lines.Should().HaveCount(3).And.EndWith(string.Empty);
        lines[0].Should().Be("Recorded (UTC),Spool,Spool id,Material,Color,Change (g),Remaining (g),Source,Reason,Print,Print status");
        lines[1].Should().MatchRegex(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ,")
            .And.EndWith($",\"Shelf \"\"A\"\"\",{spoolId},PLA Basic,\"Red, dark\",-100,900,Correction,\"'=HYPERLINK(\"\"x\"\")\",,");
        unknownSource.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HistoryCsv_NamesTheFile_AndAPageOnAnotherOriginCanReadTheName()
    {
        var client = await AuthenticatedClientAsync(_client);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/spools/history.csv");
        request.Headers.Add("Origin", "http://localhost:5173");

        using var response = await client.SendAsync(request);

        response.Content.Headers.ContentDisposition!.FileName.Should().Be("spool-history.csv");
        response.Headers.GetValues("Access-Control-Expose-Headers").Should().ContainSingle(v => v.Contains("Content-Disposition"));
    }
}
