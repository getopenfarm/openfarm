using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

/// <summary>A connector whose print-file read stays pending until the test lets it finish, counting how often it is asked.</summary>
public sealed class GatedFetchFactoryHolder : IPrinterConnectorFactory
{
    private readonly TaskCompletionSource<byte[]?> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public int Calls => _calls;

    public void Release() => _gate.TrySetResult(null);

    public IPrinterConnector CreateConnector(PrinterConnectionDetails details) => new Connector(this);

    public IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details) => throw new NotSupportedException();

    private sealed class Connector(GatedFetchFactoryHolder owner) : IPrinterConnector
    {
        public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.FromResult(true);

        public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(PrinterConnectionDetails details, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<byte[]?> FetchPrintFileAsync(PrinterConnectionDetails details, string? jobName, string? gcodeFile, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref owner._calls);
            return owner._gate.Task;
        }
    }
}

public sealed class GatedFetchFactory : ProductionTestFactory
{
    public GatedFetchFactoryHolder Connectors { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPrinterConnectorFactory>();
            services.AddSingleton<IPrinterConnectorFactory>(Connectors);
        });
    }
}

/// <summary>Reading a print's file back from the printer is slow, so it must not hold up telemetry, and runs once per print.</summary>
public class ExternalPrintFetchTests(GatedFetchFactory factory) : ProductionTestContext(factory), IClassFixture<GatedFetchFactory>
{
    private async Task ObserveRunningAsync(Guid printerId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var printer = await db.Printers.SingleAsync(p => p.Id == printerId);
        printer.ObserveReportedState("RUNNING", DateTimeOffset.UtcNow);
        printer.RecordTelemetry(null, null, 10, null, null, "benchy", [], null, null, null);
        await scope.ServiceProvider.GetRequiredService<ExternalPrintSync>().ObserveTelemetryAsync(printer, default);
    }

    [Fact]
    public async Task ASlowFileRead_DoesNotBlockTelemetry_AndIsStartedOnlyOnce()
    {
        await ClientAsync();
        var printer = await PrinterAsync();
        var gate = factory.Connectors;

        var timer = Stopwatch.StartNew();
        await ObserveRunningAsync(printer.Id);
        await ObserveRunningAsync(printer.Id);
        await ObserveRunningAsync(printer.Id);
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the read is still pending but telemetry carried on");

        await Task.Delay(100);
        gate.Calls.Should().Be(1, "later events must not start the read again");

        gate.Release();
        await Task.Delay(100);
        await ObserveRunningAsync(printer.Id);
        gate.Calls.Should().Be(1, "a file that could not be read is not retried for the same print");

        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().ExternalPrints.CountAsync(p => p.PrinterId == printer.Id))
            .Should().Be(0);
    }
}
