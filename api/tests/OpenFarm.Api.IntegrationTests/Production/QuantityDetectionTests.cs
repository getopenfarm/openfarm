using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

public class QuantityDetectionTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static readonly string[] AllObjectIds = ["A", "B", "C", "D"];
    private const string Four = "EXCLUDE_OBJECT_START NAME=A\nEXCLUDE_OBJECT_END NAME=A\nEXCLUDE_OBJECT_START NAME=B\nEXCLUDE_OBJECT_END NAME=B\nEXCLUDE_OBJECT_START NAME=C\nEXCLUDE_OBJECT_END NAME=C\nEXCLUDE_OBJECT_START NAME=D\nEXCLUDE_OBJECT_END NAME=D\n";

    [Theory]
    [InlineData(1, new[] { 1 })]
    [InlineData(3, new[] { 3 })]
    [InlineData(4, new[] { 4 })]
    [InlineData(5, new[] { 4, 1 })]
    [InlineData(10, new[] { 4, 4, 2 })]
    public async Task AutomaticObjects_MaterializeExactTargetsWithoutSavedGroups(int quantity, int[] targets)
    {
        var client = await ClientAsync();
        var part = await PartAsync(4, Four);
        var run = await RunAsync(client, part.Id, quantity);
        var jobs = await JobsAsync(run.Id);
        jobs.Select(j => j.EffectiveTargetUnits).Should().Equal(targets);
        foreach (var job in jobs)
        {
            job.ObjectExecution!.Manifest.Units.Should().HaveCount(4);
            job.ObjectExecution.SelectedUnitIds.Should().HaveCount(job.EffectiveTargetUnits);
            job.ObjectExecution.PlannedExclusions.Should().HaveCount(4 - job.EffectiveTargetUnits);
        }
    }

    [Fact]
    public async Task StaleCount_IsVisibleAndBlocksNewDemand_WithoutRepairingThePart()
    {
        var client = await ClientAsync();
        var part = await PartAsync(1, Four);
        var detail = (await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{part.Id}"))!;
        detail.UnitsPerPrint.Should().Be(1);
        detail.DetectedUnitsPerPrint.Should().Be(4);
        detail.QuantityNeedsReprocessing.Should().BeTrue();
        var response = await client.PostAsJsonAsync("/api/v1/production-demands/preview", new
        {
            line = new { targetKind = "Part", targetId = part.Id, quantity = 1, quantityBasis = "Plates" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Reprocess");
        (await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{part.Id}"))!.UnitsPerPrint.Should().Be(1);
    }

    [Fact]
    public async Task RecognizedCount_CannotBeOverridden_AndGroupedMappingsAreRejected()
    {
        var client = await ClientAsync();
        var part = await PartAsync(4, Four);
        var response = await client.PutAsJsonAsync($"/api/v1/parts/{part.Id}", new { name = "Changed", unitsPerPrint = 2, tags = Array.Empty<string>() });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var groups = await client.PutAsJsonAsync($"/api/v1/parts/{part.Id}/object-groups", new
        {
            revisionHash = part.RevisionHash,
            units = new[] { new { id = "1", objectIds = AllObjectIds } }
        });
        groups.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{part.Id}"))!.Name.Should().Be(part.Name);
    }

    [Fact]
    public async Task RawBambuLabels_AllowWholePlates_ButExplainWhyPartialPlatesAreUnavailable()
    {
        var client = await ClientAsync();
        var code = string.Concat(Enumerable.Range(1, 4).Select(i => $"; start printing object, unique label id: {i}\nM624 AQ==\n"));
        var part = await PartAsync(4, code);
        var partial = await client.PostAsJsonAsync("/api/v1/production-demands/preview", new
        {
            line = new { targetKind = "Part", targetId = part.Id, quantity = 1, quantityBasis = "Units" }
        });
        partial.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await partial.Content.ReadAsStringAsync()).Should().Contain("sliced 3MF");
        var run = await RunAsync(client, part.Id, 4);
        var job = (await JobsAsync(run.Id)).Single();
        job.EffectiveTargetUnits.Should().Be(4);
        job.ObjectExecution.Should().BeNull();
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("Replace")]
    [InlineData("Reprocess")]
    public async Task Confirmation_RefreshesQuantity_OnlyOnExplicitProcessing(string kind)
    {
        var client = await ClientAsync();
        var part = await PartAsync(1, Four);
        var snapshot = PrintJob.Create("Existing", Guid.NewGuid(), 1024, 1, AssignmentMode.Unassigned, null,
            part.RevisionHash, part.RevisionNumber, []);
        snapshot.CaptureProductionSnapshot(part);
        var frozen = snapshot.ProductionSnapshotJson;
        PartProcessingJob job;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var tracked = await db.Parts.SingleAsync(p => p.Id == part.Id);
            job = kind switch
            {
                "Add" => PartProcessingJob.CreateAdd(part.FileFormat, part.OriginalFileName, part.StoragePath, part.FileSizeBytes, false, null, null, null, null, null),
                "Replace" => PartProcessingJob.CreateReplace(part.Id, part.FileFormat, part.OriginalFileName, part.StoragePath, part.FileSizeBytes, false, null, null, null, null, null),
                _ => PartProcessingJob.CreateReprocess(tracked, null, null, null, null)
            };
            job.MarkReadyForReview(0.4m, [new PreviewFilamentRequirement(0, "PLA", null, null, null, null)],
                [new PreviewPlate(1, null, [0], 10, 5)], EstimateStatus.Available, 10, 5, []);
            await scope.ServiceProvider.GetRequiredService<IPartProcessingJobRepository>().AddAsync(job, default);
        }
        var response = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{job.Id}/confirm", new { name = kind == "Add" ? "Imported" : null });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<PartResponse>())!;
        saved.UnitsPerPrint.Should().Be(4);
        saved.DetectedUnitsPerPrint.Should().Be(4);
        var detail = (await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{saved.Id}"))!;
        detail.QuantityNeedsReprocessing.Should().BeFalse();
        detail.DetectedUnitsPerPrint.Should().Be(4);
        snapshot.ProductionSnapshotJson.Should().Be(frozen);
        snapshot.GetProductionPart()!.UnitsPerPrint.Should().Be(1);
    }
}
