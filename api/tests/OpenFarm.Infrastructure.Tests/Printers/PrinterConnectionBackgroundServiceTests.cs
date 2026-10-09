using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Finbuckle.MultiTenant.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Multitenancy;
using OpenFarm.Infrastructure.Printers;
using OpenFarm.Infrastructure.Tests.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers;

public class PrinterConnectionBackgroundServiceTests
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan UnreachableThreshold = TimeSpan.FromMilliseconds(150);

    // Effectively "always flush" — keeps tests that aren't specifically about coalescing behaving
    // like the pre-throttle code (every event flushes), so they stay deterministic.
    private static readonly TimeSpan NoFlushThrottling = TimeSpan.Zero;

    [Fact]
    public async Task RealMqttReports_SilenceAndRecovery_UpdateReachabilityAndPreserveMergedTelemetry()
    {
        await using var harness = new BambuMqttHarness
        {
            InitialReport = """{"print":{"gcode_state":"RUNNING","nozzle_temper":220,"bed_temper":60,"mc_percent":10}}""",
        };
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Protocol test", "Bambu", "P1S", harness.Details.IpAddress, harness.Details.SerialNumber);
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, harness.Details.AccessCode);
        using var provider = BuildServiceProvider(printer, secret, harness.CreateConnector(), out _);
        using var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            TimeSpan.FromMilliseconds(500),
            NoFlushThrottling);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await service.StartAsync(lifetime.Token);
        try
        {
            await WaitUntilAsync(() => printer.IsReachable && printer.NozzleTempC == 220, TimeSpan.FromSeconds(3));
            printer.Status.Should().Be(PrinterStatus.Printing);
            await WaitUntilAsync(() => !printer.IsReachable, TimeSpan.FromSeconds(3));
            printer.Status.Should().Be(PrinterStatus.Printing, "silence only changes reachability");
            printer.NozzleTempC.Should().Be(220);

            await harness.PublishReportAsync("""{"print":{"mc_percent":42}}""");
            await WaitUntilAsync(() => printer.IsReachable && printer.PrintProgressPercent == 42, TimeSpan.FromSeconds(3));
            printer.NozzleTempC.Should().Be(220);
            printer.BedTempC.Should().Be(60);
            printer.Status.Should().Be(PrinterStatus.Printing);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
            await BambuLanProtocolTests.WaitUntilAsync(() => harness.ActiveConnections == 0);
        }
    }

    [Fact]
    public async Task ReachabilitySweep_FlipsUnreachableAfterSilence_AndBackOnNextMessage()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer 1", "Bambu", "P1S", "10.0.0.50", "SN-REACH-1");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new EmittingPrinterConnector();

        var provider = BuildServiceProvider(printer, secret, connector, out _);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            UnreachableThreshold,
            NoFlushThrottling);

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
        var connector = new EmittingPrinterConnector();

        var provider = BuildServiceProvider(printer, secret, connector, out var activityLogWriter);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            UnreachableThreshold,
            NoFlushThrottling);

        await service.StartAsync(CancellationToken.None);
        try
        {
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(DateTimeOffset.UtcNow, GcodeState: "RUNNING"));

            await WaitUntilAsync(
                () => printer.Status == PrinterStatus.Printing && activityLogWriter.Entries.Count > 0,
                TimeSpan.FromSeconds(2));

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
    public async Task TelemetryEvents_WithSubsequentPartialDelta_BroadcastsMergedState()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer 1", "Bambu", "P1S", "10.0.0.50", "SN-TELEMETRY-1");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new EmittingPrinterConnector();
        var publisher = new RecordingPrinterEventPublisher();

        var provider = BuildServiceProvider(printer, secret, connector, out _, publisher);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            UnreachableThreshold,
            NoFlushThrottling);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var amsUnits = new List<AmsUnit> { new(0, []) };
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(
                DateTimeOffset.UtcNow,
                Telemetry: new PrinterTelemetrySnapshot(60.5m, 65.0m, 42, "Printing", 72, "benchy.3mf", amsUnits)));

            await WaitUntilAsync(() => publisher.TelemetryBroadcasts.Count >= 1, TimeSpan.FromSeconds(2));
            publisher.TelemetryBroadcasts[0].NozzleTempC.Should().Be(60.5m);
            publisher.TelemetryBroadcasts[0].BedTempC.Should().Be(65.0m);

            // Bambu's next delta only reports progress advancing — nozzle/bed/AMS are omitted
            // because they haven't changed. The broadcast should still carry the last-known values.
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(
                DateTimeOffset.UtcNow,
                Telemetry: new PrinterTelemetrySnapshot(null, null, 50, null, null, null, [])));

            await WaitUntilAsync(() => publisher.TelemetryBroadcasts.Count >= 2, TimeSpan.FromSeconds(2));
            var secondBroadcast = publisher.TelemetryBroadcasts[1];
            secondBroadcast.NozzleTempC.Should().Be(60.5m);
            secondBroadcast.BedTempC.Should().Be(65.0m);
            secondBroadcast.PrintProgressPercent.Should().Be(50);
            secondBroadcast.CurrentStageLabel.Should().Be("Printing");
            secondBroadcast.AmsUnits.Should().BeEquivalentTo(amsUnits);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TelemetryEvents_WithinFlushInterval_AreCoalescedIntoOneBroadcast()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer 1", "Bambu", "P1S", "10.0.0.50", "SN-TELEMETRY-2");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new EmittingPrinterConnector();
        var publisher = new RecordingPrinterEventPublisher();
        var flushInterval = TimeSpan.FromMilliseconds(300);
        // Coalescing is decided from each event's own ReceivedAtUtc relative to the last flush, so the
        // three events below carry explicit timestamps rather than DateTimeOffset.UtcNow — that keeps
        // the flush-window math independent of how fast a (possibly slow CI) runner processes them.
        // A long unreachable threshold likewise stops the reachability sweep from flipping the printer
        // offline between events (which would flush event 2 as a reachability-restore, not coalesce it),
        // keeping this test purely about flush throttling.
        var baseTime = DateTimeOffset.UtcNow;
        var noSweepInterference = TimeSpan.FromSeconds(30);

        var provider = BuildServiceProvider(printer, secret, connector, out _, publisher);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            noSweepInterference,
            flushInterval);

        await service.StartAsync(CancellationToken.None);
        try
        {
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(
                baseTime,
                Telemetry: new PrinterTelemetrySnapshot(60m, 65m, 10, "Printing", 90, "benchy.3mf", [])));
            await WaitUntilAsync(() => publisher.TelemetryBroadcasts.Count >= 1, TimeSpan.FromSeconds(2));

            // Timestamped inside the flush interval — should update the in-memory printer but not
            // trigger a second DB write / broadcast yet (the MQTT-message-rate DB churn this
            // throttle exists to prevent).
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(
                baseTime.AddMilliseconds(100),
                Telemetry: new PrinterTelemetrySnapshot(61m, 66m, 20, "Printing", 80, "benchy.3mf", [])));
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            publisher.TelemetryBroadcasts.Should().HaveCount(1, "the second event landed inside the flush interval and should be coalesced");

            // Timestamped past the flush interval — the next event flushes the merged state.
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(
                baseTime.Add(flushInterval).AddMilliseconds(100),
                Telemetry: new PrinterTelemetrySnapshot(62m, 67m, 30, "Printing", 70, "benchy.3mf", [])));

            await WaitUntilAsync(() => publisher.TelemetryBroadcasts.Count >= 2, TimeSpan.FromSeconds(2));
            var secondBroadcast = publisher.TelemetryBroadcasts[1];
            secondBroadcast.NozzleTempC.Should().Be(62m);
            secondBroadcast.BedTempC.Should().Be(67m);
            secondBroadcast.PrintProgressPercent.Should().Be(30);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StatusChange_MidFlushInterval_FlushesImmediatelyDespiteThrottle()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer 1", "Bambu", "P1S", "10.0.0.50", "SN-TELEMETRY-3");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new EmittingPrinterConnector();
        var publisher = new RecordingPrinterEventPublisher();

        var provider = BuildServiceProvider(printer, secret, connector, out var activityLogWriter, publisher);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            UnreachableThreshold,
            TimeSpan.FromSeconds(30)); // large enough that only the status change should force a flush

        await service.StartAsync(CancellationToken.None);
        try
        {
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(
                DateTimeOffset.UtcNow,
                Telemetry: new PrinterTelemetrySnapshot(60m, 65m, 10, "Printing", 90, "benchy.3mf", [])));
            await WaitUntilAsync(() => publisher.TelemetryBroadcasts.Count >= 1, TimeSpan.FromSeconds(2));

            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(DateTimeOffset.UtcNow, GcodeState: "RUNNING"));

            await WaitUntilAsync(
                () => printer.Status == PrinterStatus.Printing && activityLogWriter.Entries.Count > 0,
                TimeSpan.FromSeconds(2));
            activityLogWriter.Entries.Should().ContainSingle(e => e.EventType == "PrinterStatusObserved");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task CameraFrames_NeverTouchPrinterRepositoryOrTriggerDbWrite()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer 1", "Bambu", "P1S", "10.0.0.50", "SN-CAMERA-1");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new EmittingPrinterConnector(); // never emits an MQTT event in this test
        var publisher = new RecordingPrinterEventPublisher();
        var repository = new SaveCountingPrinterRepository(new InMemoryPrinterRepository(printer));
        var cameraRelay = new EmittingCameraRelay([1, 2, 3], [4, 5, 6]);

        var provider = BuildServiceProvider(printer, secret, connector, out _, publisher, repository, cameraRelay);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            UnreachableThreshold,
            NoFlushThrottling);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => publisher.CameraFramesReceived.Count >= 2, TimeSpan.FromSeconds(2));

            publisher.CameraFramesReceived.Should().Equal(
                Convert.ToBase64String([1, 2, 3]),
                Convert.ToBase64String([4, 5, 6]));
            repository.SaveChangesCallCount.Should().Be(1, "only starting the telemetry connection writes the unconfirmed nozzle state; camera frames add no writes");
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
            UnreachableThreshold,
            NoFlushThrottling);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => connector.ConnectCallCount >= 1, TimeSpan.FromSeconds(2));

            // Mirrors EditPrinterCommandHandler: Stop immediately followed by Start, with
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

    /// <summary>
    /// A printer's first connection populates its MultiMaterialUnit/ExternalSpool FeedUnit/FeedSlot
    /// rows; a topology shrink (fewer reported trays) soft-deactivates the missing slot without
    /// removing the row (spec.md's Edge Case). Uses the real <see cref="FeedSlotTelemetrySyncHandler"/>
    /// against an in-memory <see cref="IFeedUnitRepository"/> double.
    /// </summary>
    [Fact]
    public async Task FeedSlotTelemetrySync_FirstConnectionThenTopologyShrink_PopulatesThenSoftDeactivates()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer 1", "Bambu", "P1S", "10.0.0.50", "SN-FEEDUNIT-1");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new EmittingPrinterConnector();
        var feedUnitRepository = new InMemoryFeedUnitRepository();
        var sync = new FeedSlotTelemetrySyncHandler(feedUnitRepository);

        var provider = BuildServiceProvider(printer, secret, connector, out _, feedSlotTelemetrySync: sync);
        var service = new PrinterConnectionBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance,
            SweepInterval,
            UnreachableThreshold,
            NoFlushThrottling);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var twoTrayAms = new List<AmsUnit>
            {
                new(0, [new AmsTray(0, false, "PLA", "#FF0000", "Generic", 80, false), new AmsTray(1, false, "PETG", "#00FF00", "Generic", 50, false)]),
            };
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(
                DateTimeOffset.UtcNow,
                Telemetry: new PrinterTelemetrySnapshot(60m, 65m, 10, "Printing", null, null, twoTrayAms)));

            await WaitUntilAsync(() => feedUnitRepository.Snapshot.Any(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit), TimeSpan.FromSeconds(2));

            var unit = feedUnitRepository.Snapshot.Single(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit);
            unit.FeedSlots.Should().HaveCount(2);
            unit.FeedSlots.Should().OnlyContain(s => s.DeactivatedAtUtc == null);

            // Topology shrinks to one tray.
            var oneTrayAms = new List<AmsUnit> { new(0, [new AmsTray(0, false, "PLA", "#FF0000", "Generic", 80, false)]) };
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(
                DateTimeOffset.UtcNow,
                Telemetry: new PrinterTelemetrySnapshot(60m, 65m, 20, "Printing", null, null, oneTrayAms)));

            await WaitUntilAsync(() => unit.FeedSlots.Any(s => s.DeactivatedAtUtc != null), TimeSpan.FromSeconds(2));

            unit.FeedSlots.Should().HaveCount(2, "the row is soft-deactivated, never removed");
            unit.FeedSlots.Single(s => s.SlotPosition == 1).DeactivatedAtUtc.Should().NotBeNull();
            unit.FeedSlots.Single(s => s.SlotPosition == 0).DeactivatedAtUtc.Should().BeNull();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExactStateAndAlerts_InsideThrottle_LogAndBroadcastEveryChange()
    {
        var organizationId = Guid.NewGuid();
        var printer = Printer.Create(organizationId, "Printer", "Bambu", "P1S", "10.0.0.1", "SN-EXACT");
        var secret = ConnectionSecret.Create(organizationId, printer.SerialNumber, "encrypted-code");
        var connector = new EmittingPrinterConnector();
        var publisher = new RecordingPrinterEventPublisher();
        using var provider = BuildServiceProvider(printer, secret, connector, out var writer, publisher);
        var service = new PrinterConnectionBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PrinterConnectionBackgroundService>.Instance, SweepInterval, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        await service.StartAsync(CancellationToken.None);
        try
        {
            var at = DateTimeOffset.UtcNow;
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(at, GcodeState: "RUNNING"));
            await WaitUntilAsync(() => publisher.TelemetryBroadcasts.Count == 1, TimeSpan.FromSeconds(2));
            var warning = new PrinterAlert("Hms", "0300_0100_0002_0001", "Warning", "Test warning");
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(at.AddMilliseconds(1), GcodeState: "PAUSE", HmsAlerts: [warning]));
            await WaitUntilAsync(() => publisher.TelemetryBroadcasts.Count == 2, TimeSpan.FromSeconds(2));
            printer.Status.Should().Be(PrinterStatus.Printing);
            publisher.TelemetryBroadcasts.Last().ReportedState.Should().Be("PAUSE");
            publisher.TelemetryBroadcasts.Last().ActiveAlerts.Should().Equal(warning);
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(at.AddMilliseconds(2), GcodeState: "RUNNING", HmsAlerts: []));
            await WaitUntilAsync(() => publisher.TelemetryBroadcasts.Count == 3, TimeSpan.FromSeconds(2));
            writer.Entries.Where(e => e.EventType == "PrinterStatusObserved").Select(e => e.Details!.ReportedState)
                .Should().Equal("RUNNING", "PAUSE", "RUNNING");
            writer.Entries.Should().HaveCount(5);
            writer.Entries.Last().Details!.PreviousAlerts.Should().Equal(warning);
            writer.Entries.Last().Details!.ActiveAlerts.Should().BeEmpty();
            publisher.ActivityNotifications.Should().HaveCount(3);
            connector.Emit(printer.SerialNumber, new PrinterConnectorEvent(at.AddMilliseconds(3), GcodeState: "RUNNING", HmsAlerts: []));
            await Task.Delay(100);
            writer.Entries.Should().HaveCount(5);
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    private static ServiceProvider BuildServiceProvider(
        Printer printer,
        ConnectionSecret secret,
        IPrinterConnector connector,
        out RecordingActivityLogWriter activityLogWriter,
        IPrinterEventPublisher? publisher = null,
        IPrinterRepository? repository = null,
        IPrinterCameraRelay? cameraRelay = null,
        IFeedSlotTelemetrySync? feedSlotTelemetrySync = null)
    {
        var writer = new RecordingActivityLogWriter();
        activityLogWriter = writer;

        var services = new ServiceCollection();
        services.AddSingleton<IPrinterRepository>(repository ?? new InMemoryPrinterRepository(printer));
        services.AddSingleton<IConnectionSecretRepository>(new InMemoryConnectionSecretRepository(secret));
        services.AddSingleton<ISecretProtector>(new PassthroughSecretProtector());
        services.AddSingleton<IActivityLogWriter>(writer);
        services.AddSingleton<IPrinterEventPublisher>(publisher ?? new NoOpPrinterEventPublisher());
        services.AddSingleton<IFeedSlotTelemetrySync>(feedSlotTelemetrySync ?? new NoOpFeedSlotTelemetrySync());
        services.AddSingleton<IJobTelemetrySync>(new NoOpJobTelemetrySync());
        services.AddSingleton<IProductionDispatcher>(new NoOpProductionDispatcher());
        // Test printers use BinaryImage-camera models (e.g. P1S), so RunConnectionLoopAsync always
        // starts a concurrent camera loop too — the stub factory below hands out a relay that never
        // emits by default so that path doesn't throw.
        services.AddSingleton<IPrinterConnectorFactory>(new StubPrinterConnectorFactory(connector, cameraRelay ?? new SilentCameraRelay()));

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

        public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>Hands back the fixed connector/camera-relay instances the test constructed, regardless of details.</summary>
    private sealed class StubPrinterConnectorFactory(IPrinterConnector connector, IPrinterCameraRelay cameraRelay) : IPrinterConnectorFactory
    {
        public IPrinterConnector CreateConnector(PrinterConnectionDetails details) => connector;

        public IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details) => cameraRelay;
    }

    /// <summary>
    /// Test-only push-based connector double for exercising <see cref="PrinterConnectionBackgroundService"/>'s
    /// generic event handling (reachability, status reconciliation, telemetry coalescing) with arbitrary,
    /// hand-constructed events — the same shape the retired production <c>FakePrinterConnector</c> used.
    /// </summary>
    private sealed class EmittingPrinterConnector : IPrinterConnector
    {
        private readonly ConcurrentDictionary<string, Channel<PrinterConnectorEvent>> _channels = new();

        public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(
            PrinterConnectionDetails details,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var channel = GetOrCreateChannel(details.SerialNumber);
            await foreach (var connectorEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return connectorEvent;
            }
        }

        public void Emit(string serialNumber, PrinterConnectorEvent connectorEvent) =>
            GetOrCreateChannel(serialNumber).Writer.TryWrite(connectorEvent);

        public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Channel<PrinterConnectorEvent> GetOrCreateChannel(string serialNumber) =>
            _channels.GetOrAdd(serialNumber, static _ => Channel.CreateUnbounded<PrinterConnectorEvent>());
    }

    /// <summary>Never emits a frame — the default camera relay for tests unrelated to camera behavior.</summary>
    private sealed class SilentCameraRelay : IPrinterCameraRelay
    {
        public async IAsyncEnumerable<byte[]> StreamFramesAsync(
            PrinterConnectionDetails details,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource();
            using (cancellationToken.Register(() => completion.TrySetResult()))
            {
                await completion.Task;
            }

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

    private sealed class RecordingPrinterEventPublisher : IPrinterEventPublisher
    {
        public List<Guid> ActivityNotifications { get; } = [];
        public Task PrinterActivityChangedAsync(Guid organizationId, Guid printerId, CancellationToken cancellationToken)
        {
            ActivityNotifications.Add(printerId);
            return Task.CompletedTask;
        }

        public List<PrinterTelemetrySnapshot> TelemetryBroadcasts { get; } = [];

        public List<string> CameraFramesReceived { get; } = [];

        public Task PrinterStatusChangedAsync(Guid organizationId, Guid printerId, PrinterStatus status, bool isReachable, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PrinterTelemetryUpdatedAsync(Guid organizationId, Guid printerId, PrinterTelemetrySnapshot telemetry, CancellationToken cancellationToken)
        {
            TelemetryBroadcasts.Add(telemetry);
            return Task.CompletedTask;
        }

        public Task CameraFrameReceivedAsync(Guid organizationId, Guid printerId, string frameJpegBase64, CancellationToken cancellationToken)
        {
            CameraFramesReceived.Add(frameJpegBase64);
            return Task.CompletedTask;
        }
    }

    /// <summary>Yields a fixed set of frames once, then stays alive (like a real relay) until cancelled.</summary>
    private sealed class EmittingCameraRelay(params byte[][] frames) : IPrinterCameraRelay
    {
        public async IAsyncEnumerable<byte[]> StreamFramesAsync(
            PrinterConnectionDetails details,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var frame in frames)
            {
                yield return frame;
            }

            var completion = new TaskCompletionSource();
            using (cancellationToken.Register(() => completion.TrySetResult()))
            {
                await completion.Task;
            }
        }
    }

    private sealed class SaveCountingPrinterRepository(IPrinterRepository inner) : IPrinterRepository
    {
        public int SaveChangesCallCount { get; private set; }

        public Task AddAsync(Printer printer, CancellationToken cancellationToken) =>
            inner.AddAsync(printer, cancellationToken);

        public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) =>
            inner.GetBySerialNumberAsync(serialNumber, cancellationToken);

        public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) =>
            inner.ListAsync(cancellationToken);

        public Task RemoveAsync(Printer printer, CancellationToken cancellationToken) =>
            inner.RemoveAsync(printer, cancellationToken);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCallCount++;
            return inner.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class NoOpFeedSlotTelemetrySync : IFeedSlotTelemetrySync
    {
        public Task SyncAsync(Guid printerId, Guid organizationId, string brand, IReadOnlyList<AmsUnit> amsUnits, AmsTray? externalSpoolTray, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoOpJobTelemetrySync : IJobTelemetrySync
    {
        public Task ObserveAsync(Guid printerId, string gcodeState, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpProductionDispatcher : IProductionDispatcher
    {
        public void RequestDispatchAttempt(Guid printerId)
        {
        }
    }

    private sealed class InMemoryFeedUnitRepository : IFeedUnitRepository
    {
        private readonly List<FeedUnit> _feedUnits = [];

        public IReadOnlyList<FeedUnit> Snapshot => _feedUnits.ToList();

        public Task AddAsync(FeedUnit feedUnit, CancellationToken cancellationToken)
        {
            _feedUnits.Add(feedUnit);
            return Task.CompletedTask;
        }

        public Task<FeedUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_feedUnits.FirstOrDefault(u => u.Id == id));

        public Task<IReadOnlyList<FeedUnit>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FeedUnit>>(_feedUnits.ToList());

        public Task<IReadOnlyList<FeedUnit>> GetByPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FeedUnit>>(_feedUnits.Where(u => u.PrinterId == printerId).ToList());

        public Task<FeedUnit?> FindByPrinterKindUnitIndexAsync(Guid printerId, string kind, int unitIndex, CancellationToken cancellationToken) =>
            Task.FromResult(_feedUnits.FirstOrDefault(u => u.PrinterId == printerId && u.Kind == kind && u.UnitIndex == unitIndex));

        public Task<FeedSlot?> GetFeedSlotByIdAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
            Task.FromResult(_feedUnits.SelectMany(u => u.FeedSlots).FirstOrDefault(s => s.Id == feedSlotId));

        public Task<FeedUnit?> GetByFeedSlotIdAsync(Guid feedSlotId, CancellationToken cancellationToken) =>
            Task.FromResult(_feedUnits.FirstOrDefault(u => u.FeedSlots.Any(s => s.Id == feedSlotId)));

        // The domain-level FeedUnit.AddFeedSlot call already appended the slot in-memory — this fake
        // has no real change-tracking to inform.
        public void TrackNewFeedSlot(FeedSlot feedSlot)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
