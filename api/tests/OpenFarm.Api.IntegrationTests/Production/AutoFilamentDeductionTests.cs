using System.Globalization;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Api.Endpoints.Reports;
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

    private static async Task<JobFilamentConsumptionResponse> LineAsync(HttpClient client, Guid jobId) =>
        (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{jobId}"))!.FilamentConsumption.Single();

    [Fact]
    public async Task Finish_CostsTheLineAtThePricePaidForTheSpool_AndTheCostSticksWhenThePriceChanges()
    {
        var (jobId, printerId, spoolId) = await PrintingJobAsync(20m);
        var client = await ClientAsync();
        (await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { remainingWeightGrams = 1000m, price = 20m })).EnsureSuccessStatusCode();

        await ReplayAsync(printerId, "FINISH");

        var line = await LineAsync(client, jobId);
        line.GramsConsumed.Should().Be(20m);
        line.PricePerKg.Should().Be(20m);
        line.PriceSource.Should().Be("Spool");
        line.Cost.Should().Be(0.4m);

        // The spool is repriced afterwards; what the print cost does not move.
        (await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { remainingWeightGrams = 980m, price = 99m })).EnsureSuccessStatusCode();
        var after = await LineAsync(client, jobId);
        after.PricePerKg.Should().Be(20m);
        after.Cost.Should().Be(0.4m);
    }

    [Fact]
    public async Task Finish_OfASpoolWithoutAPrice_HasNoCost()
    {
        var (jobId, printerId, _) = await PrintingJobAsync(20m);

        await ReplayAsync(printerId, "FINISH");

        var line = await LineAsync(await ClientAsync(), jobId);
        line.GramsConsumed.Should().Be(20m);
        line.PricePerKg.Should().BeNull();
        line.PriceSource.Should().BeNull();
        line.Cost.Should().BeNull();
    }

    [Fact]
    public async Task Failed_CostsOnlyWhatWasDeducted()
    {
        var (jobId, printerId, spoolId) = await PrintingJobAsync(20m);
        var client = await ClientAsync();
        (await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { remainingWeightGrams = 1000m, price = 25m })).EnsureSuccessStatusCode();
        await SetProgressAsync(printerId, percent: 50, layer: null, totalLayers: null);

        await ReplayAsync(printerId, "FAILED");

        var line = await LineAsync(client, jobId);
        line.GramsConsumed.Should().Be(10m);
        line.Cost.Should().Be(0.25m);
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

    [Fact]
    public async Task MaterialsReport_CountsFinishedAsUsed_FailedAsWaste_AndWriteOffs_PerColour()
    {
        var range = $"?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"))}";
        var client = await ClientAsync();
        var (_, finishedPrinter, finishedSpool) = await PrintingJobAsync(20m);
        (await client.PutAsJsonAsync($"/api/v1/spools/{finishedSpool}", new { remainingWeightGrams = 1000m, price = 20m })).EnsureSuccessStatusCode();
        await ReplayAsync(finishedPrinter, "FINISH");
        (await client.PutAsJsonAsync($"/api/v1/spools/{finishedSpool}/unload", new { action = "MarkFinished" })).EnsureSuccessStatusCode();
        var (_, failedPrinter, failedSpool) = await PrintingJobAsync(20m);
        await SetProgressAsync(failedPrinter, percent: 50, layer: null, totalLayers: null);
        await ReplayAsync(failedPrinter, "FAILED");

        var report = (await client.GetFromJsonAsync<MaterialsReportResponse>($"/api/v1/reports/materials{range}"))!;
        var csv = await client.GetStringAsync($"/api/v1/reports/materials.csv{range}");

        var spoolsVariants = await SpoolVariantsAsync(finishedSpool, failedSpool);
        report.Rows.Select(r => r.FilamentVariantId).Should().Equal(spoolsVariants);
        report.Rows[0].Should().BeEquivalentTo(new { UsedGrams = 20m, WasteGrams = 0m, WrittenOffGrams = 980m, Cost = 0.4m, UnpricedGrams = 0m, PrintCount = 1 });
        report.Rows[1].Should().BeEquivalentTo(new { UsedGrams = 0m, WasteGrams = 10m, WasteShare = 1m, Cost = 0m, UnpricedGrams = 10m, PrintCount = 1 });
        report.Total.Should().BeEquivalentTo(new { UsedGrams = 20m, WasteGrams = 10m, WrittenOffGrams = 980m, Cost = 0.4m, PrintCount = 2 });
        var total = csv.Split("\r\n")[^2].Split(',');
        total[1].Should().Be("Total");
        total[3..].Select(field => decimal.Parse(field, CultureInfo.InvariantCulture)).Should().Equal(20m, 10m, 33.3m, 980m, 0.4m, 10m, 2m, 0m);
    }

    private async Task<Guid?[]> SpoolVariantsAsync(params Guid[] spoolIds)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var variants = await db.Spools.Where(s => spoolIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FilamentVariantId);
        return spoolIds.Select(id => (Guid?)variants[id]).ToArray();
    }
}
