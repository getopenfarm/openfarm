using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Simulator;

namespace OpenFarm.Api.IntegrationTests.Production;

public sealed class LiveProductionFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton(new SimulatorPrinterConnector(TimeSpan.FromSeconds(5))));
    }
}

public class LiveProductionWorkflowTests(LiveProductionFactory factory) : ProductionTestContext(factory), IClassFixture<LiveProductionFactory>
{
    [Fact]
    public async Task Simulator_AutomaticallyDispatches_ConfirmsControls_AndKeepsCancelledRunJobAlive()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        Factory.Services.GetRequiredService<IPrinterConnectionManager>().StartConnection(printer.Id, default);
        var run = await RunAsync(client, part.Id, 3, "Live simulator order");
        var firstId = (await JobsAsync(run.Id))[0].Id;
        await WaitForStatusAsync(client, firstId, "Printing");
        await PostAsync(client, $"/api/v1/print-jobs/{firstId}/pause", HttpStatusCode.Accepted);
        await WaitForStatusAsync(client, firstId, "Paused");
        await PostAsync(client, $"/api/v1/print-jobs/{firstId}/resume", HttpStatusCode.Accepted);
        await WaitForStatusAsync(client, firstId, "Printing");
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");
        await WaitForStatusAsync(client, firstId, "Completed");
        var jobs = await JobsAsync(run.Id);
        jobs.Single(j => j.Id == firstId).Filaments.Should().OnlyContain(c => c.SpoolId != null, "dispatch binds each line to the loaded spool");
        jobs.Count(j => j.Status == OpenFarm.Domain.Production.PrintJobStatus.Cancelled).Should().Be(2);
        (await DetailAsync(client, run.Id)).Status.Should().Be("Cancelled");
        (await PrinterStatusAsync(printer.Id)).Should().Be(OpenFarm.Domain.Printers.PrinterStatus.AwaitingClear);
        Factory.Services.GetRequiredService<IPrinterConnectionManager>().StopConnection(printer.Id);
    }

    private static async Task WaitForStatusAsync(HttpClient client, Guid id, string expected)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            var job = await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{id}");
            if (job?.Status == expected) return;
            await Task.Delay(50);
        }
        Assert.Fail($"Job never reached {expected}.");
    }
}
