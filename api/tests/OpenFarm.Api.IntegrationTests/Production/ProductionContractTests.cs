using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Organizations;
using OpenFarm.Api.Endpoints.Parts;

namespace OpenFarm.Api.IntegrationTests.Production;

public class ProductionContractTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task UnitsPerPrint_OperatorOverrideChangesFuturePlateCounts()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var response = await client.PatchAsJsonAsync($"/api/v1/parts/{part.Id}/units-per-print", new { unitsPerPrint = 4 });
        response.EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{part.Id}"))!.UnitsPerPrint.Should().Be(4);
        var run = await RunAsync(client, part.Id, 20);
        (await JobsAsync(run.Id)).Should().HaveCount(5);
        response = await client.PatchAsJsonAsync($"/api/v1/parts/{part.Id}/units-per-print", new { unitsPerPrint = 0 });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task JobFilamentOverrides_ArePersisted_AndUsedForMatching()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var run = await RunAsync(client, part.Id);
        var job = (await JobsAsync(run.Id)).Single();
        var response = await client.PutAsJsonAsync($"/api/v1/print-jobs/{job.Id}/filament-requirements", new
        { requirements = new[] { new { materialType = "PETG", detectedColorHex = "000000" } } });
        response.EnsureSuccessStatusCode();
        var detail = await client.GetFromJsonAsync<OpenFarm.Api.Endpoints.PrintJobs.PrintJobResponse>($"/api/v1/print-jobs/{job.Id}");
        detail!.FilamentConsumption.Should().ContainSingle(c => c.MaterialType == "PETG" && c.DetectedColorHex == "000000");
        var file = await client.GetAsync($"/api/v1/print-jobs/{job.Id}/file");
        file.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "production output needs a printer and its tracked filament assignment");
    }

    [Fact]
    public async Task OpenApi_IncludesProductionContracts()
    {
        var client = await ClientAsync();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("confirmRunCompletion").And.Contain("/api/v1/production-runs");
        if (Environment.GetEnvironmentVariable("OPENFARM_TEST_OPENAPI_EXPORT") is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, json);
    }
    [Fact]
    public async Task Confirmation_SuggestsObjectCount_AndPreservesLegacySnapshotOnReplacement()
    {
        var client = await ClientAsync();
        var legacy = await PartAsync(legacy: true);
        var printer = await PrinterAsync();
        var run = await RunAsync(client, legacy.Id);
        var snapshot = (await JobsAsync(run.Id)).Single();
        snapshot.FileRevisionHash.Should().HaveLength(64);
        snapshot.FileRevisionNumber.Should().Be(1);
        using var scope = Factory.CreateTenantScope();
        var storage = scope.ServiceProvider.GetRequiredService<IPartFileStorage>();
        var bytes = Encoding.UTF8.GetBytes("; object_count = 3\n");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var stream = new MemoryStream(bytes);
        var path = await storage.SaveAsync(Organization.WellKnownId, hash, "new.gcode", stream, default);
        var add = PartProcessingJob.CreateAdd(Organization.WellKnownId, PartFileFormat.GCode, "new.gcode", path, bytes.Length, false, null, null, null, null, null);
        var replace = PartProcessingJob.CreateReplace(Organization.WellKnownId, legacy.Id, PartFileFormat.GCode, "new.gcode", path, bytes.Length, false, null, null, null, null, null);
        var jobs = scope.ServiceProvider.GetRequiredService<IPartProcessingJobRepository>();
        foreach (var processing in new[] { add, replace })
        {
            processing.MarkReadyForReview(0.4m, [new PreviewFilamentRequirement(0, "PLA", null, null)], 1, EstimateStatus.Available, 60, 20, []);
            await jobs.AddAsync(processing, default);
        }
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ConfirmPartProcessingJobCommand, Part>>();
        var suggested = await handler.HandleAsync(new ConfirmPartProcessingJobCommand(add.Id, "Suggested part", [], [], null), default);
        suggested.UnitsPerPrint.Should().Be(3);
        var current = await handler.HandleAsync(new ConfirmPartProcessingJobCommand(replace.Id, null, null, [], null), default);
        current.UnitsPerPrint.Should().Be(1, "replacement preserves the operator's existing default");
        var content = await scope.ServiceProvider.GetRequiredService<OpenFarm.Application.Production.IJobFileResolver>().ResolveAsync(current, snapshot, printer.Id, 0.4m, default);
        Encoding.UTF8.GetString(content.Bytes).Should().Be("; revision one\n");
        var legacySnapshot = OpenFarm.Domain.Production.PrintJob.Create(legacy.OrganizationId, snapshot.PartRequirementId, 1024, 1,
            OpenFarm.Domain.Production.AssignmentMode.Unassigned, null, legacy.RevisionHash, 1, [new("PLA", null)]);
        var legacyBytes = await scope.ServiceProvider.GetRequiredService<OpenFarm.Application.Production.IJobFileResolver>().ResolveAsync(current, legacySnapshot, printer.Id, 0.4m, default);
        Encoding.UTF8.GetString(legacyBytes.Bytes).Should().Be("; revision one\n");
    }

}
