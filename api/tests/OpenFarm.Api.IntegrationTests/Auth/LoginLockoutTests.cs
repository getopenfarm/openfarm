using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Auth;

/// <summary>
/// Isolated in its own <see cref="CustomWebApplicationFactory"/> (own Postgres container) rather
/// than sharing <see cref="LoginEndpointTests"/>'s — this test locks out the seeded admin account
/// for the configured lockout window, which would make any other test in the same factory that
/// needs a valid login flaky depending on xUnit's (unordered) execution order within a class.
/// </summary>
public class LoginLockoutTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_AfterFiveFailedAttempts_LocksOutAccountEvenWithCorrectPassword()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failedResponse = await _client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative),
                new LoginCommand("admin", "wrong-password"));
            failedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var lockedOutResponse = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin", "admin-openfarm"));

        lockedOutResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
