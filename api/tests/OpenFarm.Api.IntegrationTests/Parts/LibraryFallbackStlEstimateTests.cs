using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.PartProcessingJobs;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Endpoints.Vendors;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.Parts;

// Library estimation requires explicit defaults, regardless of fleet/inventory profiles.
public class LibraryFallbackStlEstimateTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>A printer fully configured (machine + process profile linked) so it wins the no-target-printer fleet-representative pick — the only way an STL upload reaches filament resolution at all. Deliberately separate from the simulator printer used to obtain a real feed slot below.</summary>
    private async Task SeedFullyConfiguredPrinterAsync()
    {
        using var scope = factory.Services.CreateScope();
        var printerRepo = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();
        var machineRepo = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();
        var processRepo = scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>();

        var machine = MachineProfile.CreateSystemProfile(
            $"Library Fallback Machine {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"machine","nozzle_diameter":["0.4"]}""",
            "System", Guid.NewGuid().ToString("N"), isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null);
        await machineRepo.AddAsync(machine, CancellationToken.None);

        var process = ProcessProfile.CreateSystemProfile(
            $"Library Fallback Process {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"process","layer_height":"0.2"}""",
            "System", Guid.NewGuid().ToString("N"), isSelectable: true, compatibleMachineNames: null);
        await processRepo.AddAsync(process, CancellationToken.None);

        var printer = Printer.Create(
            $"Library Fallback Printer {Guid.NewGuid():N}", "Bambu", "X1C", "10.0.0.91", $"SN-{Guid.NewGuid():N}");
        printer.LinkMachineProfile(machine.Id);
        printer.LinkProcessProfile(process.Id);
        // 012 — required alongside machine/process for ResolveAsync to reach filament resolution at
        // all; X1C's catalog compatible set includes HighTemp (matches SeedDistinguishableFilamentProfileAsync's hot_plate_temp).
        printer.SelectBuildPlate(BuildPlateType.HighTemp);
        await printerRepo.AddAsync(printer, CancellationToken.None);
    }

    /// <summary>Directly seeds a distinguishable System <see cref="FilamentProfile"/> row to link a tracked <see cref="OpenFarm.Domain.FilamentTracking.Filament"/> to — no HTTP endpoint creates a raw profile row outside a bundle import.</summary>
    private async Task<Guid> SeedDistinguishableFilamentProfileAsync()
    {
        using var scope = factory.Services.CreateScope();
        var filamentProfileRepo = scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>();

        var profile = FilamentProfile.CreateSystemProfile(
            $"Library Fallback PLA {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"]}""",
            "System", Guid.NewGuid().ToString("N"), isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null);
        await filamentProfileRepo.AddAsync(profile, CancellationToken.None);

        return profile.Id;
    }

    private static async Task<HttpResponseMessage> UploadStlAsync(HttpClient client)
    {
        using var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("solid test\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid test"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadContent = new MultipartFormDataContent { { fileContent, "File", "benchy.stl" } };
        return await client.PostAsync(new Uri("/api/v1/parts", UriKind.Relative), uploadContent);
    }

    private static async Task<PartProcessingJobResponse> WaitForTerminalStatusAsync(HttpClient client, Guid jobId)
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

    [Fact]
    public async Task CreatePart_UploadedStl_InventoryAndFleetDoNotSupplyEstimationDefaults()
    {
        var client = await AuthenticatedClientAsync(_client);
        await SeedFullyConfiguredPrinterAsync();
        var filamentProfileId = await SeedDistinguishableFilamentProfileAsync();

        var vendorResponse = await client.PostAsJsonAsync(new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = $"Vendor {Guid.NewGuid():N}" });
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>();

        var filamentResponse = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), new CreateFilamentRequest
        {
            VendorId = vendor!.Id,
            Material = "PLA Basic",
            MaterialType = "PLA",
            ProfileAssignments = [new("Bambu", "X1C", 0.4m, filamentProfileId)],
            Variants = [new CreateFilamentVariantRequest { ColorName = "Black", ColorHex = "#18181b" }],
        });
        var filament = await filamentResponse.Content.ReadFromJsonAsync<FilamentResponse>();
        var variantId = filament!.Variants[0].Id;

        var spoolId = await RegisterSpoolAsync(client, variantId);

        // A real feed slot to load onto, then straight back off — leaving the spool InStorage
        // (the library tier), its Filament.FilamentProfileId link intact.
        var simulatorPrinterId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, simulatorPrinterId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;

        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = slotId });
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/unload", UriKind.Relative), new { action = "ReturnToStorage" });

        var uploadResponse = await UploadStlAsync(client);
        var submitted = await uploadResponse.Content.ReadFromJsonAsync<PartProcessingJobSubmittedResponse>();

        var finalJob = await WaitForTerminalStatusAsync(client, submitted!.JobId);

        // No System PLA profile exists anywhere in this test's database — if the library tier
        // hadn't engaged, this would fail at filament resolution naming 'PLA' unconfigured instead of
        // reaching the (always-broken, in this test environment) CLI invocation.
        finalJob.Status.Should().Be("ReadyForReview");
        finalJob.Preview!.EstimateStatus.Should().Be("Unavailable");
        finalJob.Preview!.EstimateUnavailableReason.Should().Contain("default estimation profiles");
        finalJob.Preview!.EstimateUnavailableReason.Should().NotContain("'PLA'");
    }
}
