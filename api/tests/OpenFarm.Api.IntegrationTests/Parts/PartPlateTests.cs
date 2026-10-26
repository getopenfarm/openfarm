using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.PartProcessingJobs;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Persistence;
using static OpenFarm.Api.IntegrationTests.Parts.OptionalPartMetadataTests;

namespace OpenFarm.Api.IntegrationTests.Parts;

// A part prints one plate of its file: the review offers the file's plates, and the part takes the chosen plate's data.
public class PartPlateTests(EmptySlicerBundleWebApplicationFactory factory) : IClassFixture<EmptySlicerBundleWebApplicationFactory>
{
    private static string Plate(string material, string colour, int minutes, decimal grams) =>
        $"; nozzle_diameter = 0.4\n; filament_type = {material}\n; filament_colour = {colour}\n" +
        $"; total estimated time = {minutes}m\n; total filament weight [g] = {grams}\nG28\nG1 X10 Y10\n";

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        var credentials = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials!.Token);
        return client;
    }

    [Fact]
    public async Task AnExportedSinglePlate_BecomesAPartPrintingThatPlate()
    {
        using var client = await ClientAsync();
        // "Export plate sliced file" of plate 4: model_settings lists every plate, only plate 4 has G-code.
        var file = Zip(
            ("Metadata/plate_4.gcode", Plate("PETG", "#000000", 55, 20.14m)),
            ("Metadata/plate_4.png", "thumbnail"),
            ("Metadata/model_settings.config", """
                <config>
                  <plate><metadata key="plater_id" value="1"/><metadata key="plater_name" value="Cover"/></plate>
                  <plate><metadata key="plater_id" value="4"/><metadata key="plater_name" value="Clip"/></plate>
                </config>
                """));

        var submitted = await SubmitAsync(client, "/api/v1/parts", "C0002.gcode.3mf", file);
        var ready = await WaitAsync(client, submitted.JobId);

        ready.Preview!.Plates.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Number = 4, Name = "Clip", EstimatedPrintTimeMinutes = 55 });
        var confirm = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{submitted.JobId}/confirm",
            new ConfirmPartProcessingJobRequest { Name = "Clip" });
        confirm.StatusCode.Should().Be(HttpStatusCode.Created);
        var part = await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{(await confirm.Content.ReadFromJsonAsync<PartResponse>())!.Id}");
        part!.PlateNumber.Should().Be(4);
        part.PlateName.Should().Be("Clip");
        part.EstimatedPrintTimeMinutes.Should().Be(55);
        part.EstimatedFilamentWeightGrams.Should().Be(20.14m);
        part.Thumbnails.Should().ContainSingle().Which.PlateIndex.Should().Be(3);
    }

    [Fact]
    public async Task ASeveralPlateFile_NeedsAPlate_AndThePartTakesOnlyThatPlate()
    {
        using var client = await ClientAsync();
        var file = Zip(
            ("Metadata/plate_1.gcode", Plate("PLA", "#FFFFFF", 10, 4m)),
            ("Metadata/plate_2.gcode", Plate("PETG", "#000000", 30, 9m)));
        var submitted = await SubmitAsync(client, "/api/v1/parts", "kit.gcode.3mf", file);
        var ready = await WaitAsync(client, submitted.JobId);
        ready.Preview!.Plates.Select(p => p.Number).Should().Equal(1, 2);
        var url = $"/api/v1/part-processing-jobs/{submitted.JobId}/confirm";

        (await client.PostAsJsonAsync(url, new ConfirmPartProcessingJobRequest { Name = "Kit" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(url, new ConfirmPartProcessingJobRequest { Name = "Kit", PlateNumbers = [3] }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var confirm = await client.PostAsJsonAsync(url, new ConfirmPartProcessingJobRequest { Name = "Kit lid", PlateNumbers = [2] });

        confirm.StatusCode.Should().Be(HttpStatusCode.Created);
        var part = await confirm.Content.ReadFromJsonAsync<PartResponse>();
        part!.PlateNumber.Should().Be(2);
        part.EstimatedPrintTimeMinutes.Should().Be(30);
        part.EstimatedFilamentWeightGrams.Should().Be(9m);
        part.FilamentRequirements.Should().ContainSingle().Which.MaterialType.Should().Be("PETG");

        // Reprocessing keeps the part's plate without asking again.
        var reprocess = await client.PostAsJsonAsync($"/api/v1/parts/{part.Id}/reprocess", new { });
        var job = await reprocess.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();
        await WaitAsync(client, job!.JobId);
        var reprocessed = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{job.JobId}/confirm", new { });
        reprocessed.IsSuccessStatusCode.Should().BeTrue(await reprocessed.Content.ReadAsStringAsync());
        (await reprocessed.Content.ReadFromJsonAsync<PartResponse>())!.PlateNumber.Should().Be(2);
    }

    [Fact]
    public async Task AddingSeveralPlates_MakesOnePartEach_AndAKitAssemblyOfThem()
    {
        using var client = await ClientAsync();
        // Plate 1 holds two copies, plate 2 one: a kit is one print of each, so its lines are 2 and 1.
        var file = Zip(
            ("Metadata/plate_1.gcode", Plate("PLA", "#FFFFFF", 10, 4m)),
            ("Metadata/plate_2.gcode", Plate("PETG", "#000000", 30, 9m)),
            ("Metadata/model_settings.config", """
                <config>
                  <plate><metadata key="plater_id" value="1"/><metadata key="plater_name" value="Cover"/><model_instance/><model_instance/></plate>
                  <plate><metadata key="plater_id" value="2"/><model_instance/></plate>
                </config>
                """));
        var name = $"Kit {Guid.NewGuid():N}";
        var submitted = await SubmitAsync(client, "/api/v1/parts", "kit.gcode.3mf", file);
        await WaitAsync(client, submitted.JobId);

        var confirm = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{submitted.JobId}/confirm",
            new ConfirmPartProcessingJobRequest { Name = name, PlateNumbers = [1, 2], CreateAssembly = true });

        confirm.StatusCode.Should().Be(HttpStatusCode.Created, await confirm.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var parts = await db.Parts.Where(p => p.Name.StartsWith(name)).OrderBy(p => p.PlateNumber).ToListAsync();
        parts.Select(p => (p.Name, p.PlateNumber, p.UnitsPerPrint, p.EstimatedPrintTimeMinutes))
            .Should().Equal(($"{name} – Cover", 1, 2, 10), ($"{name} – Plate 2", 2, 1, 30));
        parts.Select(p => p.StoragePath).Distinct().Should().ContainSingle("the parts share the uploaded file");
        var assemblyId = await db.Assemblies.Where(a => a.Name == name).Select(a => a.Id).SingleAsync();
        var assembly = await client.GetFromJsonAsync<AssemblyResponse>($"/api/v1/assemblies/{assemblyId}");
        assembly!.Lines.Select(l => (l.TargetId, l.Quantity)).Should().BeEquivalentTo([(parts[0].Id, 2m), (parts[1].Id, 1m)]);
        var logged = await db.ActivityLogEntries.Where(e => e.EntityId == parts[1].Id.ToString() || e.EntityId == assemblyId.ToString())
            .Select(e => e.EventType).ToListAsync();
        logged.Should().BeEquivalentTo(["PartConfirmed", "AssemblyCreated"]);

        // The kit holds its parts: one cannot be removed from under it.
        (await client.DeleteAsync($"/api/v1/parts/{parts[0].Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SeveralPlates_AreRefusedOnReplace()
    {
        using var client = await ClientAsync();
        var file = Zip(("Metadata/plate_1.gcode", Plate("PLA", "#FFFFFF", 10, 4m)), ("Metadata/plate_2.gcode", Plate("PLA", "#FFFFFF", 10, 4m)));
        var added = await SubmitAsync(client, "/api/v1/parts", "a.gcode.3mf", file);
        await WaitAsync(client, added.JobId);
        var part = await (await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{added.JobId}/confirm",
            new ConfirmPartProcessingJobRequest { Name = "A", PlateNumbers = [1] })).Content.ReadFromJsonAsync<PartResponse>();
        var replaced = await SubmitAsync(client, $"/api/v1/parts/{part!.Id}/file", "b.gcode.3mf", file, replace: true);
        await WaitAsync(client, replaced.JobId);

        var confirm = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{replaced.JobId}/confirm",
            new ConfirmPartProcessingJobRequest { PlateNumbers = [1, 2] });

        confirm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
