using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Api.IntegrationTests.Auth;

public class WhoAmIEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAndGetTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin@openfarm.local", "ChangeMe123!"));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    [Fact]
    public async Task WhoAmI_WithValidToken_ReturnsIdentity()
    {
        var token = await LoginAndGetTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/auth/whoami", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<WhoAmIResult>();
        body.Should().NotBeNull();
        body!.Username.Should().Be("admin@openfarm.local");
        body.OrganizationId.Should().Be(Organization.WellKnownId);
    }

    [Fact]
    public async Task WhoAmI_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/auth/whoami", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhoAmI_WithInvalidToken_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/auth/whoami", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
