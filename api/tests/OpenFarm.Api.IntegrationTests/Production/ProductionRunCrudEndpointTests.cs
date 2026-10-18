using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.ProductionRuns;

namespace OpenFarm.Api.IntegrationTests.Production;

public class ProductionRunCrudEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task CreateListGet_NoPrintersRequired_WholePlatesAreEagerlyQueued()
    {
        var client = await ClientAsync();
        var part = await PartAsync(4);
        var run = await RunAsync(client, part.Id, 20);
        var jobs = await JobsAsync(run.Id);
        jobs.Should().HaveCount(5).And.OnlyContain(j => j.PrinterId == null && j.PlateQuantity == 4);
        (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single().Quantity.Should().Be(20);
        var list = await client.GetFromJsonAsync<ListProductionRunsResponse>("/api/v1/production-runs");
        list!.Items.Should().Contain(r => r.Id == run.Id && r.DisplayLabel == $"{part.Name} ×20");
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/close", HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("", "Unassigned", null)]
    [InlineData("Part,Assembly", "Unassigned", null)]
    [InlineData("99", "Unassigned", null)]
    [InlineData("Part", "99", null)]
    [InlineData("Part", "Unassigned", "99")]
    [InlineData("Part", "Pinned", null)]
    public async Task Create_InvalidLine_Returns400(string targetKind, string assignmentMode, string? revisionMode)
    {
        var client = await ClientAsync();
        await PostAsync(client, "/api/v1/production-runs", HttpStatusCode.BadRequest, new
        {
            lines = new[] { new { targetKind, targetId = Guid.NewGuid(), quantity = 1, assignmentMode, revisionMode } },
        });
    }

    [Fact]
    public async Task Create_EmptyLinesAndMissingTarget_AreRejected()
    {
        var client = await ClientAsync();
        await PostAsync(client, "/api/v1/production-runs", HttpStatusCode.BadRequest, new { lines = Array.Empty<object>() });
        await PostAsync(client, "/api/v1/production-runs", HttpStatusCode.NotFound, new
        { lines = new[] { new { targetKind = "Part", targetId = Guid.NewGuid(), quantity = 1 } } });
    }
}
