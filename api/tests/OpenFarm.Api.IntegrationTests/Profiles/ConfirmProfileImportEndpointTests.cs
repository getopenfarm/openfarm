using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-004, SC-001, SC-003 — confirm persists only the selected presets; unselected ones are absent
// without a trace.
public class ConfirmProfileImportEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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
    public async Task Confirm_WithSubsetSelected_PersistsOnlySelectedPresets()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var zipBytes = ProfileBundleTestHelper.SampleBundle(suffix);

        var selections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Machine", name = $"Test Printer {suffix}", duplicateAction = (string?)null },
            new { type = "Filament", name = $"Test PLA {suffix}", duplicateAction = (string?)null },
        });

        using var content = ProfileBundleTestHelper.BuildUploadContent(zipBytes, "bundle.zip", selections);
        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        body!.CreatedMachineProfiles.Should().ContainSingle(p => p.Name == $"Test Printer {suffix}");
        body.CreatedFilamentProfiles.Should().ContainSingle(p => p.Name == $"Test PLA {suffix}");

        var machineList = await client.GetFromJsonAsync<ListMachineProfilesResponse>(new Uri("/api/v1/profiles/machine", UriKind.Relative));
        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));

        machineList!.Profiles.Should().ContainSingle(p => p.Name == $"Test Printer {suffix}");
        filamentList!.Profiles.Should().ContainSingle(p => p.Name == $"Test PLA {suffix}");
        filamentList.Profiles.Should().NotContain(p => p.Name == $"Test PETG {suffix}");
    }

    [Fact]
    public async Task Confirm_WithNothingSelected_CreatesNoProfiles()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var zipBytes = ProfileBundleTestHelper.SampleBundle(suffix);
        var selections = JsonSerializer.Serialize(Array.Empty<object>());

        using var content = ProfileBundleTestHelper.BuildUploadContent(zipBytes, "bundle.zip", selections);
        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        body!.CreatedMachineProfiles.Should().BeEmpty();
        body.CreatedFilamentProfiles.Should().BeEmpty();

        var machineList = await client.GetFromJsonAsync<ListMachineProfilesResponse>(new Uri("/api/v1/profiles/machine", UriKind.Relative));
        machineList!.Profiles.Should().NotContain(p => p.Name.Contains(suffix));
    }
}
