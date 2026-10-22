using System.Diagnostics;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

// Test-after: a printer whose loaded filament cannot cover a job is skipped for it, and a job pinned to it waits.
public class FilamentStockDispatchTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private async Task<Guid> SetLoadedSpoolAsync(Guid printerId, decimal remainingGrams)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var spool = await db.Spools.SingleAsync(s => s.State == SpoolState.Using
            && db.FeedUnits.Any(u => u.PrinterId == printerId && u.FeedSlots.Any(f => f.Id == s.FeedSlotId)));
        spool.CorrectRemainingWeight(remainingGrams);
        await db.SaveChangesAsync();
        return spool.Id;
    }

    // Printers left idle by other tests in this class load the same filament, so they would compete for an unassigned job.
    private async Task ParkOtherPrintersAsync(params Guid[] keep)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        foreach (var printer in await db.Printers.Where(p => !keep.Contains(p.Id)).ToListAsync())
            printer.ObserveStatus("RUNNING");
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AnUnassignedJob_SkipsAPrinterWithTooLittle_AndGoesToOneWithEnough()
    {
        var client = await ClientAsync();
        var part = await PartAsync(grams: 120);
        var shortPrinter = await PrinterAsync();
        await SetLoadedSpoolAsync(shortPrinter.Id, 50);
        var fullPrinter = await PrinterAsync();
        await ParkOtherPrintersAsync(shortPrinter.Id, fullPrinter.Id);
        var run = await RunAsync(client, part.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            (await WaitForAssignmentAsync(run.Id)).PrinterId.Should().Be(fullPrinter.Id);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task APinnedJob_SaysWhatItNeedsAndWhatIsLoaded_AndGoesOnceTheSpoolIsCorrected()
    {
        var client = await ClientAsync();
        var part = await PartAsync(grams: 120);
        var printer = await PrinterAsync();
        var spoolId = await SetLoadedSpoolAsync(printer.Id, 50);
        var run = await RunAsync(client, part.Id, pinned: printer.Id);
        var job = (await JobsAsync(run.Id)).Single();

        var diagnostics = await client.GetFromJsonAsync<JobMatchingDiagnostics>($"/api/v1/print-jobs/{job.Id}/matching-diagnostics");
        var result = diagnostics!.Printers.Single(p => p.PrinterId == printer.Id);
        result.Eligible.Should().BeFalse();
        result.Checks.Single(c => c.Code == "profiles").Status.Should().Be("Passed");
        var stock = result.Checks.Single(c => c.Code == "filament-stock");
        stock.Status.Should().Be("Failed");
        stock.Required.Should().Be("132 g White PLA (120 g + 10%)");
        stock.Actual.Should().Be("50 g");
        stock.Message.Should().StartWith($"Needs 132 g White PLA (120 g + 10%) · {printer.Name} has 50 g.");
        stock.ConfigurationUrl.Should().Be($"/spools/{spoolId}");

        (await client.PutAsJsonAsync($"/api/v1/spools/{spoolId}", new { remainingWeightGrams = 500 })).EnsureSuccessStatusCode();

        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            (await WaitForAssignmentAsync(run.Id)).PrinterId.Should().Be(printer.Id);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task APartWithoutAnEstimate_IsNotHeldBack()
    {
        var client = await ClientAsync();
        var part = await PartAsync(grams: null);
        var printer = await PrinterAsync();
        await SetLoadedSpoolAsync(printer.Id, 1);
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            (await WaitForAssignmentAsync(run.Id)).PrinterId.Should().Be(printer.Id);
        }
        finally { await worker.StopAsync(default); }
    }

    private async Task<PrintJob> WaitForBlockAsync(Guid jobId)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            using var scope = Factory.Services.CreateScope();
            var job = await scope.ServiceProvider.GetRequiredService<IPrintJobRepository>().GetByIdAsync(jobId, default);
            if (job?.LastBlockReason is not null) return job;
            await Task.Delay(50);
        }

        throw new TimeoutException("The job was not blocked.");
    }

    [Fact]
    public async Task WhenTheFileTakesMoreThanThePartSaid_NothingIsSent_AndTheReasonAndGramsAreKept()
    {
        var client = await ClientAsync();
        // The part says 20 g; the file itself, read only at dispatch, takes 200 g.
        var part = await PartAsync(grams: 20,
            contents: "; filament_type = PLA\n; filament_colour = #FFFFFF\n; filament used [g] = 200\n");
        var printer = await PrinterAsync();
        await SetLoadedSpoolAsync(printer.Id, 100);
        var run = await RunAsync(client, part.Id, pinned: printer.Id);
        var jobId = (await JobsAsync(run.Id)).Single().Id;

        PrintJob job;
        using (var worker = Worker())
        {
            await worker.StartAsync(default);
            try { job = await WaitForBlockAsync(jobId); }
            finally { await worker.StopAsync(default); }
        }

        job.PrinterId.Should().BeNull();
        job.DispatchedAtUtc.Should().BeNull();
        job.LastBlockReason.Should().StartWith($"Needs 220 g White PLA (200 g + 10%) · {printer.Name} has 100 g.");
        job.Filaments.Single().EstimatedGrams.Should().Be(200);

        // The kept grams are what readiness checks now, before any slicing.
        var diagnostics = await client.GetFromJsonAsync<JobMatchingDiagnostics>($"/api/v1/print-jobs/{jobId}/matching-diagnostics");
        diagnostics!.Printers.Single(p => p.PrinterId == printer.Id).Checks.Single(c => c.Code == "filament-stock")
            .Required.Should().Be("220 g White PLA (200 g + 10%)");
    }
}
