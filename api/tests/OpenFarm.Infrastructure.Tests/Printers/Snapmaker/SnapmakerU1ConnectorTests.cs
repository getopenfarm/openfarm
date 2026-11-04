using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Infrastructure.Printers;
using OpenFarm.Infrastructure.Printers.Snapmaker;

namespace OpenFarm.Infrastructure.Tests.Printers.Snapmaker;

public class SnapmakerU1ConnectorTests
{
    [Fact]
    public async Task LiveSkipping_SendsNamedExclusionsThroughMoonraker()
    {
        await using var harness = new MoonrakerHarness();
        await harness.CreateConnector().SkipObjectsAsync(harness.Details(), ["PART_A", "PART_B"], CancellationToken.None);
        var request = await harness.NextRequestAsync("printer.gcode.script");
        request.Params!["script"]!.GetValue<string>().Should().Be("EXCLUDE_OBJECT NAME=PART_A\nEXCLUDE_OBJECT NAME=PART_B");
    }

    [Fact]
    public async Task Verify_AU1_WithOrWithoutItsSerial_Succeeds()
    {
        await using var harness = new MoonrakerHarness();
        var connector = harness.CreateConnector();

        (await connector.VerifyAsync(harness.Details(serialNumber: ""), CancellationToken.None)).Should().BeTrue();
        (await connector.VerifyAsync(harness.Details(), CancellationToken.None)).Should().BeTrue();
        (await connector.ReadSerialNumberAsync(harness.Details(serialNumber: ""), CancellationToken.None)).Should().Be(harness.SerialNumber);
    }

    [Fact]
    public async Task Verify_AnotherU1OrAnotherMachine_Fails()
    {
        await using var harness = new MoonrakerHarness();
        var connector = harness.CreateConnector();

        (await connector.VerifyAsync(harness.Details(serialNumber: "SOMEONE-ELSE"), CancellationToken.None)).Should().BeFalse();
        harness.MachineType = "Klipper";
        (await connector.VerifyAsync(harness.Details(serialNumber: ""), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Verify_NothingListening_Fails()
    {
        MoonrakerTransportOptions options;
        await using (var closed = new MoonrakerHarness())
            options = closed.Options;
        var connector = new SnapmakerU1Connector(NullLogger<SnapmakerU1Connector>.Instance, options);

        (await connector.VerifyAsync(new PrinterConnectionDetails("Snapmaker", "U1", "127.0.0.1", "", ""), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Verify_ApiKeyRequired_FailsWithoutItAndSucceedsWithIt()
    {
        await using var harness = new MoonrakerHarness { RequiredApiKey = "moonraker-key" };
        var connector = harness.CreateConnector();

        (await connector.VerifyAsync(harness.Details(), CancellationToken.None)).Should().BeFalse();
        (await connector.VerifyAsync(harness.Details(apiKey: "moonraker-key"), CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task Connect_IdentifiesWithTheApiKeyAndSubscribes()
    {
        await using var harness = new MoonrakerHarness { RequiredApiKey = "moonraker-key" };
        await using var stream = Stream(harness, harness.Details(apiKey: "moonraker-key"));

        (await stream.NextAsync()).ConnectionStarted.Should().BeTrue();
        var snapshot = await stream.NextAsync();
        snapshot.GcodeState.Should().Be("IDLE");
        snapshot.Telemetry!.FeedReadings.Single().Slots.Should().HaveCount(4);
        var identify = await harness.NextRequestAsync("server.connection.identify");
        identify.Params!["api_key"].Should().BeNull();
    }

    [Fact]
    public async Task Connect_StateChangesGoOutInOrderAndAReconnectNeverResendsAState()
    {
        await using var harness = new MoonrakerHarness();
        await using var stream = Stream(harness, harness.Details());
        (await stream.NextAsync()).ConnectionStarted.Should().BeTrue();
        (await stream.NextAsync()).GcodeState.Should().Be("IDLE");

        await harness.PushStatusAsync("""{"print_stats":{"state":"printing"},"display_status":{"progress":1.0}}""");
        await harness.PushStatusAsync("""{"print_stats":{"state":"complete"}}""");
        await harness.PushStatusAsync("""{"print_stats":{"state":"standby"}}""");
        (await stream.NextStateAsync()).Should().Be("RUNNING");
        (await stream.NextStateAsync()).Should().Be("FINISH");
        (await stream.NextStateAsync()).Should().Be("IDLE");

        harness.Status["print_stats"]!["state"] = "standby";
        harness.DropConnections();
        (await stream.NextMatchingAsync(e => e.ConnectionEnded)).Should().NotBeNull();
        (await stream.NextMatchingAsync(e => e.ConnectionStarted)).Should().NotBeNull();
        var afterReconnect = await stream.NextMatchingAsync(e => e.Telemetry is not null);
        afterReconnect.GcodeState.Should().BeNull("the printer already reported IDLE on this connection");
        harness.SubscribeCount.Should().Be(2);
    }

    [Fact]
    public async Task Connect_CachedTelemetryPreservesObservationTime()
    {
        await using var harness = new MoonrakerHarness { PrintTask = new System.Text.Json.Nodes.JsonObject() };
        await using var stream = Stream(harness, harness.Details());
        var initial = await stream.NextMatchingAsync(e => e.GcodeState == "IDLE");

        var keepalive = await stream.NextMatchingAsync(e => e.Telemetry is not null);

        keepalive.ReceivedAtUtc.Should().Be(initial.ReceivedAtUtc);
        keepalive.Telemetry!.NozzleDiametersMm.Should().BeNull();
        keepalive.GcodeState.Should().BeNull();
        keepalive.HmsAlerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Connect_AQuietPrinterThatStillAnswers_StaysReachable()
    {
        await using var harness = new MoonrakerHarness();
        var options = harness.Options with { AliveInterval = TimeSpan.FromMilliseconds(100), CameraWakeInterval = TimeSpan.FromMilliseconds(50) };
        await using var stream = new EventStream(harness.CreateConnector(options), harness.Details());
        var initial = await stream.NextMatchingAsync(e => e.GcodeState == "IDLE");

        // No status pushes: only the camera wake answers arrive.
        var alive = await stream.NextMatchingAsync(e => e is { Telemetry: null, GcodeState: null, HmsAlerts: null, ConnectionStarted: false, ConnectionEnded: false });

        alive.ReceivedAtUtc.Should().BeAfter(initial.ReceivedAtUtc);
    }

    [Fact]
    public async Task Connect_ExceptionsBecomeAlertsAtOnce()
    {
        await using var harness = new MoonrakerHarness();
        await using var stream = Stream(harness, harness.Details());
        await stream.NextMatchingAsync(e => e.GcodeState == "IDLE");

        await harness.NotifyAsync("notify_exception_status",
            """[{"exceptions":[{"id":2,"index":1,"code":523,"level":3,"message":"Filament runout","timestamp":1.0}]}]""");

        var alerted = await stream.NextMatchingAsync(e => e.HmsAlerts is { Count: > 0 });
        alerted.HmsAlerts!.Single().Code.Should().Be("U1-0002-0001-0523");
    }

    [Fact]
    public async Task Connect_AnotherPrinterAtTheAddress_NeverSubscribes()
    {
        await using var harness = new MoonrakerHarness();
        var details = harness.Details();
        harness.SerialNumber = "DHCP-NEIGHBOUR";
        await using var stream = Stream(harness, details);

        await Task.Delay(300);

        harness.SubscribeCount.Should().Be(0);
        stream.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchPrintFile_ReadsFromTheGcodesRoot()
    {
        await using var harness = new MoonrakerHarness();
        harness.Files["openfarm/Bracket PLA.gcode"] = Encoding.UTF8.GetBytes("; generated by OrcaSlicer 2.3.1\nG28\n");
        var connector = harness.CreateConnector();

        var file = await connector.FetchPrintFileAsync(harness.Details(), null, "openfarm/Bracket PLA.gcode", CancellationToken.None);

        Encoding.UTF8.GetString(file!).Should().Contain("G28");
        (await connector.FetchPrintFileAsync(harness.Details(), null, "missing.gcode", CancellationToken.None)).Should().BeNull();
        (await connector.FetchPrintFileAsync(harness.Details(), null, "../moonraker.conf", CancellationToken.None)).Should().BeNull();
        (await connector.FetchPrintFileAsync(harness.Details(), null, null, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task StartJob_UnwrapsTheProjectUploadsItsGcodeAndStartsWithTheToolMap()
    {
        await using var harness = new MoonrakerHarness();
        const string gcode = "; generated by OrcaSlicer 2.3.1\nT0\nG1 X10 E1\n";
        var mapping = new FilamentFeedMapping([Head(2), null, Head(0)]);

        await harness.CreateConnector().StartJobAsync(harness.Details(), Project(gcode), "Bracket_PLA_2h", 1, mapping, CancellationToken.None);

        var upload = harness.Uploads.Should().ContainSingle().Subject;
        upload.Should().Contain(gcode).And.NotContain("Metadata/plate_1.gcode");
        upload.Should().Contain(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(gcode))));
        var routing = await harness.NextRequestAsync("printer.gcode.script");
        routing.Params!["script"]!.ToString().Should().Be("SET_PRINT_EXTRUDER_MAP CONFIG_EXTRUDER=0 MAP_EXTRUDER=2\nSET_PRINT_EXTRUDER_MAP CONFIG_EXTRUDER=2 MAP_EXTRUDER=0\nSET_PRINT_USED_EXTRUDERS EXTRUDERS=2,0");
        var start = await harness.NextRequestAsync("printer.print.start");
        start.Params!["filename"]!.ToString().Should().StartWith("openfarm-Bracket_PLA_2h-").And.EndWith(".gcode");
    }

    [Fact]
    public async Task StartJob_ExportedSinglePlate_UploadsThatPlatesGcode()
    {
        await using var harness = new MoonrakerHarness();
        const string gcode = "; plate 4\nG28\n";

        await harness.CreateConnector().StartJobAsync(harness.Details(), Project(gcode, plate: 4), "Part", 4, null, CancellationToken.None);

        harness.Uploads.Should().ContainSingle().Which.Should().Contain(gcode);
    }

    [Fact]
    public async Task StartJob_PlainGcodeWithoutAMapping_UploadsItAsItIsAndKeepsTheFirmwareMap()
    {
        await using var harness = new MoonrakerHarness();

        await harness.CreateConnector().StartJobAsync(harness.Details(), new MemoryStream("G28\n"u8.ToArray()), "Part", 1, null, CancellationToken.None);

        harness.Uploads.Should().ContainSingle().Which.Should().Contain("G28");
        var start = await harness.NextRequestAsync("printer.print.start");
        start.Params!["options"].Should().BeNull();
    }

    [Fact]
    public async Task StartJob_PrinterRefuses_Throws()
    {
        await using var harness = new MoonrakerHarness
        {
            StartResult = new() { ["state"] = "error", ["message"] = "Printer is busy, cannot start print" },
        };

        var act = () => harness.CreateConnector().StartJobAsync(harness.Details(), new MemoryStream("G28\n"u8.ToArray()), "Part", 1, null, CancellationToken.None);

        (await act.Should().ThrowAsync<MoonrakerRpcException>()).Which.Message.Should().Contain("Printer is busy");
    }

    [Fact]
    public async Task StartJob_FirmwareWithoutStartLocalPrint_Throws()
    {
        await using var harness = new MoonrakerHarness { StartResult = null };

        var act = () => harness.CreateConnector().StartJobAsync(harness.Details(), new MemoryStream("G28\n"u8.ToArray()), "Part", 1, null, CancellationToken.None);

        await act.Should().ThrowAsync<MoonrakerRpcException>();
    }

    [Fact]
    public async Task StartJob_LostReply_IsUnconfirmed()
    {
        await using var harness = new MoonrakerHarness { WithholdStartReply = true };
        var connector = harness.CreateConnector(harness.Options with { RequestTimeout = TimeSpan.FromMilliseconds(150) });
        var act = () => connector.StartJobAsync(harness.Details(), new MemoryStream("G28\n"u8.ToArray()), "Part", 1, null, CancellationToken.None);
        await act.Should().ThrowAsync<PrinterStartUnconfirmedException>();
        harness.Uploads.Should().ContainSingle();
    }

    [Fact]
    public async Task Controls_PostThePrintCommands()
    {
        await using var harness = new MoonrakerHarness();
        var connector = harness.CreateConnector();

        await connector.PauseJobAsync(harness.Details(), CancellationToken.None);
        await connector.ResumeJobAsync(harness.Details(), CancellationToken.None);
        await connector.StopJobAsync(harness.Details(), CancellationToken.None);

        harness.Commands.Should().Equal("printer.print.pause", "printer.print.resume", "printer.print.cancel");
    }

    [Fact]
    public async Task RepeatedArtifact_SkipsUploadAndUsesTheSameFilename()
    {
        await using var harness = new MoonrakerHarness();
        var connector = harness.CreateConnector();
        for (var i = 0; i < 2; i++)
            await connector.StartJobAsync(harness.Details(), new MemoryStream("G28\n"u8.ToArray()), "SamePart", 1, null, CancellationToken.None);
        harness.Uploads.Should().ContainSingle();
    }

    [Fact]
    public async Task CommandsShareMonitoringSocket_AndStateCanAcknowledgePause()
    {
        await using var harness = new MoonrakerHarness();
        harness.WithheldReplies.Add("printer.print.pause");
        await using var stream = Stream(harness, harness.Details());
        await stream.NextMatchingAsync(e => e.GcodeState == "IDLE");
        var command = harness.CreateConnector().PauseJobAsync(harness.Details(), CancellationToken.None);
        await harness.NextRequestAsync("printer.print.pause");
        harness.ActiveSockets.Should().Be(1);
        command.IsCompleted.Should().BeFalse();
        await harness.PushStatusAsync("""{"print_stats":{"state":"paused"}}""");
        await command.WaitAsync(TimeSpan.FromSeconds(2));
        (await stream.NextMatchingAsync(e => e.GcodeState == "PAUSE")).Telemetry.Should().NotBeNull();
    }

    [Fact]
    public async Task ApiKeyWithReservedCharacters_AuthenticatesInQuery()
    {
        const string key = "key +/#?&=";
        await using var harness = new MoonrakerHarness { RequiredApiKey = key };
        await using var stream = Stream(harness, harness.Details(apiKey: key));
        (await stream.NextMatchingAsync(e => e.ConnectionStarted)).ConnectionStarted.Should().BeTrue();
    }

    [Fact]
    public async Task SubscriptionsFilterAbsentObjects_AndCameraWakesWithoutViewer()
    {
        await using var harness = new MoonrakerHarness();
        harness.Status.Remove("extruder3");
        await using var stream = Stream(harness, harness.Details());
        await stream.NextMatchingAsync(e => e.ConnectionStarted);
        var subscription = await harness.NextRequestAsync("printer.objects.subscribe");
        subscription.Params!["objects"]!.AsObject().ContainsKey("extruder3").Should().BeFalse();
        var wake = await harness.NextRequestAsync("camera.start_monitor");
        wake.Params!["interval"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task HistoryIgnoresOtherFiles_AndReportsCurrentCompletion()
    {
        await using var harness = new MoonrakerHarness();
        harness.Status["print_stats"]!["filename"] = "current.gcode";
        harness.Status["print_stats"]!["state"] = "printing";
        await using var stream = Stream(harness, harness.Details());
        await stream.NextMatchingAsync(e => e.GcodeState == "RUNNING");
        await harness.NotifyAsync("notify_history_changed", """[{"action":"finished","job":{"filename":"other.gcode","status":"error"}}]""");
        await harness.NotifyAsync("notify_history_changed", """[{"action":"finished","job":{"filename":"current.gcode","status":"completed"}}]""");
        (await stream.NextMatchingAsync(e => e.GcodeState is "FAILED" or "FINISH")).GcodeState.Should().Be("FINISH");
    }

    [Fact]
    public async Task StalledCameraWake_DoesNotBlockStatusOrCommands()
    {
        await using var harness = new MoonrakerHarness();
        harness.WithheldReplies.Add("camera.start_monitor");
        await using var stream = Stream(harness, harness.Details());
        await stream.NextMatchingAsync(e => e.GcodeState == "IDLE");
        await harness.NextRequestAsync("camera.start_monitor");
        await harness.PushStatusAsync("""{"print_stats":{"state":"printing"}}""");
        (await stream.NextMatchingAsync(e => e.GcodeState == "RUNNING")).GcodeState.Should().Be("RUNNING");
        await harness.CreateConnector().PauseJobAsync(harness.Details(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FilamentPoller_ConditionalResponsesAndFailuresRetainOrClearOverrides()
    {
        await using var harness = new MoonrakerHarness { PrintTask = System.Text.Json.Nodes.JsonNode.Parse("""{"filament_type":["PLA"]}""") };
        await using var poller = new U1FilamentPoller(harness.Details(), harness.Options, NullLogger.Instance, CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await poller.Updates.ReadAsync(timeout.Token);
        first!["filament_type"]![0]!.ToString().Should().Be("PLA");
        harness.PrintTaskStatus = 304;
        poller.Refresh();
        await WaitForConditionAsync(() => harness.OverrideConditions.Count >= 2);
        poller.Updates.TryRead(out _).Should().BeFalse();
        harness.OverrideConditions.Last().Should().NotBeNull();
        harness.PrintTaskStatus = 500;
        poller.Refresh();
        await WaitForConditionAsync(() => harness.OverrideConditions.Count >= 3);
        poller.Updates.TryRead(out _).Should().BeFalse();
        harness.PrintTaskStatus = 404;
        poller.Refresh();
        (await poller.Updates.ReadAsync(timeout.Token)).Should().BeNull();
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task CommandDuringReconnect_WaitsForMonitoringInsteadOfOpeningAnotherSocket()
    {
        await using var harness = new MoonrakerHarness();
        var connector = harness.CreateConnector(harness.Options with { ReconnectDelay = TimeSpan.FromMilliseconds(300) });
        await using var stream = new EventStream(connector, harness.Details());
        await stream.NextMatchingAsync(e => e.GcodeState == "IDLE");
        harness.DropConnections();
        await stream.NextMatchingAsync(e => e.ConnectionEnded);
        await connector.PauseJobAsync(harness.Details(), CancellationToken.None);
        harness.SubscribeCount.Should().Be(2);
        harness.ActiveSockets.Should().Be(1);
    }

    [Fact]
    public async Task IndependentPrinterCommandsProceedWhileAnotherSessionInitializes()
    {
        await using var first = new MoonrakerHarness();
        first.WithheldReplies.Add("server.connection.identify");
        await using var stream = Stream(first, first.Details());
        await first.NextRequestAsync("server.connection.identify");
        await using var second = new MoonrakerHarness();
        await second.CreateConnector().PauseJobAsync(second.Details(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
        first.SubscribeCount.Should().Be(0);
        second.Commands.Should().ContainSingle().Which.Should().Be("printer.print.pause");
    }

    // The toolheads are one unit whose slot n is the spool on head n.
    private static FeedSlotRef Head(int head) => new(FeedUnitKindCatalog.MultiMaterialUnit, 0, head);

    [Fact]
    public void MapTable_ASlotThatIsNotOneOfTheFourHeads_IsRefused()
    {
        FluentActions.Invoking(() => SnapmakerU1Connector.MapTable(new FilamentFeedMapping([Head(4)])))
            .Should().Throw<InvalidOperationException>();
        var externalPort = new FeedSlotRef(FeedUnitKindCatalog.ExternalSpool, 0, 0);
        FluentActions.Invoking(() => SnapmakerU1Connector.MapTable(new FilamentFeedMapping([externalPort])))
            .Should().Throw<InvalidOperationException>();
        SnapmakerU1Connector.MapTable(null).Should().BeEmpty();
    }

    private static MemoryStream Project(string plateGcode, int plate = 1)
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry($"Metadata/plate_{plate}.gcode").Open());
            writer.Write(plateGcode);
        }

        zip.Position = 0;
        return zip;
    }

    private static EventStream Stream(MoonrakerHarness harness, PrinterConnectionDetails details) => new(harness.CreateConnector(), details);

    /// <summary>Reads a connector's event stream on a background task, so a test can await specific events with a deadline.</summary>
    [Fact]
    public async Task UploadContent_ReportsTheShareWrittenUpToAll()
    {
        var reports = new List<PrintSendProgress>();
        using var content = new SnapmakerU1Connector.UploadContent(new byte[150 * 1024], new InlineProgress<PrintSendProgress>(reports.Add));
        using var sink = new MemoryStream();

        await content.CopyToAsync(sink);

        sink.Length.Should().Be(150 * 1024);
        reports.Select(r => r.Percent).Should().Equal(42, 85, 100);
        reports.Should().OnlyContain(r => r.Stage == PrintSendStage.Uploading);
    }

    private sealed class EventStream : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Channel<PrinterConnectorEvent> _events = Channel.CreateUnbounded<PrinterConnectorEvent>();
        private readonly Task _pump;

        public EventStream(IPrinterConnector connector, PrinterConnectionDetails details)
        {
            _pump = Task.Run(async () =>
            {
                try
                {
                    await foreach (var connectorEvent in connector.ConnectAsync(details, _cts.Token))
                    {
                        Received.Add(connectorEvent);
                        _events.Writer.TryWrite(connectorEvent);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        public List<PrinterConnectorEvent> Received { get; } = [];

        public Task<PrinterConnectorEvent> NextAsync() => _events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

        public async Task<PrinterConnectorEvent> NextMatchingAsync(Func<PrinterConnectorEvent, bool> match)
        {
            while (true)
            {
                var next = await NextAsync();
                if (match(next)) return next;
            }
        }

        public async Task<string> NextStateAsync() => (await NextMatchingAsync(e => e.GcodeState is not null)).GcodeState!;

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            await _pump.WaitAsync(TimeSpan.FromSeconds(3));
            _cts.Dispose();
        }
    }
}
