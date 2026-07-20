using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.PartProcessingJobs;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;
using OpenFarm.Api.IntegrationTests.Profiles;

namespace OpenFarm.Api.IntegrationTests.Parts;

/// <summary>
/// Regression coverage for a real-DI-container bug that no hand-built test double could catch:
/// <c>IPartProcessingDispatcher</c> was registered <c>AddScoped</c> with a factory returning the
/// singleton <c>PartProcessingBackgroundService</c> (an <c>IDisposable</c> via its
/// <c>BackgroundService</c> base). The container doesn't recognize a scoped factory's returned
/// instance as externally owned, so it added that singleton to the disposables of whichever HTTP
/// request scope first resolved it — disposing it (and cancelling its <c>stoppingToken</c>,
/// permanently killing every worker loop) at the end of that very request. Every part uploaded
/// afterward — including the one that triggered it — sat "Processing" forever with no exception
/// anywhere. Only exercisable through the app's actual composition root (Program.cs), hence an
/// integration test against the real DI graph rather than a unit test with a hand-rolled container.
/// Requires Docker (Testcontainers.PostgreSql) — not runnable in every sandbox, but exercised by CI.
/// </summary>
public class CreatePartEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin@openfarm.local", "ChangeMe123!"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return _client;
    }

    /// <summary>Registers a printer, imports a machine profile with a known nozzle diameter, and links them — the minimum <see cref="OpenFarm.Application.Parts.NozzleDiameterResolver"/> needs before a part upload will even create a job.</summary>
    private static async Task RegisterPrinterWithNozzleDiameterAsync(HttpClient client, string serialNumber)
    {
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/printers", UriKind.Relative),
            new RegisterPrinterRequest
            {
                Name = $"Part Upload Test Printer {serialNumber}",
                Brand = "System",
                ModelCode = "Simulation",
                IpAddress = "10.0.0.80",
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>();

        var profileName = $"Part Upload Test Machine {serialNumber}";
        var bundle = ProfileBundleTestHelper.BuildZip(
            ($"machine/{profileName}.json", $$"""{"name":"{{profileName}}","nozzle_diameter":["0.4"]}"""));
        var selections = JsonSerializer.Serialize(new object[] { new { type = "Machine", name = profileName, duplicateAction = (string?)null } });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        var created = await confirmResponse.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        var machineProfileId = created!.CreatedMachineProfiles.Single().Id;

        await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{printer!.Id}/machine-profile", UriKind.Relative),
            new LinkPrinterMachineProfileRequest { MachineProfileId = machineProfileId });
    }

    private static async Task<PartProcessingJobResponse> WaitForTerminalStatusAsync(HttpClient client, Guid jobId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var response = await client.GetAsync(new Uri($"/api/v1/part-processing-jobs/{jobId}", UriKind.Relative));
            var job = await response.Content.ReadFromJsonAsync<PartProcessingJobResponse>();
            if (job!.Status != "Processing")
            {
                return job;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Part processing job {jobId} never left the Processing status.");
    }

    [Fact]
    public async Task CreatePart_UploadedStl_NeverSitsProcessingForever()
    {
        var client = await AuthenticatedClientAsync();
        await RegisterPrinterWithNozzleDiameterAsync(client, $"SN-{Guid.NewGuid():N}");

        using var fileContent = new ByteArrayContent([0x73, 0x6f, 0x6c, 0x69, 0x64, 0x20, 0x74, 0x65, 0x73, 0x74]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadContent = new MultipartFormDataContent { { fileContent, "File", "benchy.stl" } };

        var uploadResponse = await client.PostAsync(new Uri("/api/v1/parts", UriKind.Relative), uploadContent);
        var submitted = await uploadResponse.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();

        var finalJob = await WaitForTerminalStatusAsync(client, submitted!.JobId);

        // The specific outcome depends on whether the CI/dev host has a slicer CLI installed —
        // what this test actually guards is that it reaches SOME terminal state at all instead of
        // sitting "Processing" forever (WaitForTerminalStatusAsync already throws on timeout, but
        // assert explicitly too so a future regression fails with a clear message here).
        finalJob.Status.Should().NotBe("Processing");
        finalJob.Status.Should().BeOneOf("ReadyForReview", "Failed");
    }
}
