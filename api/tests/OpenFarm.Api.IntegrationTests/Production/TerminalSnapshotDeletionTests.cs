using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class TerminalSnapshotDeletionTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private async Task<(Guid RunId, Guid JobId)> LegacyJobAsync(Part part, PrintJobStatus status, bool unlimited = false)
    {
        var printer = await PrinterAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var run = ProductionRun.Create("Legacy dependency", null, null,
            [new ProductionDemandSpec(RunLineTargetKind.Part, part.Id, unlimited ? null : 1, null, AssignmentMode.Unassigned, null, 1, null)]);
        var requirement = PartRequirement.Create(run.Lines[0].Id, part.Id, unlimited ? null : 1);
        var job = PrintJob.Create("Test part", requirement.Id, 1024, part.UnitsPerPrint,
            AssignmentMode.Pinned, printer.Id, part.RevisionHash, part.RevisionNumber, [new JobFilamentRequirementSpec("PLA", null)]);
        if (status == PrintJobStatus.Cancelled) job.MarkCancelled();
        else if (status != PrintJobStatus.Queued)
        {
            job.Dispatch(printer.Id);
            job.MarkStarted();
            if (status == PrintJobStatus.Paused) job.MarkPaused();
            if (status == PrintJobStatus.Failed) job.MarkFailed("test");
            if (status == PrintJobStatus.Completed) job.MarkCompleted();
            if (status == PrintJobStatus.Printing) job.RequestCancel();
        }
        db.ProductionRuns.Add(run);
        db.PartRequirements.Add(requirement);
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        return (run.Id, job.Id);
    }

    [Theory]
    [InlineData(PrintJobStatus.Cancelled)]
    [InlineData(PrintJobStatus.Failed)]
    [InlineData(PrintJobStatus.Completed)]
    public async Task FinishedLegacyJob_DeletionPreservesHistoryAndPermanentlyDisablesSourceActions(PrintJobStatus status)
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var (runId, jobId) = await LegacyJobAsync(part, status);
        var previewResponse = await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = new[] { part.Id } });
        var preview = (await previewResponse.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Parts.CheckPartDeletionResponse>())!.Parts[0];
        preview.Status.Should().Be("Eligible");
        preview.LegacyJobsLosingRecovery.Should().Be(1);
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{jobId}"))!;
        detail.Status.Should().Be(status.ToString());
        detail.FileRevisionHash.Should().Be(part.RevisionHash);
        detail.SnapshotState.Should().Be("Unavailable");
        detail.SnapshotRecoveryReason.Should().Contain("Source part removed");
        var file = await client.GetAsync($"/api/v1/print-jobs/{jobId}/file");
        file.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await file.Content.ReadAsStringAsync()).Should().Contain("Source part removed");
        var retry = await client.PostAsJsonAsync($"/api/v1/print-jobs/{jobId}/requeue", new { });
        retry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await retry.Content.ReadAsStringAsync()).Should().Contain("Source part removed");
        var run = await DetailAsync(client, runId);
        run.Lines.Single().TargetName.Should().Be(part.Name);
        var requirement = run.Lines.Single().Requirements.Single();
        requirement.PartName.Should().Be(part.Name);
        requirement.Jobs.Should().NotContain(j => j.CanRequeue);
        if (status is PrintJobStatus.Cancelled or PrintJobStatus.Failed)
            requirement.Jobs.Should().Contain(j => j.Id == jobId && j.NeedsAction, "only a write-off resolves it now");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var persisted = await db.PrintJobs.SingleAsync(j => j.Id == jobId);
        var unavailableAt = persisted.SnapshotUnavailableAtUtc;
        unavailableAt.Should().NotBeNull();
        (await db.PartRequirements.SingleAsync(r => r.Id == persisted.PartRequirementId)).LibraryPartId.Should().BeNull();
        await scope.ServiceProvider.GetRequiredService<ProductionSnapshotReconciler>().ReconcileAsync(default);
        persisted.GetSnapshotState().Should().Be("Unavailable");
        persisted.SnapshotUnavailableAtUtc.Should().Be(unavailableAt);
        var recover = () => persisted.CaptureProductionSnapshot(part);
        recover.Should().Throw<ProductionSnapshotUnavailableException>();
        await using var bytes = await scope.ServiceProvider.GetRequiredService<IPartFileStorage>().OpenReadAsync(part.StoragePath, default);
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(PrintJobStatus.Queued)]
    [InlineData(PrintJobStatus.Printing)]
    [InlineData(PrintJobStatus.Paused)]
    public async Task UnfinishedLegacyJob_IncludingPendingCancellation_StillProtectsSource(PrintJobStatus status)
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var (_, jobId) = await LegacyJobAsync(part, status);
        var blockedResponse = await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = new[] { part.Id } });
        (await blockedResponse.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Parts.CheckPartDeletionResponse>())!.Parts[0].Status.Should().Be("Blocked");
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var job = (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{jobId}"))!;
        job.SnapshotState.Should().Be("Unresolved");
        if (status == PrintJobStatus.Printing) job.CancelRequestedAtUtc.Should().NotBeNull();
        (await client.GetAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CancellingOneUnlimitedJob_DoesNotReleaseRunDependency()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var (runId, jobId) = await LegacyJobAsync(part, PrintJobStatus.Cancelled, unlimited: true);
        var blockedResponse = await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = new[] { part.Id } });
        (await blockedResponse.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Parts.CheckPartDeletionResponse>())!.Parts[0].Status.Should().Be("Blocked");
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await PostAsync(client, $"/api/v1/production-runs/{runId}/cancel");
        var previewResponse = await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = new[] { part.Id } });
        var preview = (await previewResponse.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Parts.CheckPartDeletionResponse>())!.Parts[0];
        preview.Status.Should().Be("Eligible");
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{jobId}"))!.SnapshotState.Should().Be("Unavailable");
    }

    [Fact]
    public async Task MixedLegacyReferences_RemainProtectedUntilEveryUnfinishedJobIsCancelled()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var (_, finishedId) = await LegacyJobAsync(part, PrintJobStatus.Cancelled);
        var (_, queuedId) = await LegacyJobAsync(part, PrintJobStatus.Queued);
        var blockedResponse = await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = new[] { part.Id } });
        (await blockedResponse.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Parts.CheckPartDeletionResponse>())!.Parts[0].Status.Should().Be("Blocked");
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{finishedId}"))!.SnapshotState.Should().Be("Unresolved");
        await PostAsync(client, $"/api/v1/print-jobs/{queuedId}/cancel");
        var previewResponse = await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = new[] { part.Id } });
        var preview = (await previewResponse.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Parts.CheckPartDeletionResponse>())!.Parts[0];
        preview.Status.Should().Be("Eligible");
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        foreach (var id in new[] { finishedId, queuedId })
            (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{id}"))!.SnapshotState.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ConcurrentRecoveryAndDeletion_ProduceEitherReadyOrPermanentlyUnavailableSnapshot()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var (_, jobId) = await LegacyJobAsync(part, PrintJobStatus.Cancelled);
        using (var scope = Factory.Services.CreateScope())
        {
            var recover = scope.ServiceProvider.GetRequiredService<ProductionSnapshotReconciler>().ReconcileAsync(default);
            var deletion = client.DeleteAsync($"/api/v1/parts/{part.Id}");
            await Task.WhenAll(recover, deletion);
            (await deletion).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        using var fresh = Factory.Services.CreateScope();
        var job = await fresh.ServiceProvider.GetRequiredService<OpenFarmDbContext>().PrintJobs.SingleAsync(j => j.Id == jobId);
        job.GetSnapshotState().Should().BeOneOf("Ready", "Unavailable");
        (job.ProductionSnapshotJson is not null && job.SnapshotUnavailableAtUtc is not null).Should().BeFalse();
        await fresh.ServiceProvider.GetRequiredService<ProductionSnapshotReconciler>().ReconcileAsync(default);
        var detail = (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{jobId}"))!;
        detail.SnapshotState.Should().Be(job.GetSnapshotState());
    }
}
