using FluentAssertions;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class BackfillTierTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task Backfill_PausedJobRetainsOccupancy_CancelledRunIsNeverOffered()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var run = await RunAsync(client, part.Id, quantity: null);
        (await JobsAsync(run.Id)).Should().BeEmpty();
        var printer = await PrinterAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var job = PrintJob.Create("Test part", run.Lines.Single().Requirements.Single().Id, 1024, 1,
                AssignmentMode.Unassigned, null, part.RevisionHash, 1, []);
            job.Dispatch(printer.Id);
            job.MarkStarted();
            job.MarkPaused();
            var repo = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
            await repo.AddAsync(job, default);
            await repo.SaveChangesAsync(default);
            var candidates = await scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>().GetBackfillCandidatesAsync(default);
            candidates.Single(c => c.RunLineId == run.Lines.Single().Id).CurrentOccupancy.Should().Be(1);
            ProductionDispatchMatcher.SelectNextBackfillLine([0.4m], [("PLA", null, 0.4m)], candidates.Where(c => c.RunLineId == run.Lines.Single().Id).ToList()).Should().BeNull();
        }
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");
        using (var scope = Factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>().GetBackfillCandidatesAsync(default)).Should().NotContain(c => c.RunLineId == run.Lines.Single().Id);
    }
    [Fact]
    public async Task FiniteDemand_PrecedesLazyBackfill_AndCeilingStopsFurtherPlates()
    {
        var client = await ClientAsync();
        var part = await PartAsync(units: 4);
        var printer = await PrinterAsync();
        var finite = await RunAsync(client, part.Id, quantity: 4, pinned: printer.Id);
        var backfill = await RunAsync(client, part.Id, quantity: null);
        var response = await client.PutAsJsonAsync($"/api/v1/production-runs/{backfill.Id}/backfill-limits",
            new { lines = new[] { new { lineId = backfill.Lines.Single().Id, backfillPrinterCap = 1, backfillCumulativeUnitCeiling = 4 } } });
        response.EnsureSuccessStatusCode();
        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            var first = await WaitForAssignmentAsync(finite.Id);
            (await JobsAsync(backfill.Id)).Should().BeEmpty();
            await ReplayAsync(printer.Id, "RUNNING");
            await ReplayAsync(printer.Id, "FINISH");
            await PostAsync(client, $"/api/v1/printers/{printer.Id}/mark-ready");
            worker.RequestDispatchAttempt(printer.Id);
            var fill = await WaitForAssignmentAsync(backfill.Id);
            (await JobsAsync(backfill.Id)).Should().ContainSingle();
            await ReplayAsync(printer.Id, "RUNNING");
            await ReplayAsync(printer.Id, "FINISH");
            // Changing a Part default cannot rewrite the completed plate's cumulative output.
            var changed = await client.PutAsJsonAsync($"/api/v1/parts/{part.Id}", new { name = part.Name, tags = Array.Empty<string>(), unitsPerPrint = 1 });
            changed.EnsureSuccessStatusCode();
            await PostAsync(client, $"/api/v1/printers/{printer.Id}/mark-ready");
            worker.RequestDispatchAttempt(printer.Id);
            await Task.Delay(200);
            (await JobsAsync(backfill.Id)).Should().ContainSingle();
            using var scope = Factory.Services.CreateScope();
            var candidates = await scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>().GetBackfillCandidatesAsync(default);
            candidates.Single(c => c.RunLineId == backfill.Lines.Single().Id).CumulativeUnits.Should().Be(4);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task PinnedBackfill_IsOnlyOfferedToItsTargetPrinter()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var target = await PrinterAsync();
        var other = await PrinterAsync();
        var run = await RunAsync(client, part.Id, quantity: null, pinned: target.Id);
        using (var scope = Factory.Services.CreateScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>();
            (await query.GetBackfillCandidatesAsync(default, other.Id)).Should().NotContain(c => c.RunLineId == run.Lines.Single().Id);
            (await query.GetBackfillCandidatesAsync(default, target.Id)).Should().Contain(c => c.RunLineId == run.Lines.Single().Id);
        }
        using var worker = Worker();
        await worker.StartAsync(default);
        try { (await WaitForAssignmentAsync(run.Id)).PrinterId.Should().Be(target.Id); }
        finally { await worker.StopAsync(default); }
    }

}
