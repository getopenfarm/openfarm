using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public class RemotePrintJobControlEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PauseResumeCancel_LiveRequestsWaitForTelemetry_ManualAppliesImmediately(bool manual)
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync(manual);
        var run = await RunAsync(client, part.Id);
        var id = (await JobsAsync(run.Id)).Single().Id;
        await AssignAsync(id, printer.Id);
        var expectedCode = manual ? HttpStatusCode.OK : HttpStatusCode.Accepted;
        foreach (var (action, before, after, telemetry) in new[] {
            ("pause", "Printing", "Paused", "PAUSE"),
            ("resume", "Paused", "Printing", "RUNNING"),
            ("cancel", "Printing", "Cancelled", "FAILED") })
        {
            var response = await client.PostAsJsonAsync($"/api/v1/print-jobs/{id}/{action}", new { });
            response.StatusCode.Should().Be(expectedCode, await response.Content.ReadAsStringAsync());
            (await response.Content.ReadFromJsonAsync<PrintJobResponse>())!.Status.Should().Be(manual ? after : before);
            if (!manual) await ReplayAsync(printer.Id, telemetry);
            (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{id}"))!.Status.Should().Be(after);
        }
        (await JobsAsync(run.Id)).Single().Status.Should().Be(PrintJobStatus.Cancelled);
    }
}
