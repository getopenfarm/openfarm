using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Endpoints.PartProcessingJobs;
using OpenFarm.Api.Endpoints.Settings;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Organizations;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.Parts;

public sealed class EstimationTestFactory : CustomWebApplicationFactory
{
    public RecordingEstimator Estimator { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<ISlicerEngine>(Estimator)));
    }
}

public sealed class RecordingEstimator : ISlicerEngine
{
    public ConcurrentQueue<EstimationConfiguration> Calls { get; } = new();
    public TaskCompletionSource? Gate { get; set; }
    public async Task<SlicingResult> EstimateAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
        EstimationConfiguration configuration, CancellationToken cancellationToken)
    {
        Calls.Enqueue(configuration);
        if (Gate is { } gate) await gate.Task.WaitAsync(cancellationToken);
        return new SlicingResult(1, 42, 12m, [], []);
    }
    public Task<SlicingResult> SliceAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat, string materialType,
        decimal nozzleDiameterMm, Guid? printerId, CancellationToken cancellationToken) => throw new InvalidOperationException("Library estimates must not call printer-targeted slicing.");
}

public sealed class FileBasedEstimationTests(EstimationTestFactory factory) : IClassFixture<EstimationTestFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly byte[] Stl = Encoding.UTF8.GetBytes("solid t\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid t");

    [Fact]
    public async Task Defaults_AreExplicit_Persisted_IndependentOfFleet_AndUsedByReprocessing()
    {
        var client = await AuthenticatedClientAsync(_client);
        var profiles = await SeedProfilesAsync();
        var put = await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new
        { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = profiles });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadFromJsonAsync<SlicingDefaultsResponse>())!.EstimationProfiles!.MachineProfileId.Should().Be(profiles.MachineProfileId);
        var preview = await UploadAsync(client, Stl, "part.stl");
        preview.Preview!.NozzleDiameterMm.Should().Be(0.6m);
        preview.Preview.EstimatedPrintTimeMinutes.Should().Be(42);
        preview.Preview.FilamentRequirements.Should().ContainSingle(r => r.MaterialType == null);
        preview.Preview.Estimation!.Source.Should().Be("DefaultProfiles");
        factory.Estimator.Calls.Last().Profiles!.ProcessSettingsJson.Should().Contain("0.3");
        // Engine-only updates preserve the profile tuple.
        (await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0" })).EnsureSuccessStatusCode();
        var defaults = await client.GetFromJsonAsync<SlicingDefaultsResponse>("/api/v1/settings/slicing-defaults");
        defaults!.EstimationProfiles!.MachineProfileId.Should().Be(profiles.MachineProfileId);
        await factory.EnsureTenantInfoResolvedAsync();
        using (var scope = factory.CreateTenantScope())
        {
            var printer = Printer.Create(Organization.WellKnownId, "Different fleet machine", "Bambu", "P1S", "10.0.0.88", "OTHER-ESTIMATE");
            await scope.ServiceProvider.GetRequiredService<IPrinterRepository>().AddAsync(printer, CancellationToken.None);
        }
        var geometry = await UploadAsync(client, ThreeMf(null), "geometry.3mf");
        geometry.Preview!.NozzleDiameterMm.Should().Be(0.6m);
        geometry.Preview.Estimation!.Source.Should().Be("DefaultProfiles");
        var confirmed = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{preview.Id}/confirm", new { name = "Estimated STL" });
        confirmed.EnsureSuccessStatusCode();
        var part = (await confirmed.Content.ReadFromJsonAsync<PartResponse>())!;
        part.Estimation!.Source.Should().Be("DefaultProfiles");
        (await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = (object?)null })).EnsureSuccessStatusCode();
        var retry = await client.PostAsJsonAsync($"/api/v1/parts/{part.Id}/reprocess", new { });
        retry.EnsureSuccessStatusCode();
        var submitted = (await retry.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>())!;
        var job = await WaitAsync(client, submitted.JobId);
        job.Preview!.EstimateStatus.Should().Be("Unavailable");
        job.Preview.NozzleDiameterMm.Should().BeNull();
        (await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{part.Id}"))!.EstimatedPrintTimeMinutes.Should().Be(42);
    }

    [Fact]
    public async Task EmbeddedProject_PreservesCustomSettings_AndIncompleteProjectsNeverUseDefaults()
    {
        var client = await AuthenticatedClientAsync(_client);
        var defaults = await SeedProfilesAsync();
        (await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = defaults })).EnsureSuccessStatusCode();
        const string config = """{"printer_model":"Bambu Lab P1S","printer_settings_id":"Custom Scrubbler","nozzle_diameter":["0.4"],"filament_type":["PETG"],"filament_settings_id":["Custom PETG"],"print_settings_id":"Custom process","curr_bed_type":"Textured PEI Plate","layer_height":"0.3"}""";
        var project = await UploadAsync(client, ThreeMf(config), "custom.3mf");
        project.Preview!.NozzleDiameterMm.Should().Be(0.4m);
        project.Preview.Estimation!.MachineProfileName.Should().Be("Custom Scrubbler");
        factory.Estimator.Calls.Last().EmbeddedProject.Should().BeTrue();
        factory.Estimator.Calls.Last().Profiles.Should().BeNull();
        var count = factory.Estimator.Calls.Count;
        var partial = await UploadAsync(client, ThreeMf("""{"filament_type":["PETG"]}"""), "partial.3mf");
        partial.Status.Should().Be("ReadyForReview");
        partial.Preview!.EstimateStatus.Should().Be("Unavailable");
        partial.Preview.NozzleDiameterMm.Should().BeNull();
        partial.Preview.Estimation!.Source.Should().Be("EmbeddedProject");
        factory.Estimator.Calls.Count.Should().Be(count);
        var malformed = await UploadAsync(client, ThreeMf("invalid json"), "malformed.3mf");
        malformed.Preview!.EstimateStatus.Should().Be("Unavailable");
        malformed.Preview.Estimation!.Source.Should().Be("EmbeddedProject");
        (await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = (object?)null })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SettingsRejectIncompleteMissingAndIncompatibleProfilesWithoutChangingSavedDefaults()
    {
        var client = await AuthenticatedClientAsync(_client);
        var profiles = await SeedProfilesAsync();
        (await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = profiles })).EnsureSuccessStatusCode();
        var missing = await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = new { machineProfileId = profiles.MachineProfileId } });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalid = await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = profiles with { FilamentProfileId = Guid.NewGuid() } });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var wrongEngine = await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "SuperSlicer", version = "2.5.59.13" });
        wrongEngine.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.GetFromJsonAsync<SlicingDefaultsResponse>("/api/v1/settings/slicing-defaults"))!.Default!.Engine.Should().Be("OrcaSlicer");
        (await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = (object?)null })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task InFlightEstimateUsesItsSnapshotAfterDefaultsAreCleared()
    {
        var client = await AuthenticatedClientAsync(_client);
        var profiles = await SeedProfilesAsync();
        (await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = profiles })).EnsureSuccessStatusCode();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Estimator.Gate = gate;
        try
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent(Stl), "File", "snapshot.stl" } };
            var upload = await client.PostAsync("/api/v1/parts", form);
            upload.EnsureSuccessStatusCode();
            var id = (await upload.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>())!.JobId;
            PartProcessingJobResponse? ready = null;
            for (var i = 0; i < 100; i++)
            {
                ready = await client.GetFromJsonAsync<PartProcessingJobResponse>($"/api/v1/part-processing-jobs/{id}");
                if (ready!.Status == "ReadyForReview") break;
                await Task.Delay(50);
            }
            ready!.Preview!.EstimateStatus.Should().Be("Resolving");
            (await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0", estimationProfiles = (object?)null })).EnsureSuccessStatusCode();
            gate.SetResult();
            var completed = await WaitAsync(client, id);
            completed.Preview!.NozzleDiameterMm.Should().Be(0.6m);
            completed.Preview.EstimatedPrintTimeMinutes.Should().Be(42);
            completed.Preview.Estimation!.Source.Should().Be("DefaultProfiles");
            factory.Estimator.Calls.Last().Profiles!.ProcessSettingsJson.Should().Contain("0.3");
        }
        finally { gate.TrySetResult(); factory.Estimator.Gate = null; }
    }

    private sealed record Selection(Guid MachineProfileId, Guid ProcessProfileId, Guid FilamentProfileId, string BuildPlateType);
    private async Task<Selection> SeedProfilesAsync()
    {
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var name = $"Estimation machine {Guid.NewGuid():N}";
        var machine = MachineProfile.CreateSystemProfile(Organization.WellKnownId, name, ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"machine","nozzle_diameter":["0.6"],"printer_model":"Bambu Lab P1S"}""", "Test", Guid.NewGuid().ToString(), true, "Bambu Lab P1S", null);
        var process = ProcessProfile.CreateSystemProfile(Organization.WellKnownId, $"Estimate process {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"process","layer_height":"0.3"}""", "Test", Guid.NewGuid().ToString(), true, [name]);
        var filament = FilamentProfile.CreateSystemProfile(Organization.WellKnownId, $"Estimate PETG {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PETG"],"textured_plate_temp":["70"]}""", "Test", Guid.NewGuid().ToString(), true, [name], "PETG", null);
        await scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>().AddAsync(machine, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>().AddAsync(process, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>().AddAsync(filament, CancellationToken.None);
        return new(machine.Id, process.Id, filament.Id, "TexturedPei");
    }

    private static async Task<PartProcessingJobResponse> UploadAsync(HttpClient client, byte[] bytes, string name)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "File", name } };
        var response = await client.PostAsync("/api/v1/parts", form);
        response.EnsureSuccessStatusCode();
        return await WaitAsync(client, (await response.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>())!.JobId);
    }
    private static async Task<PartProcessingJobResponse> WaitAsync(HttpClient client, Guid id)
    {
        for (var i = 0; i < 100; i++)
        {
            var result = await client.GetFromJsonAsync<PartProcessingJobResponse>($"/api/v1/part-processing-jobs/{id}");
            if (result!.Status != "Processing" && result.Preview?.EstimateStatus != "Resolving") return result;
            await Task.Delay(50);
        }
        throw new TimeoutException();
    }
    private static byte[] ThreeMf(string? config)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("3D/3dmodel.model").Open()))
                writer.Write("<model><resources/><build/></model>");
            if (config is not null) { using var writer = new StreamWriter(zip.CreateEntry("Metadata/project_settings.config").Open()); writer.Write(config); }
        }
        return output.ToArray();
    }
}
