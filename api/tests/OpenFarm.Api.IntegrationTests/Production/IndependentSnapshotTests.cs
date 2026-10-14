using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class IndependentSnapshotTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task FiniteJob_AfterLibraryDeletion_CanDownloadRetrySearchAndDispatch()
    {
        var client = await ClientAsync();
        var part = await PartAsync(units: 3);
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id, quantity: 4, pinned: printer.Id);
        var original = (await JobsAsync(run.Id)).First();
        var previewResponse = await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = new[] { part.Id } });
        var eligibility = (await previewResponse.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Parts.CheckPartDeletionResponse>())!.Parts[0];
        eligibility.Status.Should().Be("Eligible");
        eligibility.LegacyJobsLosingRecovery.Should().Be(0);
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Fresh request scopes prove all inputs survived persistence and catalog deletion.
        (await client.GetStringAsync($"/api/v1/print-jobs/{original.Id}/file")).Should().Be("; revision one\n");
        var matching = await client.GetFromJsonAsync<JobMatchingDiagnostics>($"/api/v1/print-jobs/{original.Id}/matching-diagnostics");
        matching!.Printers.Single(p => p.PrinterId == printer.Id).Eligible.Should().BeTrue();
        matching.JobChecks.Should().NotContain(c => c.ConfigurationUrl == $"/parts/{part.Id}");
        var detail = await DetailAsync(client, run.Id);
        detail.Lines.Single().TargetName.Should().Be(part.Name);
        detail.Lines.Single().Requirements.Single().PartName.Should().Be(part.Name);
        detail.Lines.Single().Requirements.Single().SourcePartAvailable.Should().BeFalse();
        var search = await client.GetAsync($"/api/v1/print-jobs?search={Uri.EscapeDataString(part.Name)}");
        search.EnsureSuccessStatusCode();
        (await search.Content.ReadAsStringAsync()).Should().Contain(part.Name);
        await PostAsync(client, $"/api/v1/print-jobs/{original.Id}/cancel");
        var requeue = await client.PostAsJsonAsync($"/api/v1/print-jobs/{original.Id}/requeue", new { });
        requeue.EnsureSuccessStatusCode();
        var retry = (await requeue.Content.ReadFromJsonAsync<PrintJobResponse>())!;
        retry.PlateQuantity.Should().Be(3);
        retry.FileRevisionHash.Should().Be(original.FileRevisionHash);
        retry.SnapshotState.Should().Be("Ready");
        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            var dispatched = await WaitForAssignmentAsync(run.Id);
            dispatched.GetProductionPart()!.Name.Should().Be(part.Name);
            (await client.GetStringAsync($"/api/v1/print-jobs/{dispatched.Id}/file")).Should().Be("; revision one\n");
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task UnlimitedRun_BlocksRemovalUntilCancelled_AndDispatchedJobRetainsFile()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id, quantity: null, name: "Unlimited dependency", pinned: printer.Id);
        var blocked = await client.DeleteAsync($"/api/v1/parts/{part.Id}");
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("Unlimited dependency");
        using var worker = Worker();
        await worker.StartAsync(default);
        PrintJob assigned;
        try
        {
            assigned = await WaitForAssignmentAsync(run.Id);
            await ReplayAsync(printer.Id, "RUNNING");
        }
        finally { await worker.StopAsync(default); }
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await JobsAsync(run.Id)).Single().Status.Should().Be(PrintJobStatus.Printing);
        (await client.GetStringAsync($"/api/v1/print-jobs/{assigned.Id}/file")).Should().Be("; revision one\n");
    }

    [Fact]
    public async Task LegacyReconciliation_RecoversOnlyUnchangedVerifiedInputs_AndIsIdempotent()
    {
        var client = await ClientAsync();
        var verified = await PartAsync();
        var changed = await PartAsync();
        var missing = await PartAsync(contents: "; unique missing revision\n");
        var ids = new List<Guid>();
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            foreach (var part in new[] { verified, changed, missing })
            {
                var run = ProductionRun.Create(part.OrganizationId, "Legacy", null, null, null,
                    [new RunLineSpec(RunLineTargetKind.Part, part.Id, 1, null, AssignmentMode.Unassigned, null, 1, null)]);
                var requirement = PartRequirement.Create(part.OrganizationId, run.Lines.Single().Id, part.Id, 1);
                var job = PrintJob.Create(part.OrganizationId, requirement.Id, 1024, 1, AssignmentMode.Unassigned,
                    null, part.RevisionHash, part.RevisionNumber, [new JobFilamentRequirementSpec("PLA", null)]);
                db.ProductionRuns.Add(run);
                db.PartRequirements.Add(requirement);
                db.PrintJobs.Add(job);
                ids.Add(job.Id);
            }
            await db.SaveChangesAsync();
            (await db.Parts.SingleAsync(p => p.Id == changed.Id)).SetUnitsPerPrint(7);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPartFileStorage>().DeleteAsync(missing.StoragePath, default);

        }
        using (var scope = Factory.CreateTenantScope())
        {
            var reconciler = scope.ServiceProvider.GetRequiredService<ProductionSnapshotReconciler>();
            await reconciler.ReconcileAsync(default);
            await reconciler.ReconcileAsync(default);
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.PrintJobs.SingleAsync(j => j.Id == ids[0])).ProductionSnapshotJson.Should().NotBeNull();
            foreach (var id in ids.Skip(1))
            {
                var job = await db.PrintJobs.SingleAsync(j => j.Id == id);
                job.ProductionSnapshotJson.Should().BeNull();
                job.SnapshotRecoveryReason.Should().NotBeNullOrWhiteSpace();
            }
        }
        (await client.DeleteAsync($"/api/v1/parts/{verified.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/parts/{changed.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var cleanup = Factory.CreateTenantScope();
        var cleanupDb = cleanup.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        foreach (var job in await cleanupDb.PrintJobs.Where(j => ids.Contains(j.Id)).ToListAsync()) job.MarkCancelled();
        await cleanupDb.SaveChangesAsync();
    }
    [Fact]
    public async Task ConcurrentUnlimitedCreationAndDeletion_NeverLeavesAnUnprotectedRun()
    {
        var client = await ClientAsync();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var part = await PartAsync();
            var create = client.PostAsJsonAsync("/api/v1/production-runs", new
            {
                lines = new[] { new { targetKind = "Part", targetId = part.Id, quantity = (int?)null } }
            });
            var delete = client.DeleteAsync($"/api/v1/parts/{part.Id}");
            await Task.WhenAll(create, delete);
            var creation = await create;
            var deletion = await delete;
            if (creation.StatusCode == HttpStatusCode.Created)
            {
                deletion.StatusCode.Should().Be(HttpStatusCode.Conflict);
                (await client.GetAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
                var run = (await creation.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.ProductionRuns.ProductionRunDetailResponse>())!;
                await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");
            }
            else
            {
                creation.StatusCode.Should().Be(HttpStatusCode.NotFound);
                deletion.StatusCode.Should().Be(HttpStatusCode.NoContent);
            }
        }
    }

}
