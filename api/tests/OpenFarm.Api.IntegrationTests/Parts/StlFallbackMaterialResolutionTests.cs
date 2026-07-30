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

/// <summary>
/// Regression coverage for research.md R1 (010-filament-matching): an STL's fallback material used to
/// be an engine-specific literal profile name ("Generic PLA"/"Bambu PLA Basic"), which never matched
/// any real <c>FilamentProfile.MaterialType</c>, so every STL upload failed filament resolution
/// regardless of how much System profile ingestion had actually landed. Split across two classes (each
/// gets its own <see cref="CustomWebApplicationFactory"/>/database via <c>IClassFixture</c>) so one
/// scenario's seeded System PLA profile can never leak into the other's "zero PLA profiles" assumption.
/// </summary>
public class StlFallbackMaterialResolutionWithPlaSeededTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreatePart_UploadedStl_SystemPlaProfileIngested_NeverFailsOnGenericPlaResolution()
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
        finalJob.Status.Should().Be("Failed");
        finalJob.ErrorMessage.Should().NotContain("Generic PLA");
        finalJob.ErrorMessage.Should().NotContain("Bambu PLA Basic");
        finalJob.ErrorMessage.Should().Contain("CLI exited with code");
    }
}

public class StlFallbackMaterialResolutionWithoutPlaSeededTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreatePart_UploadedStl_NoPlaSystemProfileAnywhere_FailsNamingPlaNotGenericPla()
    {
        var client = await StlFallbackTestHelpers.AuthenticatedClientAsync(_client);
        await StlFallbackTestHelpers.SeedFullyConfiguredPrinterAsync(factory);

        var uploadResponse = await StlFallbackTestHelpers.UploadStlAsync(client);
        var submitted = await uploadResponse.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();

        var finalJob = await StlFallbackTestHelpers.WaitForTerminalStatusAsync(client, submitted!.JobId);

        finalJob.Status.Should().Be("Failed");
        finalJob.ErrorMessage.Should().Contain("'PLA'");
        finalJob.ErrorMessage.Should().NotContain("Generic PLA");
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
        await printerRepo.AddAsync(printer, CancellationToken.None);
    }

    public static async Task SeedSystemPlaFilamentProfileAsync(CustomWebApplicationFactory factory)
    {
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var filamentRepo = scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>();

        var profile = FilamentProfile.CreateSystemProfile(
            Organization.WellKnownId, $"Fallback Test PLA {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"]}""",
            "System", Guid.NewGuid().ToString("N"), isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null);
        await filamentRepo.AddAsync(profile, CancellationToken.None);
    }

    public static async Task<HttpResponseMessage> UploadStlAsync(HttpClient client)
    {
        using var fileContent = new ByteArrayContent([0x73, 0x6f, 0x6c, 0x69, 0x64, 0x20, 0x74, 0x65, 0x73, 0x74]);
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
