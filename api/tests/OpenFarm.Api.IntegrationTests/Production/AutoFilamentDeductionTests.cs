using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

public class AutoFilamentDeductionTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    /// <summary>What dispatch does: freezes the loaded spool and the slicer's grams onto the job's line.</summary>
    private async Task<Guid> BindLoadedSpoolAsync(Guid jobId, Guid printerId, decimal grams, FilamentUsageCurve? curve = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var slotIds = await db.FeedUnits.Where(u => u.PrinterId == printerId).SelectMany(u => u.FeedSlots).Select(s => s.Id).ToListAsync();
        var spool = await db.Spools.SingleAsync(s => s.FeedSlotId != null && slotIds.Contains(s.FeedSlotId.Value));
        var repository = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
        (await repository.GetByIdAsync(jobId, default))!.BindFilamentUsage([(spool.Id, grams, curve)]);
        await repository.SaveChangesAsync(default);
        return spool.Id;
    }

    private async Task SetProgressAsync(Guid printerId, int? percent, int? layer, int? totalLayers)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        (await db.Printers.SingleAsync(p => p.Id == printerId)).RecordTelemetry(null, null, percent, null, null, null, [], null, layer, totalLayers);
        await db.SaveChangesAsync();
    }

    private async Task<(decimal Remaining, List<SpoolUsageRecord> Records)> SpoolAsync(Guid spoolId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        return ((await db.Spools.SingleAsync(s => s.Id == spoolId)).RemainingWeightGrams,
            await db.SpoolUsageRecords.Where(r => r.SpoolId == spoolId).ToListAsync());
    }

    private async Task<(Guid JobId, Guid PrinterId, Guid SpoolId)> PrintingJobAsync(decimal grams, FilamentUsageCurve? curve = null)
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id);
        var job = (await JobsAsync(run.Id)).Single();
        await AssignAsync(job.Id, printer.Id);
        return (job.Id, printer.Id, await BindLoadedSpoolAsync(job.Id, printer.Id, grams, curve));
    }

    [Fact]
    public async Task Finish_DeductsTheFullEstimate_OnceAndLedgersIt()
    {
        var (jobId, printerId, spoolId) = await PrintingJobAsync(20m);

        await ReplayAsync(printerId, "FINISH");
        await ReplayAsync(printerId, "FINISH");

        var (remaining, records) = await SpoolAsync(spoolId);
        remaining.Should().Be(980m);
        records.Should().ContainSingle(r => r.Source == SpoolUsageSource.Job && r.JobId == jobId && r.GramsConsumed == 20m);
    }

    [Fact]
    public async Task Finish_ShowsOnTheSpoolHistory_WithThePrintAndWhatIsLeft()
    {
        var (jobId, printerId, spoolId) = await PrintingJobAsync(20m);

        await ReplayAsync(printerId, "FINISH");

        var client = await ClientAsync();
        var history = (await client.GetFromJsonAsync<SpoolHistoryResponse>($"/api/v1/spools/{spoolId}/history"))!;
        var entry = history.Entries.Should().ContainSingle().Subject;
        entry.Source.Should().Be("Job");
        entry.PrintId.Should().Be(jobId);
        entry.PrintName.Should().NotBeNullOrEmpty();
        entry.PrintStatus.Should().Be("Completed");
        entry.ChangeGrams.Should().Be(-20m);
        entry.RemainingAfterGrams.Should().Be(980m);
    }

    [Fact]
    public async Task Failed_DeductsByLastObservedLayer()
    {
        var (_, printerId, spoolId) = await PrintingJobAsync(20m);
        await SetProgressAsync(printerId, percent: 90, layer: 30, totalLayers: 100);

        await ReplayAsync(printerId, "FAILED");

        (await SpoolAsync(spoolId)).Remaining.Should().Be(994m);
    }

    [Fact]
    public async Task Failed_UsesTheColoursOwnCurve_NotAnEvenSpreadOverLayers()
    {
        // This colour only starts printing in layer 3 of 4; an even spread would wrongly deduct 75% of it.
        var (_, printerId, spoolId) = await PrintingJobAsync(20m, FilamentUsageCurve.Create([[0, 0, 5, 10]]));
        await SetProgressAsync(printerId, percent: 75, layer: 3, totalLayers: 4);

        await ReplayAsync(printerId, "FAILED");

        (await SpoolAsync(spoolId)).Remaining.Should().Be(995m);
    }

    [Fact]
    public async Task Failed_WithoutLayerTelemetry_FallsBackToProgressPercent()
    {
        var (_, printerId, spoolId) = await PrintingJobAsync(20m);
        await SetProgressAsync(printerId, percent: 50, layer: null, totalLayers: null);

        await ReplayAsync(printerId, "FAILED");

        (await SpoolAsync(spoolId)).Remaining.Should().Be(990m);
    }

    [Fact]
    public async Task Failed_WithNoProgressAtAll_DeductsNothingAndLogsSkip()
    {
        var (jobId, printerId, spoolId) = await PrintingJobAsync(20m);

        await ReplayAsync(printerId, "FAILED");

        (await SpoolAsync(spoolId)).Remaining.Should().Be(1000m);
        var client = await ClientAsync();
        (await ActivityEventTypesAsync(client, jobId)).Should().Contain("PrintJobFilamentDeductionSkipped");
    }
}
