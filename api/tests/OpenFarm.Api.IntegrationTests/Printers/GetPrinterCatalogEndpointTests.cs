using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Printers;

/// <summary>
/// Requires Docker (Testcontainers.PostgreSql) — not runnable in every sandbox, but exercised by CI.
/// </summary>
public class GetPrinterCatalogEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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
    public async Task GetCatalog_ReturnsEveryPrinterModelCatalogEntryGroupedByBrand()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/v1/printers/catalog", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrinterCatalogResponse>();
        body.Should().NotBeNull();

        var bambu = body!.Brands.Should().ContainSingle(b => b.Brand == "Bambu").Which;
        bambu.Models.Should().BeEquivalentTo(["X1", "X1C", "X1E", "P1P", "P1S", "A1", "A1MINI"]);

        var system = body.Brands.Should().ContainSingle(b => b.Brand == "System").Which;
        system.Models.Should().BeEquivalentTo(["Simulation"]);
    }

    [Fact]
    public async Task GetCatalog_WithoutAuthentication_Returns401()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/printers/catalog", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
