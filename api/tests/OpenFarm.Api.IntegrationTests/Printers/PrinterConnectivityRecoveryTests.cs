using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Printers;

namespace OpenFarm.Api.IntegrationTests.Printers;

public sealed class ConnectivityTestFactory : CustomWebApplicationFactory
{
    public RecoveryConnector Connector { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<IPrinterConnectorFactory>(new RecoveryConnectorFactory(Connector));
        });
    }

    private sealed class RecoveryConnectorFactory(RecoveryConnector connector) : IPrinterConnectorFactory
    {
        public IPrinterConnector CreateConnector(PrinterConnectionDetails details) => connector;
        public IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details) => new SilentCamera();
    }

    private sealed class SilentCamera : IPrinterCameraRelay
    {
        public async IAsyncEnumerable<byte[]> StreamFramesAsync(PrinterConnectionDetails details,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }
}

public sealed class RecoveryConnector : IPrinterConnector
{
    private readonly Channel<PrinterConnectorEvent> _events = Channel.CreateUnbounded<PrinterConnectorEvent>();
    private int _processed;
    public int Processed => Volatile.Read(ref _processed);
    public void Emit(PrinterTelemetrySnapshot telemetry, DateTimeOffset? observedAt = null) =>
        _events.Writer.TryWrite(new(observedAt ?? DateTimeOffset.UtcNow, Telemetry: telemetry));
    public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(PrinterConnectionDetails details,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var report in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return report;
            Interlocked.Increment(ref _processed);
        }
    }
    public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => throw new NotSupportedException();
}

public class PrinterConnectivityRecoveryTests(ConnectivityTestFactory factory) : IClassFixture<ConnectivityTestFactory>
{
    [Fact]
    public async Task SeparateDatabaseScopes_RecoverAfterSilence_AndPreserveThrottledDeltas()
    {
        await factory.EnsureTenantInfoResolvedAsync();
        var printer = Printer.Create(Organization.WellKnownId, "Recovery", "Bambu", "P1S", "127.0.0.1", $"REC-{Guid.NewGuid():N}");
        using (var scope = factory.CreateTenantScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPrinterRepository>().AddAsync(printer, default);
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            await scope.ServiceProvider.GetRequiredService<IConnectionSecretRepository>().AddAsync(
                ConnectionSecret.Create(printer.OrganizationId, printer.SerialNumber, protector.Protect("test-code")), default);
        }
        using var service = new PrinterConnectionBackgroundService(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance, TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(30));
        await service.StartAsync(default);
        try
        {
            factory.Connector.Emit(new(210, 60, 10, null, null, null, []));
            await WaitUntilAsync(async () => (await ReadAsync(printer.Id)).IsReachable);
            await WaitUntilAsync(async () => !(await ReadAsync(printer.Id)).IsReachable);
            var recoveredAt = DateTimeOffset.UtcNow;
            factory.Connector.Emit(new(null, null, 20, null, null, null, []), recoveredAt);
            await WaitUntilAsync(async () => (await ReadAsync(printer.Id)).IsReachable);
            (await ReadAsync(printer.Id)).NozzleTempC.Should().Be(210);

            factory.Connector.Emit(new(215, null, null, null, null, null, []), recoveredAt.AddMilliseconds(10));
            await WaitUntilAsync(() => Task.FromResult(factory.Connector.Processed >= 3));
            (await ReadAsync(printer.Id)).NozzleTempC.Should().Be(210, "the intermediate delta is throttled");
            factory.Connector.Emit(new(null, null, 42, null, null, null, []), recoveredAt.AddMilliseconds(200));
            await WaitUntilAsync(async () => (await ReadAsync(printer.Id)).PrintProgressPercent == 42);
            (await ReadAsync(printer.Id)).NozzleTempC.Should().Be(215);
        }
        finally { await service.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    private async Task<Printer> ReadAsync(Guid id)
    {
        using var scope = factory.CreateTenantScope();
        return (await scope.ServiceProvider.GetRequiredService<IPrinterRepository>().GetByIdAsync(id, default))!;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await predicate()) await Task.Delay(10, timeout.Token);
    }
}
