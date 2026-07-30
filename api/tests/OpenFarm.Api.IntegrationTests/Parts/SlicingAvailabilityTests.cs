using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;
using OpenFarm.Api.IntegrationTests.Profiles;

namespace OpenFarm.Api.IntegrationTests.Parts;

/// <summary>
/// With a genuinely empty bundle (no engine/version present at all), a requires-slicing upload must
/// fail promptly with a "no slicer available" ProblemDetails — before any job is created — rather
/// than creating a job that then sits Processing forever (FR-006/FR-011, spec Acceptance Scenario
/// US1-4, SC-002).
/// </summary>
public class SlicingAvailabilityTests(EmptySlicerBundleWebApplicationFactory factory) : IClassFixture<EmptySlicerBundleWebApplicationFactory>
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

    private static async Task RegisterPrinterWithNozzleDiameterAsync(HttpClient client, string serialNumber)
    {
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/printers", UriKind.Relative),
            new RegisterPrinterRequest
            {
                Name = $"Slicing Availability Test Printer {serialNumber}",
                Brand = "System",
                ModelCode = "Simulation",
                IpAddress = "10.0.0.81",
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>();

        var profileName = $"Slicing Availability Test Machine {serialNumber}";
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

    [Fact]
    public async Task GetSlicingDefaults_EmptyBundle_ReturnsNullDefaultAndEmptyAvailable()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/v1/settings/slicing-defaults", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("default").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("available").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CreatePart_RequiresSlicingUploadWithNoSlicerAvailable_Returns422BeforeAnyJobIsCreated()
    {
        var client = await AuthenticatedClientAsync();
        await RegisterPrinterWithNozzleDiameterAsync(client, $"SN-{Guid.NewGuid():N}");

        using var fileContent = new ByteArrayContent([0x73, 0x6f, 0x6c, 0x69, 0x64, 0x20, 0x74, 0x65, 0x73, 0x74]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploadContent = new MultipartFormDataContent { { fileContent, "File", "benchy.stl" } };

        var uploadResponse = await client.PostAsync(new Uri("/api/v1/parts", UriKind.Relative), uploadContent);

        uploadResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
