using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

// Test-after: the run-level actions and the run list/detail read models the Runs page is built on.
public class RunActionsEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static async Task<ListProductionRunsResponse> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<ListProductionRunsResponse>($"/api/v1/production-runs?{query}"))!;

    [Fact]
    public async Task WritingOffTheLostPlates_CompletesTheRun_AndCloseSignsItOff()
    {
        var client = await ClientAsync();
        var run = await RunAsync(client, (await PartAsync(4)).Id, 8, "Order #7");
        var jobs = await JobsAsync(run.Id);
        await PostAsync(client, $"/api/v1/print-jobs/{jobs[0].Id}/cancel");
        await PostAsync(client, $"/api/v1/print-jobs/{jobs[1].Id}/cancel");

        var detail = await DetailAsync(client, run.Id);
        detail.Lines.Single().Requirements.Single().Plates.NeedsAction.Should().Be(2);

        await PostAsync(client, $"/api/v1/print-jobs/{jobs[0].Id}/write-off");
        await PostAsync(client, $"/api/v1/print-jobs/{jobs[0].Id}/write-off", HttpStatusCode.Conflict);
        await PostAsync(client, $"/api/v1/print-jobs/{jobs[0].Id}/requeue", HttpStatusCode.Conflict);
        (await DetailAsync(client, run.Id)).Status.Should().Be("Active");
        await PostAsync(client, $"/api/v1/print-jobs/{jobs[1].Id}/write-off");

        detail = await DetailAsync(client, run.Id);
        detail.Status.Should().Be("Complete");
        var requirement = detail.Lines.Single().Requirements.Single();
        requirement.WrittenOffUnits.Should().Be(8);
        requirement.Plates.WrittenOff.Should().Be(2);
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/close");
        (await DetailAsync(client, run.Id)).Status.Should().Be("Closed");
    }

    [Fact]
    public async Task Requeue_IsRefusedOnACancelledRun()
    {
        var client = await ClientAsync();
        var run = await RunAsync(client, (await PartAsync()).Id, 2);
        var lost = (await JobsAsync(run.Id))[0];
        await PostAsync(client, $"/api/v1/print-jobs/{lost.Id}/cancel");
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");

        await PostAsync(client, $"/api/v1/print-jobs/{lost.Id}/requeue", HttpStatusCode.Conflict);
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/requeue-failed", HttpStatusCode.Conflict);
        (await JobsAsync(run.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task RequeueFailed_ReprintsEveryLostPlateInPlace()
    {
        var client = await ClientAsync();
        var run = await RunAsync(client, (await PartAsync()).Id, 3);
        var jobs = await JobsAsync(run.Id);
        await PostAsync(client, $"/api/v1/print-jobs/{jobs[0].Id}/cancel");
        await PostAsync(client, $"/api/v1/print-jobs/{jobs[2].Id}/cancel");

        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/requeue-failed");

        var after = await JobsAsync(run.Id);
        after.Should().HaveCount(5);
        after.Where(j => j.Status == PrintJobStatus.Queued).Select(j => j.QueuePosition)
            .Should().BeEquivalentTo(jobs.Select(j => j.QueuePosition));
        (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single().Plates.NeedsAction.Should().Be(0);
    }

    [Fact]
    public async Task AddLines_QueuesMorePlates_AndReopensACompleteRun()
    {
        var client = await ClientAsync();
        var printer = await PrinterAsync(manual: true);
        var run = await RunAsync(client, (await PartAsync()).Id, 1, "Order #8");
        await AssignAsync((await JobsAsync(run.Id)).Single().Id, printer.Id);
        await PostAsync(client, $"/api/v1/print-jobs/{(await JobsAsync(run.Id)).Single().Id}/complete");
        (await DetailAsync(client, run.Id)).Status.Should().Be("Complete");

        var added = await client.PostAsJsonAsync($"/api/v1/production-runs/{run.Id}/lines", new
        {
            lines = new[] { new { targetKind = "Part", targetId = (await MappedPartAsync(2)).Id, quantity = 3, assignmentMode = "Unassigned" } },
        });

        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var detail = (await added.Content.ReadFromJsonAsync<ProductionRunDetailResponse>())!;
        detail.Status.Should().Be("Active");
        detail.Lines.Should().HaveCount(2);
        detail.Lines[1].Requirements.Single().Jobs.Should().HaveCount(2);
        (await ActivityEventTypesAsync(client, run.Id)).Should().Contain("ProductionRunLinesAdded");

        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");
        (await client.PostAsJsonAsync($"/api/v1/production-runs/{run.Id}/lines", new
        {
            lines = new[] { new { targetKind = "Part", targetId = (await PartAsync()).Id, quantity = 1, assignmentMode = "Unassigned" } },
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task MoveToTop_PutsTheRunsWaitingPlatesFirst_InTheirOwnOrder()
    {
        var client = await ClientAsync();
        var first = await RunAsync(client, (await PartAsync()).Id, 2);
        var second = await RunAsync(client, (await PartAsync()).Id, 2);

        await PostAsync(client, $"/api/v1/production-runs/{second.Id}/move-to-top");

        var firstJobs = await JobsAsync(first.Id);
        var secondJobs = await JobsAsync(second.Id);
        secondJobs.Max(j => j.QueuePosition).Should().BeLessThan(firstJobs.Min(j => j.QueuePosition));
        secondJobs.Select(j => j.Id).Should().Equal(secondJobs.OrderBy(j => j.QueuePosition).Select(j => j.Id));
    }

    [Fact]
    public async Task Close_EndsAnUnlimitedOnlyRun_WithoutCancellingIt()
    {
        var client = await ClientAsync();
        var run = await RunAsync(client, (await PartAsync()).Id, quantity: null);

        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/close");

        (await DetailAsync(client, run.Id)).Status.Should().Be("Closed");
    }

    [Fact]
    public async Task RemovingAPrinter_ReleasesItsPinnedJobsAndLines()
    {
        var client = await ClientAsync();
        var printer = await PrinterAsync(state: "FINISH");
        var run = await RunAsync(client, (await PartAsync()).Id, 2, pinned: printer.Id);

        (await client.DeleteAsync($"/api/v1/printers/{printer.Id}")).EnsureSuccessStatusCode();

        (await JobsAsync(run.Id)).Should().OnlyContain(j => j.AssignmentMode == AssignmentMode.Unassigned && j.PinnedPrinterId == null);
        using var scope = Factory.Services.CreateScope();
        var line = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().ProductionDemands.SingleAsync(l => l.ProductionRunId == run.Id);
        line.DefaultAssignmentMode.Should().Be(AssignmentMode.Unassigned);
    }

    [Fact]
    public async Task Create_WithAPinToAnUnknownPrinter_Returns404()
    {
        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/production-runs", new
        {
            lines = new[] { new { targetKind = "Part", targetId = (await PartAsync()).Id, quantity = 1, assignmentMode = "Pinned", pinnedPrinterId = Guid.NewGuid() } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_FiltersBySearchAndStatus_CountsEachStatus_AndSortsByDueDate()
    {
        var client = await ClientAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var part = await PartAsync(4);
        var later = await RunAsync(client, part.Id, 8, $"{tag} later");
        var sooner = await RunAsync(client, part.Id, 8, $"{tag} sooner");
        var undated = await RunAsync(client, part.Id, 8, $"{tag} undated");
        var cancelled = await RunAsync(client, part.Id, 8, $"{tag} cancelled");
        (await client.PutAsJsonAsync($"/api/v1/production-runs/{later.Id}", new { name = $"{tag} later", dueDate = "2026-12-01" })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/production-runs/{sooner.Id}", new { name = $"{tag} sooner", dueDate = "2026-11-01" })).EnsureSuccessStatusCode();
        await PostAsync(client, $"/api/v1/production-runs/{cancelled.Id}/cancel");

        var open = await ListAsync(client, $"search={tag}&status=Open&sortBy=dueDate&sortDirection=asc");

        open.Items.Select(r => r.Id).Should().Equal(sooner.Id, later.Id, undated.Id);
        open.StatusCounts.Should().Contain("Open", 3).And.Contain("Cancelled", 1).And.Contain("Closed", 0);
        var row = open.Items[0];
        row.TargetUnits.Should().Be(8);
        row.Plates.Queued.Should().Be(2);
        row.Lines.Should().ContainSingle(l => l.Quantity == 8);
        (await ListAsync(client, $"search={tag}&status=Cancelled")).Items.Should().ContainSingle(r => r.Id == cancelled.Id);
    }

    [Fact]
    public async Task Detail_SaysWhyTheNextPlateWaits()
    {
        var client = await ClientAsync();
        var petg = await PartAsync(requirement: new OpenFarm.Domain.Parts.FilamentRequirementSpec("PETG", null, null, null, null));
        var run = await RunAsync(client, petg.Id, 1);

        var requirement = (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single();

        requirement.BlockingReason.Should().NotBeNullOrWhiteSpace("no printer has PETG loaded");
        requirement.Jobs.Should().ContainSingle(j => j.Status == "Queued");
        requirement.EstimatedMinutesPerPlate.Should().Be(60);
    }
}
