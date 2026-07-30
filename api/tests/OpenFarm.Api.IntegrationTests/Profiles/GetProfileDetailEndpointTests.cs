using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-011, SC-006 — the single-profile detail endpoint returns full rawDataJson plus chain-merged
// derived fields and hasUnresolvedBaseInChain, and 404s for an unknown/cross-tenant id.
public class GetProfileDetailEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    [Fact]
    public async Task GetMachineProfile_ReturnsFullRawDataAndDerivedFields()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var name = $"Detail Printer {suffix}";
        var bundle = ProfileBundleTestHelper.BuildZip(
            ($"machine/{name}.json", $$"""{"name":"{{name}}","nozzle_diameter":["0.4"],"custom_marker":"present"}"""));
        var selections = JsonSerializer.Serialize(new object[] { new { type = "Machine", name, duplicateAction = (string?)null } });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        var created = await confirmResponse.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        var id = created!.CreatedMachineProfiles.Single().Id;

        var response = await client.GetAsync(new Uri($"/api/v1/profiles/machine/{id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<MachineProfileDetailResponse>();
        detail!.RawDataJson.Should().Contain("custom_marker");
        detail.NozzleSizeMm.Should().Be(0.4);
        detail.HasUnresolvedBaseInChain.Should().BeFalse();
    }

    [Fact]
    public async Task GetMachineProfile_UnknownId_Returns404()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/v1/profiles/machine/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFilamentProfile_UnknownId_Returns404()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/v1/profiles/filament/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFilamentProfile_ReturnsFullRawDataAndDerivedFields()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var name = $"Detail PLA {suffix}";
        var bundle = ProfileBundleTestHelper.BuildZip(
            ($"filament/{name}.json", $$"""{"name":"{{name}}","filament_type":["PLA"],"custom_marker":"present"}"""));
        var selections = JsonSerializer.Serialize(new object[] { new { type = "Filament", name, duplicateAction = (string?)null } });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        var created = await confirmResponse.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        var id = created!.CreatedFilamentProfiles.Single().Id;

        var response = await client.GetAsync(new Uri($"/api/v1/profiles/filament/{id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<FilamentProfileDetailResponse>();
        detail!.RawDataJson.Should().Contain("custom_marker");
        detail.Material.Should().Be("PLA");
        detail.HasUnresolvedBaseInChain.Should().BeFalse();
    }
}
