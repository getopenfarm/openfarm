using System.Net;
using FluentAssertions;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.IntegrationTests.Production;

public class AwaitingClearGatingTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Theory]
    [InlineData("complete")]
    [InlineData("fail")]
    [InlineData("cancel")]
    public async Task ManualTerminalOutcome_RequiresMarkReady(string action)
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync(manual: true);
        var run = await RunAsync(client, part.Id);
        var job = (await JobsAsync(run.Id)).Single();
        await AssignAsync(job.Id, printer.Id);
        await PostAsync(client, $"/api/v1/print-jobs/{job.Id}/{action}");
        (await PrinterStatusAsync(printer.Id)).Should().Be(PrinterStatus.AwaitingClear);
        await ReplayAsync(printer.Id, "IDLE");
        (await PrinterStatusAsync(printer.Id)).Should().Be(PrinterStatus.AwaitingClear);
        await PostAsync(client, $"/api/v1/printers/{printer.Id}/mark-ready");
        (await PrinterStatusAsync(printer.Id)).Should().Be(PrinterStatus.Idle);
    }

    [Fact]
    public async Task NamedRun_CompletesWithItsLastPlate_AndMarkReadyOnlyClearsThePlate()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync(manual: true);
        var run = await RunAsync(client, part.Id, name: "Named order");
        await AssignAsync((await JobsAsync(run.Id)).Single().Id, printer.Id);
        await PostAsync(client, $"/api/v1/print-jobs/{(await JobsAsync(run.Id)).Single().Id}/complete");
        (await DetailAsync(client, run.Id)).Status.Should().Be("Complete");
        await PostAsync(client, $"/api/v1/printers/{printer.Id}/mark-ready");
        await PostAsync(client, $"/api/v1/production-runs/{run.Id}/close");
        (await DetailAsync(client, run.Id)).Status.Should().Be("Closed");
    }
}
