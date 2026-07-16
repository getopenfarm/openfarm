using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers;

namespace OpenFarm.Api.IntegrationTests.Printers;

/// <summary>
/// Requires Docker (Testcontainers.PostgreSql) — not runnable in every sandbox, but exercised by CI.
/// </summary>
public class MarkPrinterReadyEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    private async Task<PrinterSummaryResponse> RegisterAwaitingClearPrinterAsync(HttpClient client)
    {
        var serialNumber = $"MR-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/printers", UriKind.Relative),
            new RegisterPrinterRequest
            {
                Name = "Mark Ready Test Printer",
                Brand = "Bambu",
                ModelCode = "P1S",
                IpAddress = "10.0.0.70",
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>();

        var connector = factory.Services.GetRequiredService<FakePrinterConnector>();
        connector.Emit(serialNumber, new PrinterConnectorEvent(DateTimeOffset.UtcNow, GcodeState: "FINISH"));

        await WaitForStatusAsync(client, printer!.Id, "AwaitingClear");

        return printer;
    }

    private static async Task WaitForStatusAsync(HttpClient client, Guid printerId, string expectedStatus)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var fleetResponse = await client.GetAsync(new Uri("/api/v1/printers", UriKind.Relative));
            var fleet = await fleetResponse.Content.ReadFromJsonAsync<ListFleetResponse>();
            var current = fleet!.Printers.FirstOrDefault(p => p.Id == printerId);
            if (current?.Status == expectedStatus)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Printer {printerId} never reached status {expectedStatus}.");
    }

    [Fact]
    public async Task MarkReady_FromAwaitingClear_TransitionsToIdleAndWritesActivityLogEntry()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterAwaitingClearPrinterAsync(client);

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/printers/{printer.Id}/mark-ready", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrinterSummaryResponse>();
        body!.Status.Should().Be("Idle");

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
        page!.Items.Should().ContainSingle(e =>
            e.EntityId == printer.Id.ToString() && e.EventType == "PrinterMarkedReady");
    }

    [Fact]
    public async Task MarkReady_FromIdle_Returns409AndDoesNotChangeStatus()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"MR-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/printers", UriKind.Relative),
            new RegisterPrinterRequest
            {
                Name = "Already Idle Printer",
                Brand = "Bambu",
                ModelCode = "P1S",
                IpAddress = "10.0.0.71",
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>();

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/printers/{printer!.Id}/mark-ready", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
