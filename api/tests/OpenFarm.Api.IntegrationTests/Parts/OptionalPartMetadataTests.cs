using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.PartProcessingJobs;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Parts;

public class OptionalPartMetadataTests(EmptySlicerBundleWebApplicationFactory factory) : IClassFixture<EmptySlicerBundleWebApplicationFactory>
{
    [Fact]
    public async Task UploadConfirmReprocessAndProduction_WorkWithoutPrinterProfiles()
    {
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        var credentials = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials!.Token);

        var files = new (string Name, byte[] Bytes)[]
        {
            ("part.stl", Encoding.UTF8.GetBytes("solid test\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid test")),
            ("part.gcode", Encoding.UTF8.GetBytes("G28\nG1 X10 Y10")),
            ("part.3mf", Zip(("3D/3dmodel.model", "<model><resources/><build/></model>"), ("Metadata/slice_info.config", "<config/>"))),
            ("part.gcode.3mf", Zip(("Metadata/plate_1.gcode", "G28\nG1 X10 Y10"))),
        };
        PartResponse? saved = null;
        foreach (var linkedPrinterExists in new[] { false, true })
        {
            if (linkedPrinterExists)
            {
                var printer = await client.PostAsJsonAsync("/api/v1/printers", new RegisterPrinterRequest
                {
                    Name = "Unlinked",
                    Brand = "System",
                    ModelCode = "Simulation",
                    IpAddress = "10.0.0.90",
                    SerialNumber = $"OPTIONAL-{Guid.NewGuid():N}",
                    AccessCode = "12345678",
                });
                printer.IsSuccessStatusCode.Should().BeTrue();
            }
            foreach (var file in files)
            {
                var submitted = await SubmitAsync(client, "/api/v1/parts", file.Name, file.Bytes);
                var ready = await WaitAsync(client, submitted.JobId);
                ready.Status.Should().Be("ReadyForReview");
                ready.Preview!.NozzleDiameterMm.Should().BeNull();
                ready.Preview.EstimateStatus.Should().Be("Unavailable");
                ready.Preview.PrintRequirementsStatus.Should().Be("Incomplete");
                var confirmation = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{submitted.JobId}/confirm",
                    new ConfirmPartProcessingJobRequest { Name = file.Name });
                confirmation.StatusCode.Should().Be(HttpStatusCode.Created);
                saved = await confirmation.Content.ReadFromJsonAsync<PartResponse>();
                saved!.EstimatedPrintTimeMinutes.Should().BeNull();
                saved.EstimatedFilamentWeightGrams.Should().BeNull();
                saved.PrintRequirementsStatus.Should().Be("Incomplete");
            }
        }

        var partialUpload = await SubmitAsync(client, "/api/v1/parts", "partial.gcode",
            Encoding.UTF8.GetBytes("; nozzle_diameter = 0.4\n; filament_type = PLA,\n; total filament weight [g] = 4\nG28"));
        await WaitAsync(client, partialUpload.JobId);
        var partialConfirm = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{partialUpload.JobId}/confirm",
            new ConfirmPartProcessingJobRequest { Name = "Partial material slots" });
        partialConfirm.IsSuccessStatusCode.Should().BeTrue();
        var partial = await partialConfirm.Content.ReadFromJsonAsync<PartResponse>();
        var persistedPartial = await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{partial!.Id}");
        persistedPartial!.NozzleDiameterMm.Should().Be(0.4m);
        persistedPartial.FilamentRequirements.Should().ContainSingle();
        persistedPartial.PrintRequirementsStatus.Should().Be("Incomplete");
        persistedPartial.EstimateStatus.Should().Be("Partial");
        persistedPartial.EstimatedFilamentWeightGrams.Should().Be(4m);

        var blockedRun = await client.PostAsJsonAsync("/api/v1/production-runs", RunRequest(saved!.Id));
        blockedRun.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var runs = await client.GetStringAsync("/api/v1/production-runs");
        runs.Should().NotContain("Optional metadata run");

        var start = await client.PostAsJsonAsync($"/api/v1/parts/{saved.Id}/reprocess", new { });
        start.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var retry = await start.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();
        (await WaitAsync(client, retry!.JobId)).Kind.Should().Be("Reprocess");
        var duplicate = await client.PostAsJsonAsync($"/api/v1/parts/{saved.Id}/reprocess", new { });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var confirmedRetry = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{retry.JobId}/confirm", new { });
        confirmedRetry.IsSuccessStatusCode.Should().BeTrue();
        (await confirmedRetry.Content.ReadFromJsonAsync<PartResponse>())!.Id.Should().Be(saved.Id);

        var staleStart = await client.PostAsJsonAsync($"/api/v1/parts/{saved.Id}/reprocess", new { });
        var stale = await staleStart.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();
        await WaitAsync(client, stale!.JobId);
        var corrected = await SubmitAsync(client, $"/api/v1/parts/{saved.Id}/file", "fixed.gcode",
            Encoding.UTF8.GetBytes("; nozzle_diameter = 0.4\n; filament_type = PLA\nG28"), replace: true);
        await WaitAsync(client, corrected.JobId);
        var replacement = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{corrected.JobId}/confirm", new { });
        replacement.IsSuccessStatusCode.Should().BeTrue();
        var complete = await replacement.Content.ReadFromJsonAsync<PartResponse>();
        complete!.PrintRequirementsStatus.Should().Be("Complete");
        complete.EstimateStatus.Should().Be("Unavailable");
        var staleConfirm = await client.PostAsJsonAsync($"/api/v1/part-processing-jobs/{stale.JobId}/confirm", new { });
        staleConfirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var discard = await client.DeleteAsync($"/api/v1/part-processing-jobs/{stale.JobId}/confirm");
        discard.IsSuccessStatusCode.Should().BeTrue();
        var allowedRun = await client.PostAsJsonAsync("/api/v1/production-runs", RunRequest(saved.Id));
        allowedRun.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static CreateProductionRunRequest RunRequest(Guid partId) => new()
    {
        Name = "Optional metadata run",
        Lines = [new CreateRunLineRequest { TargetKind = "Part", TargetId = partId, Quantity = 1 }],
    };

    private static async Task<PartProcessingJobSubmittedResponse> SubmitAsync(HttpClient client, string url, string name, byte[] bytes, bool replace = false)
    {
        using var upload = new MultipartFormDataContent { { new ByteArrayContent(bytes), "File", name } };
        var response = replace ? await client.PutAsync(url, upload) : await client.PostAsync(url, upload);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>())!;
    }

    private static async Task<PartProcessingJobResponse> WaitAsync(HttpClient client, Guid jobId)
    {
        for (var i = 0; i < 100; i++)
        {
            var job = await client.GetFromJsonAsync<PartProcessingJobResponse>($"/api/v1/part-processing-jobs/{jobId}");
            if (job!.Status != "Processing" && job.Preview?.EstimateStatus != "Resolving") return job;
            await Task.Delay(50);
        }
        throw new TimeoutException("Part processing did not settle.");
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open());
                writer.Write(entry.Text);
            }
        return buffer.ToArray();
    }
}
