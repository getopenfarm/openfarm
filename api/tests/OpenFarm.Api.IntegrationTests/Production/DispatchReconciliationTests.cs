using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class DispatchReconciliationTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task Startup_RevertsInterruptedAssignment_AndWakesAlreadyIdlePrinters()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
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
                    job.PrinterId.Should().Be(printer.Id);
                    return;
                }
                await Task.Delay(50);
            }
            Assert.Fail("Idle printer was not offered the reconciled job on startup.");
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task Startup_LeavesAnUnconfirmedStartToThePrinter_ThenFailsItAndFreesThePrinter()
    {
        var client = await ClientAsync();
        var part = await MappedPartAsync(1);
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id, 2, pinned: printer.Id);
        var stuck = (await JobsAsync(run.Id)).First();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var job = await db.PrintJobs.SingleAsync(j => j.Id == stuck.Id);
            job.RecordObjectExecution(job.ObjectExecution! with { StartRequested = true });
            job.Dispatch(printer.Id);
            await db.SaveChangesAsync();
        }

        using var worker = Worker(startConfirmGrace: TimeSpan.Zero);
        await worker.StartAsync(default);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            while ((await JobsAsync(run.Id)).Single(j => j.Id == stuck.Id).LastBlockReason is null && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(50);
            (await JobsAsync(run.Id)).Single(j => j.Id == stuck.Id).Status.Should().Be(PrintJobStatus.Queued, "restart never resends or reverts it");

            await ReportIdleAsync(printer.Id);
            worker.RequestDispatchAttempt(printer.Id);

            var next = await WaitForAssignmentAsync(run.Id, excludeJobId: stuck.Id);
            next.PrinterId.Should().Be(printer.Id, "the printer is free for the next job");
            (await JobsAsync(run.Id)).Single(j => j.Id == stuck.Id).Status.Should().Be(PrintJobStatus.Failed);
        }
        finally { await worker.StopAsync(default); }
    }
}
