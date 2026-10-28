using FluentAssertions;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

public class ProductionRunQueueActivityLogTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task RunAndJobActions_RecordActorAndTimestampIncludingObservedTransitions()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var run = await RunAsync(client, part.Id);
        var printer = await PrinterAsync();
        var job = (await JobsAsync(run.Id)).Single();
        await AssignAsync(job.Id, printer.Id, started: false);
        await ReplayAsync(printer.Id, "RUNNING");
        await ReplayAsync(printer.Id, "FINISH");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var entries = await db.ActivityLogEntries.Where(e => e.EntityId == run.Id.ToString() || e.EntityId == job.Id.ToString()).ToListAsync();
        entries.Should().Contain(e => e.EventType == "ProductionRunCreated" && e.ActorType == ActorType.Operator && e.ActorId != null);
        entries.Should().Contain(e => e.EventType == "PrintJobStatusObserved" && e.ActorType == ActorType.System);
        entries.Should().Contain(e => e.EventType == "ProductionRunCompleted" && e.ActorType == ActorType.System);
        entries.Should().OnlyContain(e => e.TimestampUtc != default);
    }
    [Fact]
    public async Task QueueControlsAndBulkCancellation_RecordOperatorAndSystemActors()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id);
        var job = (await JobsAsync(run.Id)).Single();
        await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/reorder", body: new { afterJobId = (Guid?)null });
        await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/pin", body: new { printerId = printer.Id });
        await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/unpin");
        await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/pin", body: new { printerId = printer.Id });
        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            await WaitForAssignmentAsync(run.Id);
            await ReplayAsync(printer.Id, "RUNNING");
            await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/pause", expected: System.Net.HttpStatusCode.Accepted);
            await ReplayAsync(printer.Id, "PAUSE");
            await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/resume", expected: System.Net.HttpStatusCode.Accepted);
            await ReplayAsync(printer.Id, "RUNNING");
            await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/cancel", expected: System.Net.HttpStatusCode.Accepted);
            await ReplayAsync(printer.Id, "FAILED");
            await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/requeue", expected: System.Net.HttpStatusCode.Created);
            await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var entries = await db.ActivityLogEntries.ToListAsync();
            foreach (var type in new[] { "PrintJobReordered", "PrintJobPinned", "PrintJobUnpinned", "PrintJobPauseRequested", "PrintJobResumeRequested", "PrintJobCancelRequested", "PrintJobRequeued", "ProductionRunCancelled", "PrintJobCancelled" })
                entries.Should().Contain(e => e.EventType == type && e.ActorType == ActorType.Operator && e.ActorId != null && e.TimestampUtc != default);
            entries.Should().Contain(e => e.EntityId == job.Id.ToString() && e.EventType == "PrintJobDispatched" && e.ActorType == ActorType.System);
        }
        finally { await worker.StopAsync(default); }
    }

}
