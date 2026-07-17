using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-010, US3 Acceptance Scenarios 1-2 — every imported machine/filament profile is listed with its
// name, source format, and derived spec fields.
public class ListProfilesEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    [Fact]
    public async Task ListMachineAndFilamentProfiles_ReturnsImportedProfilesWithKeyFields()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var machineName = $"Test Printer {suffix}";
        var filamentName = $"Test PLA {suffix}";
        var bundle = ProfileBundleTestHelper.BuildZip(
            ($"machine/{machineName}.json", $$"""{"name":"{{machineName}}","nozzle_diameter":["0.4"]}"""),
            ($"filament/{filamentName}.json", $$"""{"name":"{{filamentName}}","filament_type":["PLA"]}"""));

        var selections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Machine", name = machineName, duplicateAction = (string?)null },
            new { type = "Filament", name = filamentName, duplicateAction = (string?)null },
        });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        confirmResponse.EnsureSuccessStatusCode();

        var machineList = await client.GetFromJsonAsync<ListMachineProfilesResponse>(new Uri("/api/v1/profiles/machine", UriKind.Relative));
        var machineEntry = machineList!.Profiles.Should().ContainSingle(p => p.Name == machineName).Subject;
        machineEntry.SourceFormat.Should().Be("OrcaSlicer");
        machineEntry.NozzleSizeMm.Should().Be(0.4);

        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));
        var filamentEntry = filamentList!.Profiles.Should().ContainSingle(p => p.Name == filamentName).Subject;
        filamentEntry.SourceFormat.Should().Be("OrcaSlicer");
        filamentEntry.Material.Should().Be("PLA");
    }
}
