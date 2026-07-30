using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Printers.Simulator;

namespace OpenFarm.Api.IntegrationTests.Printers;

/// <summary>
/// Backend-level validation of User Story 2 (job lifecycle) — no live UI trigger exists yet
/// (research.md §3), so <see cref="SimulatorPrinterConnector.StartSimulatedJob"/> is driven directly,
/// the same way a future job-dispatch command eventually will. Requires Docker (Testcontainers.PostgreSql).
/// </summary>
public class SimulatedPrintLifecycleTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    private static async Task<FleetPrinterResponse?> GetFleetEntryAsync(HttpClient client, Guid printerId)
    {
        var fleetResponse = await client.GetAsync(new Uri("/api/v1/printers", UriKind.Relative));
        var fleet = await fleetResponse.Content.ReadFromJsonAsync<ListFleetResponse>();
        return fleet!.Printers.FirstOrDefault(p => p.Id == printerId);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!await condition())
        {
            if (cts.IsCancellationRequested)
            {
                throw new TimeoutException($"Condition not met within {timeout}.");
            }

            await Task.Delay(20, CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartSimulatedJob_OnConnectedPrinter_TransitionsIdleToPrintingToAwaitingClear()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"LC-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/printers", UriKind.Relative),
            new RegisterPrinterRequest
            {
                Name = "Lifecycle Test Printer",
                Brand = "System",
                ModelCode = "Simulation",
                IpAddress = "10.0.0.90",
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>();

        await WaitUntilAsync(async () => (await GetFleetEntryAsync(client, printer!.Id))?.IsReachable == true, TimeSpan.FromSeconds(5));

        var connector = factory.Services.GetRequiredService<SimulatorPrinterConnector>();
        connector.StartSimulatedJob(serialNumber);

        await WaitUntilAsync(async () => (await GetFleetEntryAsync(client, printer!.Id))?.Status == "Printing", TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () => (await GetFleetEntryAsync(client, printer!.Id))?.Status == "AwaitingClear", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConnectedPrinter_WithNoStartSimulatedJobCall_NeverLeavesIdle()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"LC-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/printers", UriKind.Relative),
            new RegisterPrinterRequest
            {
                Name = "No Free-Run Test Printer",
                Brand = "System",
                ModelCode = "Simulation",
                IpAddress = "10.0.0.91",
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>();

        await WaitUntilAsync(async () => (await GetFleetEntryAsync(client, printer!.Id))?.IsReachable == true, TimeSpan.FromSeconds(5));

        for (var i = 0; i < 5; i++)
        {
            var entry = await GetFleetEntryAsync(client, printer!.Id);
            entry!.Status.Should().Be("Idle");
            await Task.Delay(50);
        }
    }
}
