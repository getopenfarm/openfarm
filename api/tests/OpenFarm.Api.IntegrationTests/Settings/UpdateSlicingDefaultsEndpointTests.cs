using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Settings;
using OpenFarm.Api.IntegrationTests.Parts;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Settings;

/// <summary>
/// The test factory's dummy bundle (see <see cref="CustomWebApplicationFactory"/>) bundles
/// OrcaSlicer 2.2.0 and SuperSlicer 2.5.59.13 for Linux — OrcaSlicer 2.2.0 is the "valid" pair
/// every case here targets (contracts/slicer-management-api.md PUT, FR-007/FR-019).
/// </summary>
public class UpdateSlicingDefaultsEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin@openfarm.local", "ChangeMe123!"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body!.Token);
        return _client;
    }

    [Fact]
    public async Task Put_ValidAvailablePair_Returns200WithExplicitDefault()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/v1/settings/slicing-defaults", UriKind.Relative),
            new { engine = "OrcaSlicer", version = "2.2.0" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SlicingDefaultsResponse>();
        body!.Default.Should().NotBeNull();
        body.Default!.Engine.Should().Be("OrcaSlicer");
        body.Default.Version.Should().Be("2.2.0");
        body.Default.IsExplicit.Should().BeTrue();
    }

    [Fact]
    public async Task Put_ValidAvailablePair_WritesActivityLogEntry()
    {
        var client = await AuthenticatedClientAsync();

        await client.PutAsJsonAsync(
            new Uri("/api/v1/settings/slicing-defaults", UriKind.Relative),
            new { engine = "OrcaSlicer", version = "2.2.0" });

        var activityResponse = await client.GetAsync(new Uri("/api/v1/activity-log?Page=1&PageSize=50", UriKind.Relative));
        var page = await activityResponse.Content.ReadFromJsonAsync<JsonElement>();

        var hasEntry = page.GetProperty("items").EnumerateArray()
            .Any(e => e.GetProperty("eventType").GetString() == "SlicingDefaultsUpdated");
        hasEntry.Should().BeTrue();
    }

    [Fact]
    public async Task Put_UnavailablePair_Returns422()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/v1/settings/slicing-defaults", UriKind.Relative),
            new { engine = "SuperSlicer", version = "9.9.9" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
