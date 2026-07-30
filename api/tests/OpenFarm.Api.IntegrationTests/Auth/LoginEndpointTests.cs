using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Auth;

public class LoginEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAnd200()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin", "admin-openfarm"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401AndNoToken()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin", "wrong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithEmptyUsername_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand(string.Empty, "admin-openfarm"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
