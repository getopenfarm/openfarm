using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-001–003, SC-004 — preview extracts and returns every recognized preset without persisting
// anything: catalog counts (via the real list endpoints) are unchanged before/after.
public class PreviewProfileImportEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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
    public async Task Preview_WithMultiPresetBundle_ReturnsEveryRecognizedPreset()
    {
        var client = await AuthenticatedClientAsync();
        var zipBytes = ProfileBundleTestHelper.SampleBundle(Guid.NewGuid().ToString("N"));
        using var content = ProfileBundleTestHelper.BuildUploadContent(zipBytes, "bundle.zip");

        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/preview", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PreviewProfileImportResponse>();
        body!.MachinePresets.Should().ContainSingle();
        body.FilamentPresets.Should().HaveCount(2);
    }

    [Fact]
    public async Task Preview_DoesNotPersistAnything()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var zipBytes = ProfileBundleTestHelper.SampleBundle(suffix);

        var beforeMachine = await client.GetFromJsonAsync<ListMachineProfilesResponse>(
            new Uri("/api/v1/profiles/machine", UriKind.Relative));
        var beforeFilament = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(
            new Uri("/api/v1/profiles/filament", UriKind.Relative));

        using var content = ProfileBundleTestHelper.BuildUploadContent(zipBytes, "bundle.zip");
        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/preview", UriKind.Relative), content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterMachine = await client.GetFromJsonAsync<ListMachineProfilesResponse>(
            new Uri("/api/v1/profiles/machine", UriKind.Relative));
        var afterFilament = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(
            new Uri("/api/v1/profiles/filament", UriKind.Relative));

        afterMachine!.Items.Should().HaveCount(beforeMachine!.Items.Count);
        afterFilament!.Items.Should().HaveCount(beforeFilament!.Items.Count);
        afterMachine.Items.Should().NotContain(p => p.Name.Contains(suffix));
    }
}
