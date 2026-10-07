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
    public async Task CancelRun_LeavesPrintingJobAlive_ThenRecordsPartialUsageOnFailure()
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
        Guid spoolId;
        Guid secondSpoolId;
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var vendor = Vendor.Create(Organization.WellKnownId, "Test vendor");
            db.Vendors.Add(vendor);
            var filament = Filament.Create(Organization.WellKnownId, vendor.Id, "PLA test", "PLA");
            var variant = filament.AddVariant("White", "FFFFFF");
            db.Filaments.Add(filament);
            var spool = Spool.Create(Organization.WellKnownId, variant.Id, null, 1000, 1000);
            var feeder = FeedUnit.CreateAutomaticFilamentReloader(Organization.WellKnownId, printer.Id, 1, "Consumption test", null);
            spool.LoadOntoFeedSlot(feeder.AddFeedSlot(0).Id);
            db.FeedUnits.Add(feeder);
            db.Spools.Add(spool);
            var secondSpool = Spool.Create(Organization.WellKnownId, variant.Id, null, 1000, 1000);
            secondSpool.LoadOntoFeedSlot(feeder.AddFeedSlot(1).Id);
            db.Spools.Add(secondSpool);
            secondSpoolId = secondSpool.Id;
            await db.SaveChangesAsync();
            spoolId = spool.Id;
        }
        var body = new { filamentConsumption = new[] { new { materialType = "PLA", detectedColorHex = (string?)null, spoolId, gramsConsumed = 12.5m }, new { materialType = "PLA", detectedColorHex = (string?)null, spoolId = secondSpoolId, gramsConsumed = 3m } } };
        await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/fail", body: body);
        await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/filament-consumption", body: body);
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Spools.SingleAsync(s => s.Id == spoolId)).RemainingWeightGrams.Should().Be(987.5m);
            (await db.Spools.SingleAsync(s => s.Id == secondSpoolId)).RemainingWeightGrams.Should().Be(997m);
            (await db.JobFilamentConsumptions.Where(c => c.PrintJobId == job.Id).ToListAsync()).Should().HaveCount(2);
        }
        (await DetailAsync(client, run.Id)).Status.Should().Be("Cancelled");
    }
}
