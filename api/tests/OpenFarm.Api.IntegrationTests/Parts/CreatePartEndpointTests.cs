using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.PartProcessingJobs;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Api.Endpoints.Settings;
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
            new LoginCommand("admin", "admin-openfarm"));
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

        await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer!.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, MachineProfileId = machineProfileId });
    }

    private static async Task<PartProcessingJobResponse> WaitForTerminalStatusAsync(HttpClient client, Guid jobId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var response = await client.GetAsync(new Uri($"/api/v1/part-processing-jobs/{jobId}", UriKind.Relative));
            var job = await response.Content.ReadFromJsonAsync<PartProcessingJobResponse>();

            // "ReadyForReview" with EstimateStatus "Resolving" is not yet terminal either — the
            // trailing async slice continuation hasn't settled, and can still flip Status to
            // Failed. Stopping here (as this helper used to) raced that continuation and
            // intermittently returned "ReadyForReview" a moment before it turned into "Failed".
            var stillSettling = job!.Status == "Processing"
                || (job.Status == "ReadyForReview" && job.Preview?.EstimateStatus == "Resolving");

            if (!stillSettling)
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

        // The specific outcome depends on whether the dummy bundle's stub binary can actually
        // execute — what this test actually guards is that it reaches SOME terminal state at all
        // instead of sitting "Processing" forever (WaitForTerminalStatusAsync already throws on
        // timeout, but assert explicitly too so a future regression fails with a clear message here).
        finalJob.Status.Should().NotBe("Processing");
        finalJob.Status.Should().BeOneOf("ReadyForReview", "Failed");
    }

    [Fact]
    public async Task CreatePart_BundledBinaryIsBroken_FailsAtSliceTimeInsteadOfHanging()
    {
        // The test factory's dummy bundle's AppRun is a non-executable stub — the slice can never
        // succeed, so this reaches Failed deterministically (Constitution V: never a hang).
        var client = await AuthenticatedClientAsync();
        await RegisterPrinterWithNozzleDiameterAsync(client, $"SN-{Guid.NewGuid():N}");

        using var fileContent = new ByteArrayContent([0x73, 0x6f, 0x6c, 0x69, 0x64, 0x20, 0x74, 0x65, 0x73, 0x74]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadContent = new MultipartFormDataContent { { fileContent, "File", "benchy.stl" } };

        var uploadResponse = await client.PostAsync(new Uri("/api/v1/parts", UriKind.Relative), uploadContent);
        var submitted = await uploadResponse.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();

        var finalJob = await WaitForTerminalStatusAsync(client, submitted!.JobId);

        finalJob.Status.Should().Be("Failed");
        finalJob.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreatePart_OneDiscoveredEngineVersion_ResolvesTheResolvedSlicerOntoTheJob()
    {
        var client = await AuthenticatedClientAsync();
        await RegisterPrinterWithNozzleDiameterAsync(client, $"SN-{Guid.NewGuid():N}");

        using var fileContent = new ByteArrayContent([0x73, 0x6f, 0x6c, 0x69, 0x64, 0x20, 0x74, 0x65, 0x73, 0x74]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadContent = new MultipartFormDataContent { { fileContent, "File", "benchy.stl" } };

        var uploadResponse = await client.PostAsync(new Uri("/api/v1/parts", UriKind.Relative), uploadContent);
        var submitted = await uploadResponse.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();

        var jobResponse = await client.GetAsync(new Uri($"/api/v1/part-processing-jobs/{submitted!.JobId}", UriKind.Relative));
        var job = await jobResponse.Content.ReadFromJsonAsync<PartProcessingJobResponse>();

        // The test factory's dummy bundle bundles OrcaSlicer 2.2.0 and SuperSlicer 2.5.59.13 —
        // OrcaSlicer is preferred when no explicit default is set (research.md §5).
        job!.Slicer.Should().NotBeNull();
        job.Slicer!.Engine.Should().Be("OrcaSlicer");
        job.Slicer.Version.Should().Be("2.2.0");
    }

    [Fact]
    public async Task CreatePart_ExplicitEngineAndVersionOverride_UsesThatExactPairAndLeavesDefaultUnchanged()
    {
        var client = await AuthenticatedClientAsync();
        await RegisterPrinterWithNozzleDiameterAsync(client, $"SN-{Guid.NewGuid():N}");

        using var fileContent = new ByteArrayContent([0x73, 0x6f, 0x6c, 0x69, 0x64, 0x20, 0x74, 0x65, 0x73, 0x74]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadContent = new MultipartFormDataContent
        {
            { fileContent, "File", "benchy.stl" },
            { new StringContent("SuperSlicer"), "SlicerEngine" },
            { new StringContent("2.5.59.13"), "SlicerVersion" },
        };

        var uploadResponse = await client.PostAsync(new Uri("/api/v1/parts", UriKind.Relative), uploadContent);
        var submitted = await uploadResponse.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();

        var jobResponse = await client.GetAsync(new Uri($"/api/v1/part-processing-jobs/{submitted!.JobId}", UriKind.Relative));
        var job = await jobResponse.Content.ReadFromJsonAsync<PartProcessingJobResponse>();

        job!.Slicer!.Engine.Should().Be("SuperSlicer");
        job.Slicer.Version.Should().Be("2.5.59.13");

        var defaultsResponse = await client.GetAsync(new Uri("/api/v1/settings/slicing-defaults", UriKind.Relative));
        var defaults = await defaultsResponse.Content.ReadFromJsonAsync<SlicingDefaultsResponse>();
        defaults!.Default!.Engine.Should().Be("OrcaSlicer");
    }

    [Fact]
    public async Task CreatePart_StalePairNotInBundle_Returns422BeforeAnyProcessingStarts()
    {
        var client = await AuthenticatedClientAsync();
        await RegisterPrinterWithNozzleDiameterAsync(client, $"SN-{Guid.NewGuid():N}");

        using var fileContent = new ByteArrayContent([0x73, 0x6f, 0x6c, 0x69, 0x64, 0x20, 0x74, 0x65, 0x73, 0x74]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadContent = new MultipartFormDataContent
        {
            { fileContent, "File", "benchy.stl" },
            { new StringContent("OrcaSlicer"), "SlicerEngine" },
            { new StringContent("9.9.9"), "SlicerVersion" },
        };

        var uploadResponse = await client.PostAsync(new Uri("/api/v1/parts", UriKind.Relative), uploadContent);

        uploadResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // Asserted at the job level (SlicerEngineUsed/SlicerVersionUsed are fixed at job creation and
    // never recomputed) rather than via a Confirmed Part, since the dummy bundle's stub binary
    // can never actually complete a slice in this environment (same carve-out as the other tests
    // in this file) — the underlying guarantee (FR-008) is the same either way.
    [Fact]
    public async Task ChangingOrgDefault_DoesNotRetroactivelyRewriteAnAlreadyProcessedPart()
    {
        var client = await AuthenticatedClientAsync();
        await RegisterPrinterWithNozzleDiameterAsync(client, $"SN-{Guid.NewGuid():N}");

        using var fileContent = new ByteArrayContent([0x73, 0x6f, 0x6c, 0x69, 0x64, 0x20, 0x74, 0x65, 0x73, 0x74]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadContent = new MultipartFormDataContent { { fileContent, "File", "benchy.stl" } };

        var uploadResponse = await client.PostAsync(new Uri("/api/v1/parts", UriKind.Relative), uploadContent);
        var submitted = await uploadResponse.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();

        // Change the org default after the job was created but before re-checking it.
        await client.PutAsJsonAsync(
            new Uri("/api/v1/settings/slicing-defaults", UriKind.Relative),
            new { engine = "SuperSlicer", version = "2.5.59.13" });

        var jobResponse = await client.GetAsync(new Uri($"/api/v1/part-processing-jobs/{submitted!.JobId}", UriKind.Relative));
        var job = await jobResponse.Content.ReadFromJsonAsync<PartProcessingJobResponse>();

        job!.Slicer!.Engine.Should().Be("OrcaSlicer");
        job.Slicer.Version.Should().Be("2.2.0");

        // Restore the auto-defaulted baseline (OrcaSlicer, the newest/preferred bundled engine) —
        // there's no "clear explicit default" endpoint, and every test in this IClassFixture-shared
        // factory/database runs against the same Organization row with no guaranteed ordering, so
        // leaving the SuperSlicer override in place here would silently corrupt any sibling test that
        // assumes no explicit default has been set yet.
        await client.PutAsJsonAsync(
            new Uri("/api/v1/settings/slicing-defaults", UriKind.Relative),
            new { engine = "OrcaSlicer", version = "2.2.0" });
    }
}
