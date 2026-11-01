using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Infrastructure.Persistence;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.IntegrationTests.Production;

public class SharedPrintEstimateTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task PartDraftPreviewAndRunWorkload_UseTheSamePurgedPartialPlateEstimate()
    {
        var client = await ClientAsync();
        var code = string.Concat(Enumerable.Range(1, 20).Select(i => $"EXCLUDE_OBJECT_START NAME=OBJECT_{i}\nEXCLUDE_OBJECT_END NAME=OBJECT_{i}\n"));
        var part = await PartAsync(20, code, grams: 10,
            requirement: new FilamentRequirementSpec("PLA", "#000000", null, null, null, 40));
        var detail = (await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{part.Id}"))!;
        detail.PrintEstimate!.Filament.Value.Should().Be(40, "per-color grams including purge take precedence over the 10g scalar");
        detail.UnitEstimate!.Time.Value.Should().Be(3);
        detail.UnitEstimate.Filament.Value.Should().Be(2);
        var draft = await client.PostAsJsonAsync("/api/v1/parts/print-estimate", new { parts = new[] { new { partId = part.Id, quantity = 1 } } });
        draft.StatusCode.Should().Be(HttpStatusCode.OK);
        (await draft.Content.ReadFromJsonAsync<PrintEstimate>()).Should().BeEquivalentTo(detail.UnitEstimate);
        var preview = await client.PostAsJsonAsync("/api/v1/production-demands/preview", new { line = new { targetKind = "Part", targetId = part.Id, quantity = 1, quantityBasis = "Units" } });
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = (await preview.Content.ReadFromJsonAsync<ProductionPlan>())!;
        plan.Estimate.Should().BeEquivalentTo(detail.UnitEstimate);
        plan.Minutes.Should().Be(3);
        plan.Grams.Should().Be(2);
        var run = await RunAsync(client, part.Id, 1);
        var workload = (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single().RemainingEstimate;
        workload.Should().BeEquivalentTo(detail.UnitEstimate);
    }

    [Fact]
    public async Task DraftEstimate_AggregatesRepeatedParts_AndPreservesMissingSources()
    {
        var client = await ClientAsync();
        var part = await PartAsync(20);
        var response = await client.PostAsJsonAsync("/api/v1/parts/print-estimate", new { parts = new[] { new { partId = part.Id, quantity = 1 }, new { partId = part.Id, quantity = 2 }, new { partId = Guid.NewGuid(), quantity = 1 } } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var estimate = (await response.Content.ReadFromJsonAsync<PrintEstimate>())!;
        estimate.Time.Should().Be(new EstimateMetric(9, 1));
        estimate.Filament.Should().Be(new EstimateMetric(3, 1));
    }

    [Fact]
    public async Task DraftEstimate_RejectsNegativeQuantities()
    {
        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/parts/print-estimate", new { parts = new[] { new { partId = Guid.NewGuid(), quantity = -1 } } });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task RemainingWork_UsesJobTargetsAndSnapshots_ExcludingCompletedAndFailedJobs()
    {
        var client = await ClientAsync();
        var part = await MappedPartAsync(20);
        var run = await RunAsync(client, part.Id, 41);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var jobs = await db.PrintJobs.Where(j => j.PartRequirementId == run.Lines.Single().Requirements.Single().Id).OrderBy(j => j.QueuePosition).ToListAsync();
            jobs[0].MarkStarted(); jobs[0].MarkCompleted();
            jobs[1].MarkStarted(); jobs[1].MarkFailed();
            var source = await db.Parts.SingleAsync(p => p.Id == part.Id);
            db.Entry(source).Property(p => p.EstimatedPrintTimeMinutes).CurrentValue = 600;
            db.Entry(source).Property(p => p.EstimatedFilamentWeightGrams).CurrentValue = 999;
            await db.SaveChangesAsync();
        }
        var remaining = (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single().RemainingEstimate!;
        remaining.Time.Should().Be(new EstimateMetric(3, 0));
        remaining.Filament.Should().Be(new EstimateMetric(1, 0));
        remaining.Approximate.Should().BeTrue();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var waiting = await db.PrintJobs.SingleAsync(j => j.PartRequirementId == run.Lines.Single().Requirements.Single().Id
                && j.Status == OpenFarm.Domain.Production.PrintJobStatus.Queued);
            db.Entry(waiting).Property(j => j.ProductionSnapshotJson).CurrentValue = "[]";
            await db.SaveChangesAsync();
        }
        var unknown = (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single().RemainingEstimate!;
        unknown.Time.Should().Be(new EstimateMetric(null, 1), "an unreadable snapshot must not substitute the changed library estimate");
    }

}
