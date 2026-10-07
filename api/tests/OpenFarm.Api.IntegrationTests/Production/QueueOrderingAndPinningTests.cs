using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class QueueOrderingAndPinningTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelQueuedJob_RemovesItFromQueue_AndPreservesHistory(bool backfill)
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var run = await RunAsync(client, part.Id, backfill ? null : 2);
        if (backfill)
        {
            // Unlimited runs create jobs lazily; seed one waiting for dispatch/retry.
            using var scope = Factory.CreateTenantScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
            var job = PrintJob.Create(Organization.WellKnownId, run.Lines[0].Requirements[0].Id,
                1024, 1, AssignmentMode.Unassigned, null, part.RevisionHash, part.RevisionNumber, []);
            await repository.AddAsync(job, default);
            await repository.SaveChangesAsync(default);
        }
        var jobs = await JobsAsync(run.Id);
        var id = jobs[0].Id;
        var view = backfill ? "backfill" : "queue";
        var before = await client.GetFromJsonAsync<ListQueueJobsResponse>($"/api/v1/print-jobs?view={view}&pageSize=1000");
        before!.Items.Should().Contain(j => j.JobId == id);

        await PostAsync(client, $"/api/v1/print-jobs/{id}/cancel");

        var after = await client.GetFromJsonAsync<ListQueueJobsResponse>($"/api/v1/print-jobs?view={view}&pageSize=1000");
        after!.Items.Should().NotContain(j => j.JobId == id);
        after.TotalCount.Should().Be(before.TotalCount - 1);
        (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{id}"))!.Status.Should().Be("Cancelled");
        (await JobsAsync(run.Id)).Should().Contain(j => j.Id == id && j.Status == PrintJobStatus.Cancelled);
    }

    [Fact]
    public async Task Reorder_ChangesMatcherOrder_AndPinnedUnclearedPrinterStaysBlocked()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var run = await RunAsync(client, part.Id, 2);
        var jobs = await JobsAsync(run.Id);
        var response = await client.PostAsJsonAsync($"/api/v1/print-jobs/{jobs[1].Id}/reorder", new { afterJobId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        using (var scope = Factory.CreateTenantScope())
        {
            var candidates = await scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>().GetUnassignedCandidatesAsync(default);
            candidates[0].JobId.Should().Be(jobs[1].Id);
        }
        var printer = await PrinterAsync(state: "FINISH");
        response = await client.PostAsJsonAsync($"/api/v1/print-jobs/{jobs[1].Id}/pin", new { printerId = printer.Id });
        response.EnsureSuccessStatusCode();
        var queue = await client.GetFromJsonAsync<ListQueueJobsResponse>("/api/v1/print-jobs?view=queue&pageSize=1000");
        queue!.Items.Should().Contain(j => j.JobId == jobs[1].Id && j.IsBlocked);
        await AssignAsync(jobs[0].Id, printer.Id);
        response = await client.PostAsJsonAsync($"/api/v1/print-jobs/{jobs[0].Id}/reorder", new { afterJobId = (Guid?)null });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response = await client.PostAsJsonAsync($"/api/v1/print-jobs/{jobs[0].Id}/pin", new { printerId = printer.Id });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response = await client.PostAsJsonAsync($"/api/v1/print-jobs/{jobs[0].Id}/unpin", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
