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
public class RegisterPrinterEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    private static RegisterPrinterRequest ValidRequest(string serialNumber, string ipAddress = "10.0.0.50") => new()
    {
        Name = "Test Printer",
        Brand = "System",
        ModelCode = "Simulation",
        IpAddress = ipAddress,
        SerialNumber = serialNumber,
        AccessCode = "12345678",
    };

    [Fact]
    public async Task Register_WithValidVerifiableConnection_ReturnsCreatedPrinter()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"SN-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/printers", UriKind.Relative), ValidRequest(serialNumber));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<PrinterSummaryResponse>();
        body.Should().NotBeNull();
        body!.SerialNumber.Should().Be(serialNumber);
        body.Status.Should().Be("Idle");
        body.IsReachable.Should().BeFalse();
    }

    [Fact]
    public async Task Register_WithDuplicateSerialNumber_Returns409AndPermitsSameIp()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"SN-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync(new Uri("/api/v1/printers", UriKind.Relative), ValidRequest(serialNumber));

        var duplicateRequest = ValidRequest(serialNumber, "10.0.0.51");
        duplicateRequest.Name = "Duplicate";
        var duplicateResponse = await client.PostAsJsonAsync(new Uri("/api/v1/printers", UriKind.Relative), duplicateRequest);

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Same IP as the first printer, different serial — duplicate IPs are explicitly permitted.
        var sameIpRequest = ValidRequest($"SN-{Guid.NewGuid():N}", "10.0.0.50");
        sameIpRequest.Name = "Same IP";
        var sameIpDifferentSerialResponse = await client.PostAsJsonAsync(new Uri("/api/v1/printers", UriKind.Relative), sameIpRequest);

        sameIpDifferentSerialResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_WithUnverifiableConnection_Returns422AndDoesNotPersist()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"SN-{Guid.NewGuid():N}";
        var distinctiveName = $"UnverifiableTest-{serialNumber}";

        var request = ValidRequest(serialNumber, "10.0.0.99");
        request.Name = distinctiveName;
        request.AccessCode = "wrong"; // SimulatorPrinterConnector's fixed sentinel for "verification fails".

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/printers", UriKind.Relative), request);

        response.StatusCode.Should().Be((HttpStatusCode)422);

        var fleetResponse = await client.GetAsync(new Uri("/api/v1/printers", UriKind.Relative));
        var fleet = await fleetResponse.Content.ReadFromJsonAsync<ListFleetResponse>();
        fleet!.Printers.Should().NotContain(p => p.Name == distinctiveName);
    }
}
