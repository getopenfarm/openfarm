using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Printers;

/// <summary>
/// Requires Docker (Testcontainers.PostgreSql) — not runnable in every sandbox, but exercised by CI.
/// </summary>
public class RemovePrinterEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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
    public async Task Remove_ExistingPrinter_Returns204AndWritesActivityLogEntry()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"RM-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/printers", UriKind.Relative),
            new RegisterPrinterRequest
            {
                Name = "Remove Test Printer",
                Brand = "Bambu",
                ModelCode = "P1S",
                IpAddress = "10.0.0.80",
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>();

        var response = await client.DeleteAsync(new Uri($"/api/v1/printers/{printer!.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
        page!.Items.Should().ContainSingle(e =>
            e.EntityId == printer.Id.ToString() && e.EventType == "PrinterRemoved");
    }
}
