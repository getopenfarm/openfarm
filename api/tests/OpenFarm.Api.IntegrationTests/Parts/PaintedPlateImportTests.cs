using System.IO.Compression;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFarm.Api.Endpoints.PartProcessingJobs;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.IntegrationTests.Mcp;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;
using OpenFarm.Infrastructure.Persistence;
using static OpenFarm.Api.IntegrationTests.Parts.OptionalPartMetadataTests;

namespace OpenFarm.Api.IntegrationTests.Parts;

public sealed class PaintedPlateTestFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<ISlicerEngine>(new PlateEstimator())));
    }

    private sealed class PlateEstimator : ISlicerEngine
    {
        public async Task<SlicingResult> EstimateAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
            EstimationConfiguration configuration, CancellationToken cancellationToken)
        {
            // Recorded per-tool usage for two plates. Keep the real metadata parser and processing pipeline.
            var bytes = Zip(("Metadata/plate_1.gcode", Code("0.68,0,3.84,0")),
                ("Metadata/plate_2.gcode", Code("0,6.33,0.47,0.87")));
            using var output = new MemoryStream(bytes);
            var parsed = await new AlreadySlicedFileMetadataParser().ParseAsync(output, PartFileFormat.ThreeMf, cancellationToken);
            return new(parsed.EstimatedPrintTimeMinutes, parsed.EstimatedFilamentWeightGrams, [], bytes, PartFileFormat.ThreeMf, parsed.Plates);
        }

        private static string Code(string grams) => "; nozzle_diameter = 0.4\n; filament_type = PLA;PLA;PLA;PLA\n"
            + "; filament_colour = #FFCC3C;#FFFFFF;#000000;#CE0000\n; total estimated time = 10m\n"
            + $"; filament used [g] = {grams}\nG28\n";

        public Task<SlicingResult> SliceConfiguredAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
            string materialType, decimal nozzleDiameterMm, Guid printerId, ResolvedSlicingProfiles profiles, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Library import must use estimation.");
    }
}

public sealed class PaintedPlateImportTests(PaintedPlateTestFactory factory) : IClassFixture<PaintedPlateTestFactory>
{
    [Fact]
    public async Task ImportAndReprocess_PreservePlateIdentityAndAssemblyReferences_WithoutPhantomColours()
    {
        using var client = await McpTestClient.BrowserAsync(factory);
        const string project = """{"printer_model":"Snapmaker U1","printer_settings_id":"Snapmaker U1 (0.4 nozzle)","print_settings_id":"Custom process","curr_bed_type":"Textured PEI Plate","nozzle_diameter":["0.4"],"filament_type":["PLA","PLA","PLA","PLA"],"filament_colour":["#FFCC3C","#FFFFFF","#000000","#CE0000"]}""";
        var file = Zip(("Metadata/project_settings.config", project), ("Metadata/model_settings.config", """
            <config>
              <object id="2"><metadata key="extruder" value="3"/></object>
              <object id="4"><metadata key="extruder" value="2"/></object>
              <plate><metadata key="plater_id" value="1"/><metadata key="plater_name" value="Pikachu"/><model_instance><metadata key="object_id" value="2"/></model_instance></plate>
              <plate><metadata key="plater_id" value="2"/><metadata key="plater_name" value="General"/><model_instance><metadata key="object_id" value="4"/></model_instance></plate>
            </config>
            """), ("3D/3dmodel.model", """
            <model xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02"><resources>
              <object id="2"><mesh><triangles><triangle paint_color="4"/></triangles></mesh></object>
              <object id="4"><mesh><triangles><triangle paint_color="0C1C1"/></triangles></mesh></object>
            </resources><build><item objectid="2"/><item objectid="4"/></build></model>
            """));
        var name = $"Painted labels {Guid.NewGuid():N}";
        var submitted = await SubmitAsync(client, "/api/v1/parts", "labels.3mf", file);
        var review = await WaitAsync(client, submitted.JobId);
        review.Preview!.Plates[0].FilamentRequirements.Should().HaveCount(2).And.OnlyContain(r => r.EstimatedGrams != null);
        review.Preview.Plates[1].FilamentRequirements.Should().HaveCount(3).And.OnlyContain(r => r.EstimatedGrams != null);
        var response = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{submitted.JobId}/confirm",
            new ConfirmPartProcessingJobRequest { Name = name, PlateNumbers = [1, 2] });
        response.EnsureSuccessStatusCode();

        Guid pikachuId, generalId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var parts = await db.Parts.Where(p => p.Name.StartsWith(name)).OrderBy(p => p.PlateNumber).ToListAsync();
            parts.Should().HaveCount(2);
            pikachuId = parts[0].Id;
            generalId = parts[1].Id;
        }
        var assemblyResponse = await client.PostAsJsonAsync("/api/v1/assemblies", new
        {
            name = $"Labels assembly {Guid.NewGuid():N}",
            lines = new[] { new { targetKind = "Part", targetId = pikachuId, quantity = 2 }, new { targetKind = "Part", targetId = generalId, quantity = 1 } },
        });
        assemblyResponse.EnsureSuccessStatusCode();
        using var assembly = System.Text.Json.JsonDocument.Parse(await assemblyResponse.Content.ReadAsStringAsync());
        var assemblyId = assembly.RootElement.GetProperty("id").GetGuid();
        await AssertQuoteAsync();

        // Reproduce saved rows from the old detector, without changing the stored file or part identity.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            db.Set<PartFilamentRequirement>().Add(PartFilamentRequirement.Create(pikachuId, new("PLA", "#CE0000", null, null, null)));
            db.Set<PartFilamentRequirement>().Add(PartFilamentRequirement.Create(generalId, new("PLA", "#FFCC3C", null, null, null)));
            await db.SaveChangesAsync();
        }
        foreach (var (id, plate, count) in new[] { (pikachuId, 1, 2), (generalId, 2, 3) })
        {
            var before = (await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{id}"))!;
            before.FilamentRequirements.Should().HaveCount(count + 1);
            var reprocess = await client.PostAsJsonAsync($"/api/v1/parts/{id}/reprocess", new { });
            reprocess.EnsureSuccessStatusCode();
            var job = (await reprocess.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>())!;
            await WaitAsync(client, job.JobId);
            var confirmed = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{job.JobId}/confirm", new { });
            confirmed.EnsureSuccessStatusCode();
            var after = (await confirmed.Content.ReadFromJsonAsync<PartResponse>())!;
            after.Id.Should().Be(id);
            after.Name.Should().Be(before.Name);
            after.PlateNumber.Should().Be(plate);
            after.OriginalFileName.Should().Be(before.OriginalFileName);
            after.UnitsPerPrint.Should().Be(before.UnitsPerPrint);
            after.FilamentRequirements.Should().HaveCount(count).And.OnlyContain(r => r.EstimatedGrams != null);
        }
        await AssertQuoteAsync();

        async Task AssertQuoteAsync()
        {
            var result = await client.PostAsJsonAsync("/api/v1/assemblies/quote", new { assemblyId });
            result.EnsureSuccessStatusCode();
            var quote = (await result.Content.ReadFromJsonAsync<AssemblyQuote>())!;
            quote.Colors.Should().HaveCount(4).And.OnlyContain(c => c.Name != "Unknown color" && c.Grams != null && c.Missing == 0);
            quote.Colors.Single(c => c.ColorHex == "#FFCC3C").Grams.Should().Be(1.36m);
            quote.Colors.Single(c => c.ColorHex == "#000000").Grams.Should().Be(8.15m);
            quote.Colors.Single(c => c.ColorHex == "#CE0000").Grams.Should().Be(0.87m);
        }
    }
}
