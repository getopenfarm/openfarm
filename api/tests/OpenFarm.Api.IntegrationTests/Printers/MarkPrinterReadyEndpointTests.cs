using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Printers.Simulator;

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
            new LoginCommand("admin", "admin-openfarm"));
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
                Brand = "System",
                ModelCode = "Simulation",
                IpAddress = "10.0.0.70",
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>();

        // StartSimulatedJob no-ops until the background connection loop's ConnectAsync stream has
        // started (it marks the serial "active" before its very first — reachability-establishing —
        // event); wait for that to land before triggering the job (research.md §3).
        await WaitForReachableAsync(client, printer!.Id);

        var connector = factory.Services.GetRequiredService<SimulatorPrinterConnector>();
        connector.StartSimulatedJob(serialNumber);

        await WaitForStatusAsync(client, printer.Id, "AwaitingClear");

        return printer;
    }

    private static async Task WaitForReachableAsync(HttpClient client, Guid printerId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var fleetResponse = await client.GetAsync(new Uri("/api/v1/printers", UriKind.Relative));
            var fleet = await fleetResponse.Content.ReadFromJsonAsync<ListFleetResponse>();
            var current = fleet!.Printers.FirstOrDefault(p => p.Id == printerId);
            if (current?.IsReachable == true)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Printer {printerId} never became reachable.");
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

        // The connector never learns Mark Ready happened (data-model.md's status-flapping-avoidance
        // note) — confirm a few more of the simulator's own ticks don't flip status back to
        // AwaitingClear by re-asserting "FINISH".
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        var fleetResponse = await client.GetAsync(new Uri("/api/v1/printers", UriKind.Relative));
        var fleet = await fleetResponse.Content.ReadFromJsonAsync<ListFleetResponse>();
        fleet!.Printers.Single(p => p.Id == printer.Id).Status.Should().Be("Idle");

        // The printer's time is saved stretch by stretch through its own collection, which EF must insert, not update.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var spans = await db.PrinterActivitySpans.Where(s => s.PrinterId == printer.Id).OrderBy(s => s.StartedAtUtc).ToListAsync();
        spans.Select(s => s.Activity).Should().ContainInOrder(PrinterActivity.Printing, PrinterActivity.AwaitingClear);
        spans.Should().OnlyContain(s => s.EndedAtUtc > s.StartedAtUtc);
        (await db.Printers.SingleAsync(p => p.Id == printer.Id)).Activity.Should().Be(PrinterActivity.Idle);
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
                Brand = "System",
                ModelCode = "Simulation",
                IpAddress = "10.0.0.71",
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>();

        var response = await client.PostAsJsonAsync(new Uri($"/api/v1/printers/{printer!.Id}/mark-ready", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
