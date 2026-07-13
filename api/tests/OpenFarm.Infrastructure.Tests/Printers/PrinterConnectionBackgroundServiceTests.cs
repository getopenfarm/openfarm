using Finbuckle.MultiTenant.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Multitenancy;
using OpenFarm.Infrastructure.Printers;

namespace OpenFarm.Infrastructure.Tests.Printers;

public class PrinterConnectionBackgroundServiceTests
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan UnreachableThreshold = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task ReachabilitySweep_FlipsUnreachableAfterSilence_AndBackOnNextMessage()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer 1", "Bambu", "P1S", "10.0.0.50", "SN-REACH-1");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new FakePrinterConnector();

        var provider = BuildServiceProvider(printer, secret, connector, out _);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            UnreachableThreshold);

        await service.StartAsync(CancellationToken.None);
        try
        {
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(DateTimeOffset.UtcNow));
            await WaitUntilAsync(() => printer.IsReachable, TimeSpan.FromSeconds(2));
            printer.IsReachable.Should().BeTrue();

            await WaitUntilAsync(() => !printer.IsReachable, TimeSpan.FromSeconds(2));
            printer.IsReachable.Should().BeFalse();

            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(DateTimeOffset.UtcNow));
            await WaitUntilAsync(() => printer.IsReachable, TimeSpan.FromSeconds(2));
            printer.IsReachable.Should().BeTrue();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ConnectorReportedStatusChange_ReconcilesAndWritesActivityLogEntry()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer 1", "Bambu", "P1S", "10.0.0.50", "SN-REACH-2");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new FakePrinterConnector();

        var provider = BuildServiceProvider(printer, secret, connector, out var activityLogWriter);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            UnreachableThreshold);

        await service.StartAsync(CancellationToken.None);
        try
        {
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(DateTimeOffset.UtcNow, GcodeState: "RUNNING"));

            await WaitUntilAsync(() => printer.Status == PrinterStatus.Printing, TimeSpan.FromSeconds(2));

            printer.Status.Should().Be(PrinterStatus.Printing);
            activityLogWriter.Entries.Should().ContainSingle(e =>
                e.EntityType == "Printer" &&
                e.EntityId == printer.Id.ToString() &&
                e.EventType == "PrinterStatusObserved" &&
                e.ActorType == ActorType.System &&
                e.ActorId == null);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopThenImmediatelyStartConnection_DuringSlowUnwind_DoesNotOrphanTheNewLoop()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer 1", "Bambu", "P1S", "10.0.0.50", "SN-RACE-1");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new SlowCancelPrinterConnector { CancellationLag = TimeSpan.FromMilliseconds(150) };

        var provider = BuildServiceProvider(printer, secret, connector, out _);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            UnreachableThreshold);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => connector.ConnectCallCount >= 1, TimeSpan.FromSeconds(2));

            // Mirrors EditPrinterConnectionCommandHandler: Stop immediately followed by Start, with
            // no synchronization between them and no wait for the old loop to actually unwind.
            service.StopConnection(printer.Id);
            service.StartConnection(printer.Id, CancellationToken.None);

            await WaitUntilAsync(() => connector.ConnectCallCount >= 2, TimeSpan.FromSeconds(2));

            // Give the old (slow-to-unwind) loop's `finally` plenty of time to run.
            await Task.Delay(connector.CancellationLag + TimeSpan.FromMilliseconds(300));

            // StartConnection is documented as a no-op when the printer is already connected. If
            // the old loop's `finally` wrongly tore down the new loop's dictionary entry (the bug
            // this test guards against), that entry would be gone and this call would succeed in
            // starting a second, duplicate connector loop instead of no-op'ing.
            service.StartConnection(printer.Id, CancellationToken.None);
            await Task.Delay(100);

            connector.ConnectCallCount.Should().Be(2);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider BuildServiceProvider(
        Printer printer,
        ConnectionSecret secret,
        IPrinterConnector connector,
        out RecordingActivityLogWriter activityLogWriter)
    {
        var writer = new RecordingActivityLogWriter();
        activityLogWriter = writer;

        var services = new ServiceCollection();
        services.AddSingleton<IPrinterRepository>(new InMemoryPrinterRepository(printer));
        services.AddSingleton<IConnectionSecretRepository>(new InMemoryConnectionSecretRepository(secret));
        services.AddSingleton<ISecretProtector>(new PassthroughSecretProtector());
        services.AddSingleton<IPrinterConnector>(connector);
        services.AddSingleton<IActivityLogWriter>(writer);
        services.AddSingleton<IPrinterEventPublisher, NoOpPrinterEventPublisher>();
        // Test printers use BinaryImage-camera models (e.g. P1S), so RunConnectionLoopAsync always
        // starts a concurrent camera loop too — register the fake relay so that path doesn't throw.
        services.AddSingleton<IPrinterCameraRelay, FakePrinterCameraRelay>();

        // PrinterConnectionBackgroundService resolves these to populate tenant context per scope
        // (Finbuckle's HTTP-middleware-based resolution never runs for a scope it creates itself).
        var tenantInfo = new AppTenantInfo { Id = printer.OrganizationId.ToString(), Identifier = "default" };
        services.AddSingleton<IMultiTenantStore<AppTenantInfo>>(new StubTenantStore(tenantInfo));
        services.AddSingleton<AsyncLocalMultiTenantContextAccessor<AppTenantInfo>>();
        services.AddSingleton<IMultiTenantContextSetter>(sp => sp.GetRequiredService<AsyncLocalMultiTenantContextAccessor<AppTenantInfo>>());

        return services.BuildServiceProvider();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            if (cts.IsCancellationRequested)
            {
                throw new TimeoutException($"Condition not met within {timeout}.");
            }

            await Task.Delay(10, CancellationToken.None);
        }
    }

    private sealed class InMemoryPrinterRepository(params Printer[] printers) : IPrinterRepository
    {
        private readonly Dictionary<Guid, Printer> _printers = printers.ToDictionary(p => p.Id);

        public Task AddAsync(Printer printer, CancellationToken cancellationToken)
        {
            _printers[printer.Id] = printer;
            return Task.CompletedTask;
        }

        public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_printers.GetValueOrDefault(id));

        public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) =>
            Task.FromResult(_printers.Values.FirstOrDefault(p => p.SerialNumber == serialNumber));

        public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Printer>>(_printers.Values.ToList());

        public Task RemoveAsync(Printer printer, CancellationToken cancellationToken)
        {
            _printers.Remove(printer.Id);
            return Task.CompletedTask;
        }

        // Printer instances are mutated in place (shared reference), so there's nothing to flush.
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryConnectionSecretRepository(ConnectionSecret secret) : IConnectionSecretRepository
    {
        public Task AddAsync(ConnectionSecret connectionSecret, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ConnectionSecret>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConnectionSecret>>([secret]);

        public Task<ConnectionSecret?> GetByPrinterSerialNumberAsync(string printerSerialNumber, CancellationToken cancellationToken) =>
            Task.FromResult(secret.PrinterSerialNumber == printerSerialNumber ? secret : null);
    }

    /// <summary>
    /// Never observes cancellation to stop yielding immediately — after <see cref="CancellationLag"/>
    /// once cancellation is requested, simulating a connector whose underlying transport takes a
    /// moment to unwind (e.g. a live socket read that must return before the loop notices it was
    /// cancelled). Widens the Stop-then-Start race window enough for a test to deterministically
    /// land inside it.
    /// </summary>
    private sealed class SlowCancelPrinterConnector : IPrinterConnector
    {
        private int _connectCallCount;

        public TimeSpan CancellationLag { get; init; } = TimeSpan.FromMilliseconds(150);

        public int ConnectCallCount => _connectCallCount;

        public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(
            PrinterConnectionDetails details,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _connectCallCount);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(5, CancellationToken.None);
            }

            await Task.Delay(CancellationLag, CancellationToken.None);
            yield break;
        }
    }

    private sealed class PassthroughSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class RecordingActivityLogWriter : IActivityLogWriter
    {
        public List<ActivityLogEntry> Entries { get; } = [];

        public Task WriteAsync(ActivityLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpPrinterEventPublisher : IPrinterEventPublisher
    {
        public Task PrinterStatusChangedAsync(Guid organizationId, Guid printerId, PrinterStatus status, bool isReachable, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PrinterTelemetryUpdatedAsync(Guid organizationId, Guid printerId, PrinterTelemetrySnapshot telemetry, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CameraFrameReceivedAsync(Guid organizationId, Guid printerId, string frameJpegBase64, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubTenantStore(AppTenantInfo tenantInfo) : IMultiTenantStore<AppTenantInfo>
    {
        public Task<AppTenantInfo?> GetAsync(string id) => Task.FromResult<AppTenantInfo?>(tenantInfo);

        public Task<AppTenantInfo?> GetByIdentifierAsync(string identifier) => Task.FromResult<AppTenantInfo?>(tenantInfo);

        public Task<IEnumerable<AppTenantInfo>> GetAllAsync() => Task.FromResult<IEnumerable<AppTenantInfo>>([tenantInfo]);

        public Task<IEnumerable<AppTenantInfo>> GetAllAsync(int take, int skip) => Task.FromResult<IEnumerable<AppTenantInfo>>([tenantInfo]);

        public Task<bool> AddAsync(AppTenantInfo tenantInfoToAdd) => Task.FromResult(true);

        public Task<bool> UpdateAsync(AppTenantInfo tenantInfoToUpdate) => Task.FromResult(true);

        public Task<bool> RemoveAsync(string identifier) => Task.FromResult(true);
    }
}
