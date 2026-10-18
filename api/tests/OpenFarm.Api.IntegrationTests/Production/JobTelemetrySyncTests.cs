using FluentAssertions;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class JobTelemetrySyncTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Theory]
    [InlineData("FINISH", false, PrintJobStatus.Completed)]
    [InlineData("FAILED", false, PrintJobStatus.Failed)]
    [InlineData("FAILED", true, PrintJobStatus.Cancelled)]
    [InlineData("FINISH", true, PrintJobStatus.Cancelled)]
    public async Task Replay_IdempotentLifecycleAndStopIntent(string terminal, bool stopRequested, PrintJobStatus expected)
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id);
        var jobId = (await JobsAsync(run.Id)).Single().Id;
        await AssignAsync(jobId, printer.Id, started: false);
        foreach (var state in new[] { "RUNNING", "RUNNING", "PAUSE", "PAUSE", "RUNNING" }) await ReplayAsync(printer.Id, state);
        var startedAt = (await JobsAsync(run.Id)).Single().StartedAtUtc;
        startedAt.Should().NotBeNull();
        if (stopRequested)
        {
            using var scope = Factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
            (await repo.GetByIdAsync(jobId, default))!.RequestCancel();
            await repo.SaveChangesAsync(default);
        }
        await ReplayAsync(printer.Id, terminal);
        await ReplayAsync(printer.Id, terminal);
        var job = (await JobsAsync(run.Id)).Single();
        job.Status.Should().Be(expected);
        job.StartedAtUtc.Should().Be(startedAt);
        job.EndedAtUtc.Should().NotBeNull();
        (await PrinterStatusAsync(printer.Id)).Should().Be(PrinterStatus.AwaitingClear);
    }
}
