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
        return new SlicingResult(42, 12m, [], []);
    }
    public Task<SlicingResult> SliceConfiguredAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat, string materialType,
        decimal nozzleDiameterMm, Guid printerId, ResolvedSlicingProfiles profiles, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Library estimates must not call printer-targeted slicing.");
}

public sealed class FileBasedEstimationTests(EstimationTestFactory factory) : IClassFixture<EstimationTestFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly byte[] Stl = Encoding.UTF8.GetBytes("solid t\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid t");

    [Fact]
    public async Task GeometryFiles_RemainSaveableWithoutEstimates()
    {
        var client = await AuthenticatedClientAsync(_client);
        foreach (var (bytes, name) in new[] { (Stl, "part.stl"), (ThreeMf(null), "geometry.3mf") })
        {
            var preview = await UploadAsync(client, bytes, name);
            preview.Preview!.EstimateStatus.Should().Be("Unavailable");
            preview.Preview.NozzleDiameterMm.Should().BeNull();
            preview.Preview.EstimateUnavailableReason.Should().Contain("embedded slicing settings");
            var confirmed = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{preview.Id}/confirm", new { name });
            confirmed.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task EmbeddedProject_PreservesCustomSettings_AndIncompleteProjectsNeverUseDefaults()
    {
        var client = await AuthenticatedClientAsync(_client);
        (await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.2.0" })).EnsureSuccessStatusCode();
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
