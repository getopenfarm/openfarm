using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class ProductionDemandTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static async Task<ProductionPlan> PreviewAsync(HttpClient client, object line, object? plateOverrides = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/production-demands/preview", new { line, plateOverrides });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProductionPlan>())!;
    }

    [Theory]
    [InlineData("Plates", 3, 3, 18)]
    [InlineData("Units", 14, 3, 14)]
    public async Task DirectSubmission_UsesExplicitBasis_CreatesNoRun_AndIsIdempotent(string basis, int quantity, int plates, int output)
    {
        var client = await ClientAsync();
        var part = await MappedPartAsync(6);
        var line = new { targetKind = "Part", targetId = part.Id, quantity, quantityBasis = basis };
        var plan = await PreviewAsync(client, line);
        plan.Plates.Should().Be(plates);
        plan.Parts.Single().PlannedUnits.Should().Be(output);
        var request = new { submissionId = Guid.NewGuid(), line, planVersion = plan.Version };
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/v1/production-demands", request)));
        foreach (var response in responses) response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var demand = (await responses[0].Content.ReadFromJsonAsync<ProductionDemandResponse>())!;
        demand.ProductionRunId.Should().BeNull();
        (await responses[1].Content.ReadFromJsonAsync<ProductionDemandResponse>())!.Id.Should().Be(demand.Id);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var jobs = await (from job in db.PrintJobs
                          join req in db.PartRequirements on job.PartRequirementId equals req.Id
                          where req.RunLineId == demand.Id
                          select job).ToListAsync();
        jobs.Should().HaveCount(plates);
        jobs.Sum(j => j.EffectiveTargetUnits).Should().Be(output);
        jobs.Should().OnlyContain(j => j.GetProductionPart() != null && j.PlateQuantity == 6);
        var queue = await client.GetFromJsonAsync<ListQueueJobsResponse>($"/api/v1/print-jobs?search={Uri.EscapeDataString(part.Name)}");
        queue!.Items.Should().OnlyContain(j => j.ProductionRunId == null);
        // Finite snapshots survive source removal and requeue without a Run.
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await PostAsync(client, $"/api/v1/print-jobs/{jobs[0].Id}/cancel");
        var cancelled = await client.GetFromJsonAsync<ListQueueJobsResponse>($"/api/v1/print-jobs?status=Cancelled&search={Uri.EscapeDataString(part.Name)}");
        cancelled!.Items.Single(j => j.JobId == jobs[0].Id).CanRequeue.Should().BeTrue();
        await PostAsync(client, $"/api/v1/print-jobs/{jobs[0].Id}/requeue", HttpStatusCode.Created);
    }

    [Fact]
    public async Task ChangedPlateCapacity_RejectsStalePreview_WithoutPersistingDemand()
    {
        var client = await ClientAsync();
        var part = await PartAsync(4);
        var line = new { targetKind = "Part", targetId = part.Id, quantity = 3, quantityBasis = "Plates" };
        var plan = await PreviewAsync(client, line);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var tracked = await db.Parts.SingleAsync(p => p.Id == part.Id);
            db.Entry(tracked).Property(p => p.UnitsPerPrint).CurrentValue = 6;
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync("/api/v1/production-demands", new { submissionId = Guid.NewGuid(), line, planVersion = plan.Version });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Backfill_IsVisibleBeforeJobs_PauseAndRemovePreserveHistory_AndProtectSource()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var response = await client.PostAsJsonAsync("/api/v1/production-demands", new
        {
            submissionId = Guid.NewGuid(),
            line = new { targetKind = "Part", targetId = part.Id, quantity = (int?)null, quantityBasis = "Plates" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var demand = (await response.Content.ReadFromJsonAsync<ProductionDemandResponse>())!;
        (await client.GetFromJsonAsync<List<ProductionDemandResponse>>("/api/v1/production-demands/backfill"))!.Should().Contain(d => d.Id == demand.Id);
        foreach (var action in new[] { "Pause", "Enable", "Remove" })
        {
            if (action != "Remove") (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await client.PostAsJsonAsync($"/api/v1/production-demands/{demand.Id}/backfill", new { action })).EnsureSuccessStatusCode();
            using var scope = Factory.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<IProductionDemandRepository>().CanDispatchAsync(demand.Id, default))
                .Should().Be(action == "Enable");
        }
        (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }


    [Fact]
    public async Task PartialPlateWithoutObjectMapping_IsRejected()
    {
        var client = await ClientAsync();
        var part = await PartAsync(6);
        var response = await client.PostAsJsonAsync("/api/v1/production-demands/preview", new { line = new { targetKind = "Part", targetId = part.Id, quantity = 10, quantityBasis = "Units" } });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private async Task<(OpenFarm.Domain.Assemblies.Assembly Assembly, OpenFarm.Domain.Parts.Part Bracket, OpenFarm.Domain.Parts.Part Cover)> AssemblyAsync()
    {
        var bracket = await MappedPartAsync(6);
        var cover = await MappedPartAsync(4);
        var assembly = OpenFarm.Domain.Assemblies.Assembly.Create("Lamp", null, [
            new(OpenFarm.Domain.Assemblies.BomLineTargetKind.Part, bracket.Id, null, null, 2),
            new(OpenFarm.Domain.Assemblies.BomLineTargetKind.Part, cover.Id, null, null, 1)]);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        db.Assemblies.Add(assembly);
        await db.SaveChangesAsync();
        return (assembly, bracket, cover);
    }

    [Fact]
    public async Task AssemblyBatch_CombinesBeforeSplitting_AndRejectsPlateOverrides()
    {
        var client = await ClientAsync();
        var (assembly, bracket, cover) = await AssemblyAsync();
        var line = new { targetKind = "Assembly", targetId = assembly.Id, quantity = 10, quantityBasis = "Sets" };
        var plan = await PreviewAsync(client, line);
        plan.Plates.Should().Be(7);
        plan.Parts.Single(p => p.PartId == bracket.Id).JobUnits.Should().Equal(6, 6, 6, 2);
        plan.Parts.Single(p => p.PartId == cover.Id).JobUnits.Should().Equal(4, 4, 2);
        plan.Parts.Should().OnlyContain(p => p.SurplusUnits == 0);
        plan.CompleteSets.Should().Be(10);
        var overrides = new Dictionary<Guid, int> { [bracket.Id] = 3 };
        var adjusted = await client.PostAsJsonAsync("/api/v1/production-demands/preview", new { line, plateOverrides = overrides });
        adjusted.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

    }

    [Fact]
    public async Task AssemblyBackfill_BalancesReservedAndCompletedOutput_AndRefreshRetainsOldRecipe()
    {
        var client = await ClientAsync();
        var (assembly, bracket, cover) = await AssemblyAsync();
        var printer = await PrinterAsync();
        var response = await client.PostAsJsonAsync("/api/v1/production-demands", new
        {
            submissionId = Guid.NewGuid(),
            line = new { targetKind = "Assembly", targetId = assembly.Id, quantity = (int?)null, quantityBasis = "Sets" }
        });
        response.EnsureSuccessStatusCode();
        var demand = (await response.Content.ReadFromJsonAsync<ProductionDemandResponse>())!;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var req = await db.PartRequirements.SingleAsync(r => r.RunLineId == demand.Id && r.PartId == bracket.Id);
            req.UnitsPerSet.Should().Be(2);
            var job = PrintJob.Create(bracket.Name, req.Id, 1024, 6, AssignmentMode.Unassigned, null, bracket.RevisionHash, bracket.RevisionNumber, []);
            job.CaptureProductionSnapshot(bracket);
            job.Dispatch(printer.Id); job.MarkStarted(); job.MarkCompleted();
            db.PrintJobs.Add(job); await db.SaveChangesAsync();
            var candidates = await scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>().GetBackfillCandidatesAsync(default);
            var own = candidates.Where(c => c.RunLineId == demand.Id).ToList();
            own.Should().ContainSingle();
            (await db.PartRequirements.SingleAsync(r => r.Id == own.Single().PartRequirementId)).PartId.Should().Be(cover.Id);
        }
        // Refresh twice exercises the filtered active-recipe uniqueness constraint while retaining history.
        foreach (var unused in Enumerable.Range(0, 2))
            (await client.PostAsJsonAsync($"/api/v1/production-demands/{demand.Id}/backfill", new { action = "Refresh" })).EnsureSuccessStatusCode();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.PartRequirements.CountAsync(r => r.RunLineId == demand.Id)).Should().Be(6);
            (await db.PartRequirements.CountAsync(r => r.RunLineId == demand.Id && r.RecipeActive)).Should().Be(2);
            (await db.PrintJobs.CountAsync(j => db.PartRequirements.Any(r => r.RunLineId == demand.Id && r.Id == j.PartRequirementId))).Should().Be(1);
        }
    }

    [Fact]
    public async Task WaitingBackfillRetry_IsSeparateFromFiniteCandidates_AndPauseBlocksIt()
    {
        var client = await ClientAsync();
        var part = await PartAsync(2);
        var created = await client.PostAsJsonAsync("/api/v1/production-demands", new
        {
            submissionId = Guid.NewGuid(),
            line = new { targetKind = "Part", targetId = part.Id, quantity = (int?)null, quantityBasis = "Plates" }
        });
        created.EnsureSuccessStatusCode();
        var backfill = (await created.Content.ReadFromJsonAsync<ProductionDemandResponse>())!;
        var finite = await RunAsync(client, part.Id, 2);
        Guid retryId;
        var printer = await PrinterAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var requirement = await db.PartRequirements.SingleAsync(r => r.RunLineId == backfill.Id);
            var job = PrintJob.Create(part.Name, requirement.Id, -1024, 2, AssignmentMode.Unassigned, null, part.RevisionHash, part.RevisionNumber, []);
            job.CaptureProductionSnapshot(part); retryId = job.Id;
            db.PrintJobs.Add(job); await db.SaveChangesAsync();
            var query = scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>();
            (await query.GetUnassignedCandidatesAsync(default)).Should().NotContain(c => c.JobId == retryId);
            (await query.GetUnassignedCandidatesAsync(default)).Should().Contain(c => c.JobId == (db.PrintJobs
                .Where(j => db.PartRequirements.Any(r => r.Id == j.PartRequirementId && db.ProductionDemands.Any(d => d.Id == r.RunLineId && d.ProductionRunId == finite.Id))).First().Id));
            (await query.GetBackfillRetryCandidatesAsync(printer.Id, default)).Should().Contain(c => c.JobId == retryId);
        }
        (await client.PostAsJsonAsync($"/api/v1/production-demands/{backfill.Id}/backfill", new { action = "Pause" })).EnsureSuccessStatusCode();
        using (var scope = Factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>().GetBackfillRetryCandidatesAsync(printer.Id, default))
                .Should().NotContain(c => c.JobId == retryId);
    }

}
