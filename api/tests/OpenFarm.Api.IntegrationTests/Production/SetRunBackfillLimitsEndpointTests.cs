using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.ProductionRuns;

namespace OpenFarm.Api.IntegrationTests.Production;

// Test-after: PUT /api/v1/production-runs/{id}/backfill-limits — every unlimited line's limits in one save.
public class SetRunBackfillLimitsEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static async Task<ProductionRunDetailResponse> TwoLineRunAsync(HttpClient client, Guid firstPart, Guid secondPart, int? secondQuantity)
    {
        var response = await client.PostAsJsonAsync("/api/v1/production-runs", new
        {
            name = (string?)null,
            lines = new object[]
            {
                new { targetKind = "Part", targetId = firstPart, quantity = (int?)null, assignmentMode = "Unassigned" },
                new { targetKind = "Part", targetId = secondPart, quantity = secondQuantity, assignmentMode = "Unassigned" },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProductionRunDetailResponse>())!;
    }

    [Fact]
    public async Task Set_UpdatesEveryLine_AndLogsTheRun()
    {
        var client = await ClientAsync();
        var run = await TwoLineRunAsync(client, (await PartAsync()).Id, (await PartAsync()).Id, null);
        var (first, second) = (run.Lines[0].Id, run.Lines[1].Id);

        var response = await client.PutAsJsonAsync($"/api/v1/production-runs/{run.Id}/backfill-limits", new
        {
            lines = new[]
            {
                new { lineId = first, backfillPrinterCap = 2, backfillCumulativeUnitCeiling = (int?)10 },
                new { lineId = second, backfillPrinterCap = 3, backfillCumulativeUnitCeiling = (int?)null },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var detail = await DetailAsync(client, run.Id);
        detail.Lines.Single(l => l.Id == first).Should().Match<RunLineSummaryResponse>(l => l.BackfillPrinterCap == 2 && l.BackfillCumulativeUnitCeiling == 10);
        detail.Lines.Single(l => l.Id == second).Should().Match<RunLineSummaryResponse>(l => l.BackfillPrinterCap == 3 && l.BackfillCumulativeUnitCeiling == null);
        (await ActivityEventTypesAsync(client, run.Id)).Should().Contain("ProductionRunBackfillLimitsChanged");
    }

    [Fact]
    public async Task Set_ZeroCap_Returns400()
    {
        var client = await ClientAsync();
        var run = await RunAsync(client, (await PartAsync()).Id, quantity: null);

        var response = await client.PutAsJsonAsync($"/api/v1/production-runs/{run.Id}/backfill-limits", new
        {
            lines = new[] { new { lineId = run.Lines.Single().Id, backfillPrinterCap = 0 } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Set_IncludingAFiniteLine_Returns409AndPersistsNothing()
    {
        var client = await ClientAsync();
        var run = await TwoLineRunAsync(client, (await PartAsync()).Id, (await PartAsync()).Id, 4);
        var unlimited = run.Lines.Single(l => l.Quantity is null);
        var finite = run.Lines.Single(l => l.Quantity is not null);

        var response = await client.PutAsJsonAsync($"/api/v1/production-runs/{run.Id}/backfill-limits", new
        {
            lines = new[]
            {
                new { lineId = unlimited.Id, backfillPrinterCap = 5 },
                new { lineId = finite.Id, backfillPrinterCap = 5 },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await DetailAsync(client, run.Id)).Lines.Single(l => l.Id == unlimited.Id).BackfillPrinterCap.Should().Be(unlimited.BackfillPrinterCap);
    }

    [Fact]
    public async Task Set_UnknownLine_Returns404()
    {
        var client = await ClientAsync();
        var run = await RunAsync(client, (await PartAsync()).Id, quantity: null);

        var response = await client.PutAsJsonAsync($"/api/v1/production-runs/{run.Id}/backfill-limits", new
        {
            lines = new[] { new { lineId = Guid.NewGuid(), backfillPrinterCap = 1 } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
