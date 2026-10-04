using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class DispatchReconciliationTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task Startup_RevertsInterruptedAssignment_AndWakesAlreadyIdlePrinters()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync(manual: true);
        var run = await RunAsync(client, part.Id);
        var id = (await JobsAsync(run.Id)).Single().Id;
        await AssignAsync(id, printer.Id, started: false);
        var previousDispatch = (await JobsAsync(run.Id)).Single().DispatchedAtUtc;
        using var worker = new ProductionDispatchBackgroundService(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Factory.Services.GetRequiredService<ILogger<ProductionDispatchBackgroundService>>());
        await worker.StartAsync(default);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                var job = (await JobsAsync(run.Id)).Single();
                if (job.DispatchedAtUtc is not null && job.DispatchedAtUtc > previousDispatch)
                {
                    job.Status.Should().Be(PrintJobStatus.Queued);
                    job.DispatchedPrinterId.Should().Be(printer.Id);
                    return;
                }
                await Task.Delay(50);
            }
            Assert.Fail("Idle printer was not offered the reconciled job on startup.");
        }
        finally { await worker.StopAsync(default); }
    }
}
