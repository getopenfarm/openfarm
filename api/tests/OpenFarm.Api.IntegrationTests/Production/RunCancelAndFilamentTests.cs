using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

public class RunCancelAndFilamentTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task CancelRun_LeavesPrintingJobAlive_ThenFailureOnManualPrinterDeductsNothing()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync(manual: true);
        var run = await RunAsync(client, part.Id, 3);
        var job = (await JobsAsync(run.Id))[0];
        await AssignAsync(job.Id, printer.Id);
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");
        var jobs = await JobsAsync(run.Id);
        jobs.Count(j => j.Status == PrintJobStatus.Cancelled).Should().Be(2);
        jobs.Single(j => j.Id == job.Id).Status.Should().Be(PrintJobStatus.Printing);
        await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/fail");
        (await DetailAsync(client, run.Id)).Status.Should().Be("Cancelled");
    }
}
