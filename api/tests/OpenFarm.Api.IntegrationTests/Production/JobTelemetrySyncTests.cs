using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Production;
using OpenFarm.Infrastructure.Persistence;

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

    [Theory]
    [InlineData(40, PrintJobStatus.Failed)]
    [InlineData(100, PrintJobStatus.Completed)]
    public async Task BackToIdleMidPrint_ClosesThePrintByItsLastProgress_AndTheBedAwaitsClear(int percent, PrintJobStatus expected)
    {
        var (runId, printerId) = await PrintingJobAsync();
        await ChangePrinterAsync(printerId, p => p.RecordTelemetry(null, null, percent, null, null, null, []));

        await ReplayAsync(printerId, "IDLE");
        await ReplayAsync(printerId, "IDLE");

        var job = (await JobsAsync(runId)).Single();
        job.Status.Should().Be(expected);
        job.StopPercent.Should().Be(percent);
        (await PrinterStatusAsync(printerId)).Should().Be(PrinterStatus.AwaitingClear, "the plate may hold the print");
    }

    [Fact]
    public async Task AnEndSeenOnlyAfterASilence_IsDatedBackToWhenItsTimeRanOut()
    {
        var (runId, printerId) = await PrintingJobAsync(startedAgo: TimeSpan.FromHours(4));
        await ChangePrinterAsync(printerId, p => p.RecordTelemetry(null, null, 90, null, 30, null, []));
        var lastHeard = DateTimeOffset.UtcNow.AddHours(-3);

        await ReplayAsync(printerId, "FINISH", unseenSinceUtc: lastHeard);

        var job = (await JobsAsync(runId)).Single();
        job.Status.Should().Be(PrintJobStatus.Completed);
        job.EndedAtUtc.Should().BeCloseTo(lastHeard.AddMinutes(30), TimeSpan.FromSeconds(1));
        job.ActualDurationMinutes.Should().Be(90);
    }

    [Fact]
    public async Task RemovingAPrinterMidPrint_FailsItsJob()
    {
        var (runId, printerId) = await PrintingJobAsync();
        var client = await ClientAsync();

        (await client.DeleteAsync($"/api/v1/printers/{printerId}")).StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        var job = (await JobsAsync(runId)).Single();
        job.Status.Should().Be(PrintJobStatus.Failed);
        job.PrinterId.Should().BeNull();
    }

    /// <summary>A job printing on a fresh printer, started <paramref name="startedAgo"/> before now.</summary>
    private async Task<(Guid RunId, Guid PrinterId)> PrintingJobAsync(TimeSpan? startedAgo = null)
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id);
        var jobId = (await JobsAsync(run.Id)).Single().Id;
        await AssignAsync(jobId, printer.Id);
        if (startedAgo is { } ago)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var job = await db.PrintJobs.SingleAsync(j => j.Id == jobId);
            db.Entry(job).Property(j => j.StartedAtUtc).CurrentValue = DateTimeOffset.UtcNow - ago;
            await db.SaveChangesAsync();
        }

        return (run.Id, printer.Id);
    }

    private async Task ChangePrinterAsync(Guid printerId, Action<Printer> change)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        change(await db.Printers.SingleAsync(p => p.Id == printerId));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AFailedPrint_KeepsThePrintersAlertsAsItsFailureCodes()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id);
        var jobId = (await JobsAsync(run.Id)).Single().Id;
        await AssignAsync(jobId, printer.Id);
        var error = new PrinterAlert("PrintError", "0300_8004", "Error", "Filament runout");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Printers.SingleAsync(p => p.Id == printer.Id)).ObserveAlerts(null, [error]);
            await db.SaveChangesAsync();
        }

        await ReplayAsync(printer.Id, "FAILED");

        var job = (await JobsAsync(run.Id)).Single();
        job.Status.Should().Be(PrintJobStatus.Failed);
        job.StopAlerts.Should().Equal(error);
    }
}
