using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;
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

    // Printers left idle by other tests in this class would compete for an unassigned job: a printer defers to a preferred
    // idle one, and without telemetry nothing wakes it again.
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
}
