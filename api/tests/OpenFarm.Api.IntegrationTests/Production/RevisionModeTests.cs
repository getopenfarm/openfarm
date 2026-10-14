using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class RevisionModeTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task Snapshot_ResolvesRetainedBytes_AfterReplacementChangesFormat()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id);
        var job = (await JobsAsync(run.Id)).Single();
        using var scope = Factory.CreateTenantScope();
        var parts = scope.ServiceProvider.GetRequiredService<IPartRepository>();
        var current = (await parts.GetByIdAsync(part.Id, default))!;
        var resolver = scope.ServiceProvider.GetRequiredService<IJobFileResolver>();
        var original = await resolver.ResolveAsync(current, job, printer.Id, 0.4m, default);
        original.FileName.Should().Be($"{part.Name}_PLA_1h0m.gcode");
        var bytes = Encoding.UTF8.GetBytes("solid changed\nendsolid changed\n");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var storage = scope.ServiceProvider.GetRequiredService<IPartFileStorage>();
        using var stream = new MemoryStream(bytes);
        var path = await storage.SaveAsync(part.OrganizationId, hash, "changed.stl", stream, default);
        current.ReplaceFile(PartFileFormat.Stl, true, "changed.stl", path, bytes.Length, 0.4m, null, null,
            EstimateStatus.Available, 60, 20, 1, hash, [new FilamentRequirementSpec("PLA", null, null, null, null)], []);
        parts.TrackReplacementChildren(current);
        await parts.SaveChangesAsync(default);
        var content = await scope.ServiceProvider.GetRequiredService<IJobFileResolver>().ResolveAsync(current, job, printer.Id, 0.4m, default);
        Encoding.UTF8.GetString(content.Bytes).Should().Be("; revision one\n");
        content.FileName.Should().Be($"{part.Name}_PLA_1h0m.gcode", "the original estimate belongs to the snapshot");
        job.FileRevisionHash.Should().Be(part.RevisionHash);
        await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/cancel");
        var requeue = await client.PostAsJsonAsync($"/api/v1/print-jobs/{job.Id}/requeue", new { });
        requeue.EnsureSuccessStatusCode();
        (await requeue.Content.ReadFromJsonAsync<PrintJobResponse>())!.FileRevisionHash.Should().Be(part.RevisionHash);
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");
    }

    [Fact]
    public async Task DefaultsAndModeChange_DoNotAlterAlreadyPrintingJob()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var finite = await RunAsync(client, part.Id);
        finite.Lines.Single().RevisionMode.Should().Be("Snapshot");
        var backfill = await RunAsync(client, part.Id, quantity: null);
        backfill.Lines.Single().RevisionMode.Should().Be("FollowLatest");
        await PostAsync(client, $"/api/v1/production-runs/{backfill.Id}/cancel");
        var printer = await PrinterAsync();
        var job = (await JobsAsync(finite.Id)).Single();
        await AssignAsync(job.Id, printer.Id);
        var response = await client.PutAsJsonAsync($"/api/v1/production-runs/{finite.Id}/lines/{finite.Lines.Single().Id}/revision-mode", new { revisionMode = "FollowLatest" });
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.UnprocessableEntity);
        (await JobsAsync(finite.Id)).Single().FileRevisionHash.Should().Be(job.FileRevisionHash);
        (await DetailAsync(client, finite.Id)).Lines.Single().SpansMultipleRevisions.Should().BeFalse();
    }
    [Fact]
    public async Task FollowLatest_DispatchesNewRevision_WithoutRewritingPrintingPlate()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id, quantity: null, pinned: printer.Id);
        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            var first = await WaitForAssignmentAsync(run.Id);
            await ReplayAsync(printer.Id, "RUNNING");
            string hash;
            using (var scope = Factory.CreateTenantScope())
            {
                var parts = scope.ServiceProvider.GetRequiredService<IPartRepository>();
                var current = (await parts.GetByIdAsync(part.Id, default))!;
                var bytes = Encoding.UTF8.GetBytes("; revision two\n");
                hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                using var stream = new MemoryStream(bytes);
                var path = await scope.ServiceProvider.GetRequiredService<IPartFileStorage>().SaveAsync(part.OrganizationId, hash, "part.gcode", stream, default);
                current.ReplaceFile(PartFileFormat.GCode, false, "part.gcode", path, bytes.Length, 0.4m, null, null,
                    EstimateStatus.Available, 60, 20, 1, hash, [new FilamentRequirementSpec("PLA", null, null, null, null)], []);
                parts.TrackReplacementChildren(current);
                await parts.SaveChangesAsync(default);
            }
            (await JobsAsync(run.Id)).Single(j => j.Id == first.Id).FileRevisionHash.Should().Be(part.RevisionHash);
            await ReplayAsync(printer.Id, "FINISH");
            worker.RequestDispatchAttempt(printer.Id);
            await Task.Delay(100);
            (await JobsAsync(run.Id)).Count(j => j.DispatchedPrinterId != null).Should().Be(1, "AwaitingClear prevents dispatch");
            await PostAsync(client, $"/api/v1/printers/{printer.Id}/mark-ready");
            worker.RequestDispatchAttempt(printer.Id);
            var second = await WaitForAssignmentAsync(run.Id, first.Id);
            await ReplayAsync(printer.Id, "RUNNING");
            second.FileRevisionHash.Should().Be(hash);
            second.FileRevisionNumber.Should().Be(2);
            (await client.GetStringAsync($"/api/v1/print-jobs/{second.Id}/file")).Should().Be("; revision two\n");
            (await DetailAsync(client, run.Id)).Lines.Single().SpansMultipleRevisions.Should().BeTrue();
        }
        finally { await worker.StopAsync(default); }
    }

}
