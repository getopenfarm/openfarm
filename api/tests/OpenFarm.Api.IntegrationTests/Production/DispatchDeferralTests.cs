using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

// Test-after: a printer that defers an unassigned job to a preferred idle printer is woken again when that printer takes an
// earlier job of its own. Nothing else wakes it here, as on a manual printer: these tests replay no telemetry.
public class DispatchDeferralTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private async Task SetStatusAsync(string gcodeState, params Guid[] printerIds)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        foreach (var printer in await db.Printers.Where(p => printerIds.Contains(p.Id)).ToListAsync())
            printer.ObserveStatus(gcodeState);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AJobDeferredToAPreferredPrinter_GoesToTheDeferringOne_WhenThatPrinterTakesItsPinnedJob()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var sentinel = await PrinterAsync();
        var one = await PrinterAsync(state: "RUNNING");
        var two = await PrinterAsync(state: "RUNNING");
        // Equal capability, so the lower id is preferred (ProductionDispatchMatcher.IsPreferredAmongQualifyingPrinters).
        var (preferred, deferring) = one.Id < two.Id ? (one, two) : (two, one);
        var sentinelRun = await RunAsync(client, part.Id, pinned: sentinel.Id);
        var pinnedRun = await RunAsync(client, part.Id, pinned: preferred.Id);
        var unassignedRun = await RunAsync(client, part.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            // The sentinel is the only idle printer at start-up: once it has its job, start-up has woken everything it will.
            await WaitForAssignmentAsync(sentinelRun.Id);
            await SetStatusAsync("IDLE", preferred.Id, deferring.Id);
            worker.RequestDispatchAttempt(deferring.Id);

            (await WaitForAssignmentAsync(pinnedRun.Id)).PrinterId.Should().Be(preferred.Id);
            (await WaitForAssignmentAsync(unassignedRun.Id)).PrinterId.Should().Be(deferring.Id);
        }
        finally { await worker.StopAsync(default); }
    }
}
