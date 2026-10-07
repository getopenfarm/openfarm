using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

public class JobMatchingDiagnosticsTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Theory]
    [InlineData("Snapshot", false)]
    [InlineData("FollowLatest", true)]
    public async Task SelectedVariantOverridesDetectedColour_AndRevisionModeControlsLaterEdits(string mode, bool follows)
    {
        var client = await ClientAsync();
        var source = await PartAsync();
        var printer = await PrinterAsync();
        Guid variantId;
        Guid processingId;
        Part part;
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            variantId = await (from spool in db.Spools
                               join slot in db.FeedSlots on spool.FeedSlotId equals slot.Id
                               join unit in db.FeedUnits on slot.FeedUnitId equals unit.Id
                               where unit.PrinterId == printer.Id
                               select spool.FilamentVariantId).SingleAsync();
            var processing = PartProcessingJob.CreateAdd(source.OrganizationId, source.FileFormat,
                source.OriginalFileName, source.StoragePath, source.FileSizeBytes, false, null, null, null, null, null);
            processing.MarkReadyForReview(0.4m, [new PreviewFilamentRequirement(0, "PLA", "#000000", null)],
                1, EstimateStatus.Available, 60, 20, []);
            await scope.ServiceProvider.GetRequiredService<IPartProcessingJobRepository>().AddAsync(processing, default);
            processingId = processing.Id;
        }
        var confirm = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{processingId}/confirm",
            new
            {
                name = "Selected catalog filament",
                filamentRequirementOverrides = new[] {
                new { requirementIndex = 0, narrowFilamentVariantId = variantId } }
            });
        confirm.StatusCode.Should().Be(HttpStatusCode.Created);
        var saved = (await confirm.Content.ReadFromJsonAsync<PartResponse>())!;
        var reopened = await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{saved.Id}");
        reopened!.FilamentRequirements.Single().NarrowFilamentVariantId.Should().Be(variantId);
        using (var scope = Factory.CreateTenantScope())
            part = (await scope.ServiceProvider.GetRequiredService<IPartRepository>().GetByIdAsync(saved.Id, default))!;
        var run = await RunAsync(client, part.Id, revisionMode: mode, pinned: printer.Id);
        var job = (await JobsAsync(run.Id)).Single();
        job.FilamentConsumption.Single().NarrowFilamentVariantId.Should().Be(variantId);
        var diagnostics = await client.GetFromJsonAsync<JobMatchingDiagnostics>($"/api/v1/print-jobs/{job.Id}/matching-diagnostics");
        diagnostics!.Printers.Single(p => p.PrinterId == printer.Id).Eligible.Should().BeTrue();
        diagnostics.Requirements.Single().DetectedColorHex.Should().Be("#000000");
        diagnostics.Requirements.Single().SelectedVariantId.Should().Be(variantId);

        // A part edit clears the selected identity: only Follow-latest returns to file-colour matching.
        var patch = await client.PatchAsJsonAsync($"/api/v1/parts/{part.Id}/filament-requirements/{part.FilamentRequirements.Single().Id}",
            new { narrowFilamentVariantId = (Guid?)null, narrowFilamentProfileId = (Guid?)null });
        patch.EnsureSuccessStatusCode();
        diagnostics = await client.GetFromJsonAsync<JobMatchingDiagnostics>($"/api/v1/print-jobs/{job.Id}/matching-diagnostics");
        var result = diagnostics!.Printers.Single(p => p.PrinterId == printer.Id);
        result.Eligible.Should().Be(!follows);
        diagnostics.RequirementSource.Should().Be(follows ? "Follow-latest part" : "Job snapshot");
        if (follows) result.Checks.Should().Contain(c => c.Code == "filament" && c.Status == "Failed");

        var queue = await client.GetFromJsonAsync<ListQueueJobsResponse>("/api/v1/print-jobs?view=queue&pageSize=1000");
        queue!.Items.Single(j => j.JobId == job.Id).IsBlocked.Should().Be(follows);
        // An explicit job override wins over Follow-latest and the part's detected colour.
        var change = await client.PutAsJsonAsync($"/api/v1/print-jobs/{job.Id}/filament-requirements",
            new { requirements = new[] { new { materialType = "PLA", narrowFilamentVariantId = variantId, detectedColorHex = "#000000" } } });
        change.EnsureSuccessStatusCode();
        diagnostics = await client.GetFromJsonAsync<JobMatchingDiagnostics>($"/api/v1/print-jobs/{job.Id}/matching-diagnostics");
        diagnostics!.RequirementSource.Should().Be("Job override");
        diagnostics.Printers.Single(p => p.PrinterId == printer.Id).Eligible.Should().BeTrue();
        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            worker.RequestDispatchAttempt(printer.Id);
            var assigned = await WaitForAssignmentAsync(run.Id);
            assigned.DispatchedPrinterId.Should().Be(printer.Id);
            assigned.FilamentConsumption.Single().NarrowFilamentVariantId.Should().Be(variantId);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task DiagnosticsRequireAuthentication_AndReturnNotFoundForForeignTenant()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var run = await RunAsync(client, part.Id);
        var job = (await JobsAsync(run.Id)).Single();
        using var anonymous = Factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/print-jobs/{job.Id}/matching-diagnostics")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PrintJobs\" SET \"TenantId\" = {Guid.NewGuid().ToString()} WHERE \"Id\" = {job.Id}");
        }
        (await client.GetAsync($"/api/v1/print-jobs/{job.Id}/matching-diagnostics")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CatalogSelectionRejectsMissingVariants()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var response = await client.PatchAsJsonAsync($"/api/v1/parts/{part.Id}/filament-requirements/{part.FilamentRequirements.Single().Id}",
            new { narrowFilamentVariantId = Guid.NewGuid() });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
