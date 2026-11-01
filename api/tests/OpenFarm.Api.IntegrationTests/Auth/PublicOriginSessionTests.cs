using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Auth;

public sealed class PublicOriginSessionTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task HttpsOriginWithHttpBackend_SupportsSessionLifecycleAndRejectsInvalidProofs()
    {
        await using var app = factory.WithWebHostBuilder(builder => builder.UseSetting("ORIGIN", "https://farm.example:443/"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://internal:6868"),
            HandleCookies = false,
        });
        var cookies = new CookieContainer();
        var publicAddress = new Uri("https://farm.example/api/v1/auth/");

        // Model a browser's HTTPS cookie jar while the tunnel sends HTTP to the backend.
        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? csrf = null, string origin = "https://farm.example")
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Origin", origin);
            var cookie = cookies.GetCookieHeader(publicAddress);
            if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
            if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
            if (path.EndsWith("/login", StringComparison.Ordinal))
                request.Content = JsonContent.Create(new LoginCommand("admin", "admin-openfarm"));
            var response = await client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (var value in values) cookies.SetCookies(publicAddress, value);
            return response;
        }

        using var login = await SendAsync(HttpMethod.Post, "/api/v1/auth/login");
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.GetValues("Set-Cookie").Should().Contain(value =>
            value.StartsWith("openfarm.refresh=", StringComparison.Ordinal) && value.Contains("secure", StringComparison.OrdinalIgnoreCase));
        login.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://farm.example");

        using var csrfResponse = await SendAsync(HttpMethod.Get, "/api/v1/auth/csrf");
        csrfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var csrf = (await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;
        using var missing = await SendAsync(HttpMethod.Post, "/api/v1/auth/refresh");
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", "invalid");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var untrusted = await SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", csrf, "https://untrusted.example");
        untrusted.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var refresh = await SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", csrf);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        using var logout = await SendAsync(HttpMethod.Post, "/api/v1/auth/logout", csrf);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        logout.Headers.GetValues("Set-Cookie").Should().Contain(value =>
            value.StartsWith("openfarm.refresh=", StringComparison.Ordinal) && value.Contains("secure", StringComparison.OrdinalIgnoreCase));
        using var expired = await SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", csrf);
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("https://other.example")]
    public async Task ExplicitAdditionalOrigins_RemainTrusted(string origin)
    {
        await using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ORIGIN", "https://farm.example");
            builder.UseSetting("Cors:AllowedOrigins:0", origin);
        });
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", origin);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be(origin);
    }
}
