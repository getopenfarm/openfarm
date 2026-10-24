using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

/// <summary>A print the printer started itself is one Print row: listed with the jobs, and deducted through the shared flow.</summary>
public class ExternalPrintHistoryTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private async Task<(Guid PrintId, Guid PrinterId, Guid SpoolId)> StartExternalAsync(decimal grams)
    {
        await ClientAsync();
        var printer = await PrinterAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var slotIds = await db.FeedUnits.Where(u => u.PrinterId == printer.Id).SelectMany(u => u.FeedSlots).Select(s => s.Id).ToListAsync();
        var spool = await db.Spools.SingleAsync(s => s.FeedSlotId != null && slotIds.Contains(s.FeedSlotId.Value));

        var print = ExternalPrint.Start(printer.Id, "benchy", [new ExternalFilamentSpec(0, "PLA", "000000", grams, null)]);
        print.ObserveSlot(new FeedSlotRef(FeedUnitKindCatalog.MultiMaterialUnit, 0, 0), spool.Id);
        var repository = scope.ServiceProvider.GetRequiredService<IExternalPrintRepository>();
        await repository.AddAsync(print, default);
        await repository.SaveChangesAsync(default);
        return (print.Id, printer.Id, spool.Id);
    }

    private static async Task<List<QueueJobRowResponse>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<ListQueueJobsResponse>($"/api/v1/print-jobs?{query}&pageSize=1000"))!.Items;

    [Fact]
    public async Task Finish_DeductsTheSpoolOnce_AndTheHistoryShowsThePrint()
    {
        var (printId, printerId, spoolId) = await StartExternalAsync(20m);
        var client = await ClientAsync();

        (await ListAsync(client, "view=external&status=Active")).Should().ContainSingle(r => r.JobId == printId && r.Status == "Printing");

        await ReplayAsync(printerId, "FINISH");
        await ReplayAsync(printerId, "FINISH");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Spools.SingleAsync(s => s.Id == spoolId)).RemainingWeightGrams.Should().Be(980m);
            (await db.SpoolUsageRecords.Where(r => r.SpoolId == spoolId).ToListAsync())
                .Should().ContainSingle(r => r.Source == SpoolUsageSource.Job && r.JobId == printId && r.GramsConsumed == 20m);
        }

        var done = (await ListAsync(client, "view=external&status=Completed")).Single(r => r.JobId == printId);
        done.Source.Should().Be("External");
        done.PartName.Should().Be("benchy");
        done.PartId.Should().BeNull();
        done.EndedAtUtc.Should().NotBeNull();

        var detail = await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{printId}");
        detail!.Source.Should().Be("External");
        detail.PartRequirementId.Should().BeNull();
        detail.FilamentConsumption.Should().ContainSingle(f => f.SpoolId == spoolId && f.GramsConsumed == 20m && f.EstimatedGrams == 20m);
    }

    [Fact]
    public async Task Finish_CostsAPrinterStartedPrintLikeAQueuedOne()
    {
        var (printId, printerId, spoolId) = await StartExternalAsync(20m);
        var client = await ClientAsync();
        (await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { remainingWeightGrams = 1000m, price = 20m })).EnsureSuccessStatusCode();

        await ReplayAsync(printerId, "FINISH");

        var line = (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{printId}"))!.FilamentConsumption.Single();
        line.GramsConsumed.Should().Be(20m);
        line.PricePerKg.Should().Be(20m);
        line.PriceSource.Should().Be("Spool");
        line.Cost.Should().Be(0.4m);
    }

    [Fact]
    public async Task ExternalPrints_AreNeverInTheQueue_OrCandidatesForDispatchLookups()
    {
        var (printId, printerId, _) = await StartExternalAsync(20m);
        var client = await ClientAsync();

        (await ListAsync(client, "view=queue&status=Active")).Should().NotContain(r => r.JobId == printId);
        (await ListAsync(client, "view=backfill&status=Active")).Should().NotContain(r => r.JobId == printId);

        using var scope = Factory.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
        (await jobs.GetActiveJobByDispatchedPrinterIdAsync(printerId, default)).Should().BeNull();
        (await jobs.GetLastJobByDispatchedPrinterIdAsync(printerId, default)).Should().BeNull();
    }

    [Fact]
    public async Task Pause_AndResume_KeepThePrintOpen_ThenFailedDeductsByProgress()
    {
        var (printId, printerId, spoolId) = await StartExternalAsync(20m);
        var client = await ClientAsync();

        await ReplayAsync(printerId, "PAUSE");
        (await ListAsync(client, "view=external&status=Paused")).Should().ContainSingle(r => r.JobId == printId);
        await ReplayAsync(printerId, "RUNNING");
        (await ListAsync(client, "view=external&status=Printing")).Should().ContainSingle(r => r.JobId == printId);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Printers.SingleAsync(p => p.Id == printerId)).RecordTelemetry(null, null, 50, null, null, null, [], null, null, null);
            await db.SaveChangesAsync();
        }

        await ReplayAsync(printerId, "FAILED");

        (await ListAsync(client, "view=external&status=Failed")).Should().ContainSingle(r => r.JobId == printId);
        using var check = Factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Spools.SingleAsync(s => s.Id == spoolId))
            .RemainingWeightGrams.Should().Be(990m);
    }

    [Fact]
    public async Task DeletingThePrinter_KeepsTheHistoryRow_WithoutAPrinter()
    {
        var (printId, printerId, _) = await StartExternalAsync(20m);
        await ReplayAsync(printerId, "FINISH");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Printers\" WHERE \"Id\" = {printerId}");
        }

        using var check = Factory.Services.CreateScope();
        var verify = check.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        (await verify.Prints.SingleAsync(p => p.Id == printId)).PrinterId.Should().BeNull();
        (await verify.ExternalPrints.CountAsync(p => p.Id == printId)).Should().Be(1);
    }
}
