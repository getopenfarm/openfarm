using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.PartProcessingJobs;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Api.IntegrationTests.Parts;

// Library estimation requires explicit defaults, regardless of fleet/inventory profiles.
public class StlFallbackMaterialResolutionWithPlaSeededTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreatePart_UploadedStl_SystemPlaProfileDoesNotSupplyEstimationDefaults()
    {
        var client = await StlFallbackTestHelpers.AuthenticatedClientAsync(_client);
        await StlFallbackTestHelpers.SeedFullyConfiguredPrinterAsync(factory);
        await StlFallbackTestHelpers.SeedSystemPlaFilamentProfileAsync(factory);

        var uploadResponse = await StlFallbackTestHelpers.UploadStlAsync(client);
        var submitted = await uploadResponse.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();

        var finalJob = await StlFallbackTestHelpers.WaitForTerminalStatusAsync(client, submitted!.JobId);

        // The dummy bundle's stub binary can never actually complete a slice in this test environment
        // (CustomWebApplicationFactory's carve-out), so the job still ends up Failed — what matters is
        // *why*: it must fail at CLI invocation, having already resolved a real PLA filament, never at
        // filament resolution naming the old literal profile name.
        finalJob.Status.Should().Be("ReadyForReview");
        finalJob.Preview!.EstimateStatus.Should().Be("Unavailable");
        finalJob.Preview!.EstimateUnavailableReason.Should().NotContain("Generic PLA");
        finalJob.Preview!.EstimateUnavailableReason.Should().NotContain("Bambu PLA Basic");
        finalJob.Preview!.EstimateUnavailableReason.Should().Contain("default estimation profiles");
    }
}

public class StlFallbackMaterialResolutionWithoutPlaSeededTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreatePart_UploadedStl_NoEstimationDefaults_RemainsReviewable()
    {
        var client = await StlFallbackTestHelpers.AuthenticatedClientAsync(_client);
        await StlFallbackTestHelpers.SeedFullyConfiguredPrinterAsync(factory);

        var uploadResponse = await StlFallbackTestHelpers.UploadStlAsync(client);
        var submitted = await uploadResponse.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();

        var finalJob = await StlFallbackTestHelpers.WaitForTerminalStatusAsync(client, submitted!.JobId);

        finalJob.Status.Should().Be("ReadyForReview");
        finalJob.Preview!.EstimateStatus.Should().Be("Unavailable");
        finalJob.Preview!.EstimateUnavailableReason.Should().Contain("default estimation profiles");
        finalJob.Preview!.EstimateUnavailableReason.Should().NotContain("Generic PLA");
    }
}

internal static class StlFallbackTestHelpers
{
    public static async Task<HttpClient> AuthenticatedClientAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin", "admin-openfarm"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    /// <summary>
    /// A printer fully configured (machine + process profile linked) so it wins the no-target-printer
    /// fleet-representative pick (<c>ResolveSlicingProfileSelection.ResolveForFleetRepresentativeAsync</c>)
    /// — the only way an STL upload (which never targets a specific printer) reaches filament resolution
    /// at all.
    /// </summary>
    public static async Task SeedFullyConfiguredPrinterAsync(CustomWebApplicationFactory factory)
    {
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var printerRepo = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();
        var machineRepo = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();
        var processRepo = scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>();

        var machine = MachineProfile.CreateSystemProfile(
            Organization.WellKnownId, $"Fallback Test Machine {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"machine","nozzle_diameter":["0.4"]}""",
            "System", Guid.NewGuid().ToString("N"), isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null);
        await machineRepo.AddAsync(machine, CancellationToken.None);

        var process = ProcessProfile.CreateSystemProfile(
            Organization.WellKnownId, $"Fallback Test Process {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"process","layer_height":"0.2"}""",
            "System", Guid.NewGuid().ToString("N"), isSelectable: true, compatibleMachineNames: null);
        await processRepo.AddAsync(process, CancellationToken.None);

        var printer = Printer.Create(
            Organization.WellKnownId, $"Fallback Test Printer {Guid.NewGuid():N}", "Bambu", "X1C", "10.0.0.90", $"SN-{Guid.NewGuid():N}");
        printer.LinkMachineProfile(machine.Id);
        printer.LinkProcessProfile(process.Id);
        // 012 — required alongside machine/process for the fleet-representative pick to consider this
        // printer at all; X1C's catalog compatible set includes HighTemp (matches
        // SeedSystemPlaFilamentProfileAsync's hot_plate_temp).
        printer.SelectBuildPlate(BuildPlateType.HighTemp);
        await printerRepo.AddAsync(printer, CancellationToken.None);
    }

    public static async Task SeedSystemPlaFilamentProfileAsync(CustomWebApplicationFactory factory)
    {
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var filamentRepo = scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>();

        var profile = FilamentProfile.CreateSystemProfile(
            Organization.WellKnownId, $"Fallback Test PLA {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"]}""",
            "System", Guid.NewGuid().ToString("N"), isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null);
        await filamentRepo.AddAsync(profile, CancellationToken.None);
    }

    public static async Task<HttpResponseMessage> UploadStlAsync(HttpClient client)
    {
        using var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("solid test\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid test"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadContent = new MultipartFormDataContent { { fileContent, "File", "benchy.stl" } };
        return await client.PostAsync(new Uri("/api/v1/parts", UriKind.Relative), uploadContent);
    }

    public static async Task<PartProcessingJobResponse> WaitForTerminalStatusAsync(HttpClient client, Guid jobId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var response = await client.GetAsync(new Uri($"/api/v1/part-processing-jobs/{jobId}", UriKind.Relative));
            var job = await response.Content.ReadFromJsonAsync<PartProcessingJobResponse>();

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
}
