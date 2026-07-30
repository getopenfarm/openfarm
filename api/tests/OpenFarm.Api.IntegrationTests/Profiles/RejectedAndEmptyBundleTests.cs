using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-007, FR-008, SC-005 — a corrupted/unrecognized upload is rejected with 422 and creates no
// catalog entries; a successfully-opened bundle with nothing recognizable returns 200 with both
// arrays empty (not treated as an error).
public class RejectedAndEmptyBundleTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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
    public async Task Preview_WithCorruptedArchive_Returns422AndCreatesNoEntries()
    {
        var client = await AuthenticatedClientAsync();
        var beforeMachine = await client.GetFromJsonAsync<ListMachineProfilesResponse>(new Uri("/api/v1/profiles/machine", UriKind.Relative));

        var notAZip = Encoding.UTF8.GetBytes("this is not a zip archive");
        using var content = ProfileBundleTestHelper.BuildUploadContent(notAZip, "bad.zip");

        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/preview", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var afterMachine = await client.GetFromJsonAsync<ListMachineProfilesResponse>(new Uri("/api/v1/profiles/machine", UriKind.Relative));
        afterMachine!.Items.Should().HaveCount(beforeMachine!.Items.Count);
    }

    [Fact]
    public async Task Preview_WithUnrecognizedExtension_Returns422()
    {
        var client = await AuthenticatedClientAsync();
        var zipBytes = ProfileBundleTestHelper.SampleBundle(Guid.NewGuid().ToString("N"));
        using var content = ProfileBundleTestHelper.BuildUploadContent(zipBytes, "bundle.txt");

        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/preview", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Preview_WithNoRecognizablePresets_ReturnsOkWithEmptyArrays()
    {
        var client = await AuthenticatedClientAsync();
        var zipBytes = ProfileBundleTestHelper.BuildZip(("readme.txt", "no presets in here"));
        using var content = ProfileBundleTestHelper.BuildUploadContent(zipBytes, "empty.zip");

        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/preview", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PreviewProfileImportResponse>();
        body!.MachinePresets.Should().BeEmpty();
        body.FilamentPresets.Should().BeEmpty();
    }
}
