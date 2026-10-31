using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

public class ObjectSkippingWorkflowTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static readonly string[] ThreeProducedUnits = ["1", "2", "3"];

    [Fact]
    public async Task CompletionFoundAfterAnOutage_DoesNotGuessObjectOutcomes()
    {
        var client = await ClientAsync();
        var part = await MappedPartAsync(6);
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id, 6);
        var source = (await JobsAsync(run.Id)).Single();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var job = await db.PrintJobs.SingleAsync(j => j.Id == source.Id);
            job.Dispatch(printer.Id);
            job.MarkStarted();
            await db.SaveChangesAsync();
        }
        await ReplayAsync(printer.Id, "FINISH", DateTimeOffset.UtcNow.AddMinutes(-5));
        var result = await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{source.Id}");
        result!.Status.Should().Be("Completed");
        result.ProducedUnits.Should().Be(0);
        (await DetailAsync(client, run.Id)).Status.Should().Be("Active");
    }

    [Fact]
    public async Task OperatorCanResolveAnUnconfirmedStart_WithoutAutomaticallyResendingIt()
    {
        var client = await ClientAsync();
        var part = await MappedPartAsync(6);
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id, 4);
        var source = (await JobsAsync(run.Id)).Single();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var job = await db.PrintJobs.SingleAsync(j => j.Id == source.Id);
            job.RecordObjectExecution(job.ObjectExecution! with { ArtifactHash = "unconfirmed", StartRequested = true });
            job.Dispatch(printer.Id);
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync($"/api/v1/print-jobs/{source.Id}/confirm-not-started", new { artifactHash = "unconfirmed" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await JobsAsync(run.Id)).Single().Status.Should().Be(PrintJobStatus.Cancelled);
        var replacement = await client.PostAsJsonAsync($"/api/v1/print-jobs/{source.Id}/requeue", new { });
        replacement.StatusCode.Should().Be(HttpStatusCode.Created);
        (await replacement.Content.ReadFromJsonAsync<PrintJobResponse>())!.TargetUnits.Should().Be(4);
    }

    [Fact]
    public async Task PartialSuccess_RequeuesOnlyMissingUnits_AndRetainsSnapshotAfterSourceRemoval()
    {
        var client = await ClientAsync();
        var part = await MappedPartAsync(6);
        var run = await RunAsync(client, part.Id, 10);
        var jobs = await JobsAsync(run.Id);
        jobs.Select(j => j.EffectiveTargetUnits).Should().Equal(6, 4);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            foreach (var source in jobs)
            {
                var job = await db.PrintJobs.SingleAsync(j => j.Id == source.Id);
                job.MarkStarted();
                var excluded = job.ObjectExecution!.PlannedExclusions;
                job.ObserveExcludedObjects(source.EffectiveTargetUnits == 4 ? excluded.Concat(["OBJECT_1"]).ToArray() : []);
                job.MarkCompleted();
            }
            await db.SaveChangesAsync();
        }
        var detail = await DetailAsync(client, run.Id);
        detail.Lines.Single().Requirements.Single().CompletedUnits.Should().Be(9);
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync($"/api/v1/print-jobs/{jobs[1].Id}/requeue", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var replacement = (await response.Content.ReadFromJsonAsync<PrintJobResponse>())!;
        replacement.TargetUnits.Should().Be(1);
        replacement.PlateQuantity.Should().Be(6);
        (await client.PostAsJsonAsync($"/api/v1/print-jobs/{jobs[1].Id}/requeue", new { })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JobsAsync(run.Id)).Should().HaveCount(3);
    }

    [Fact]
    public async Task UnverifiedCompletion_RequiresOutputConfirmationBeforeReplacement()
    {
        var client = await ClientAsync();
        var part = await MappedPartAsync(6);
        var run = await RunAsync(client, part.Id, 4);
        var source = (await JobsAsync(run.Id)).Single();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var job = await db.PrintJobs.SingleAsync(j => j.Id == source.Id);
            job.RecordObjectExecution(job.ObjectExecution! with { ArtifactHash = "test-artifact" });
            job.MarkStarted();
            job.MarkCompleted();
            await db.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync($"/api/v1/print-jobs/{source.Id}/requeue", new { })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var confirm = await client.PostAsJsonAsync($"/api/v1/print-jobs/{source.Id}/object-output", new { artifactHash = "test-artifact", producedUnitIds = ThreeProducedUnits });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        var detail = await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{source.Id}");
        detail!.ProducedUnits.Should().Be(3);
        detail.MissingUnits.Should().Be(1);
        (await client.PostAsJsonAsync($"/api/v1/print-jobs/{source.Id}/write-off", new { })).EnsureSuccessStatusCode();
        (await DetailAsync(client, run.Id)).Status.Should().Be("Complete");
    }
}
