using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class ShortfallAndRequeueTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Theory]
    [InlineData(null)]
    [InlineData("Named")]
    public async Task CancelledFiniteJob_HasExplicitOneTimeReplacement(string? name)
    {
        var client = await ClientAsync();
        var part = await PartAsync(4);
        var run = await RunAsync(client, part.Id, 8, name);
        var original = (await JobsAsync(run.Id))[0];
        await PostAsync(client, $"/api/v1/print-jobs/{original.Id}/cancel");
        (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single().JobsNeedingRequeue.Should().Equal(original.Id);
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/close", HttpStatusCode.Conflict);
        var response = await client.PostAsJsonAsync($"/api/v1/print-jobs/{original.Id}/requeue", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var replacement = (await response.Content.ReadFromJsonAsync<PrintJobResponse>())!;
        replacement.PartRequirementId.Should().Be(original.PartRequirementId);
        replacement.PlateQuantity.Should().Be(4);
        (await JobsAsync(run.Id)).Should().HaveCount(3);
        (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single().JobsNeedingRequeue.Should().BeEmpty();
        await PostAsync(client, $"/api/v1/print-jobs/{original.Id}/requeue", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CancelledBackfillJob_HasNoShortfallOrRequeue()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var run = await RunAsync(client, part.Id, quantity: null);
        Guid id;
        using (var scope = Factory.CreateTenantScope())
        {
            var job = PrintJob.Create(part.OrganizationId, run.Lines.Single().Requirements.Single().Id, 1024, 1,
                AssignmentMode.Unassigned, null, part.RevisionHash, 1, []);
            job.MarkCancelled();
            var repo = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
            await repo.AddAsync(job, default);
            await repo.SaveChangesAsync(default);
            id = job.Id;
        }
        (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single().JobsNeedingRequeue.Should().BeEmpty();
        await PostAsync(client, $"/api/v1/print-jobs/{id}/requeue", HttpStatusCode.Conflict);
    }
}
