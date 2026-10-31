using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Application.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

// A dedicated fixture prevents earlier tests' queued work from competing for this dispatch assertion.
public class DirectDemandDispatchTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task DirectDemand_DispatchesWithoutRun_UsingExistingWorker()
    {
        var client = await ClientAsync();
        var part = await PartAsync(2);
        var printer = await PrinterAsync();
        var line = new { targetKind = "Part", targetId = part.Id, quantity = 1, quantityBasis = "Plates", assignmentMode = "Pinned", pinnedPrinterId = printer.Id };
        var preview = await client.PostAsJsonAsync("/api/v1/production-demands/preview", new { line });
        preview.EnsureSuccessStatusCode();
        var plan = (await preview.Content.ReadFromJsonAsync<ProductionPlan>())!;
        var response = await client.PostAsJsonAsync("/api/v1/production-demands", new { submissionId = Guid.NewGuid(), line, planVersion = plan.Version });
        response.EnsureSuccessStatusCode();
        var demand = (await response.Content.ReadFromJsonAsync<ProductionDemandResponse>())!;
        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            for (var i = 0; i < 100; i++)
            {
                using var scope = Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
                if (await (from job in db.PrintJobs
                           join req in db.PartRequirements on job.PartRequirementId equals req.Id
                           where req.RunLineId == demand.Id && job.PrinterId == printer.Id
                           select job).AnyAsync()) return;
                await Task.Delay(100);
            }
            Assert.Fail("The direct plate was not dispatched.");
        }
        finally { await worker.StopAsync(default); }
    }
}
