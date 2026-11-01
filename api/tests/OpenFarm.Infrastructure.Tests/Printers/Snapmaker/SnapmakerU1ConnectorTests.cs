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
        identify.Params!["api_key"]!.GetValue<string>().Should().Be("moonraker-key");
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
    public async Task Connect_TelemetryKeepsFlowingWithoutChanges()
    {
        await using var harness = new MoonrakerHarness();
        await using var stream = Stream(harness, harness.Details());
        await stream.NextMatchingAsync(e => e.GcodeState == "IDLE");

        var keepalive = await stream.NextMatchingAsync(e => e.Telemetry is not null);

        keepalive.GcodeState.Should().BeNull();
        keepalive.HmsAlerts.Should().BeEmpty();
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
        alerted.HmsAlerts!.Single().Code.Should().Be("2:1:523");
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
        upload.Should().MatchRegex("""name=root\s+gcodes""").And.MatchRegex("""name=path\s+openfarm""");
        upload.Should().Contain(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(gcode))));
        var fileName = Regex.Match(upload, """filename=(?<name>Bracket_PLA_2h_\d{8}-\d{9}\.gcode)""").Groups["name"].Value;
        fileName.Should().NotBeEmpty();

        var start = await harness.NextRequestAsync("server.files.start_local_print");
        start.Params!["path"]!.GetValue<string>().Should().Be($"openfarm/{fileName}");
        // Tool 0 prints on head 2 (T3), tool 2 on head 0; tool 1 is unused.
        start.Params["options"]!["map_table"]!.ToJsonString().Should().Be("[[0,2],[2,0]]");
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
        var start = await harness.NextRequestAsync("server.files.start_local_print");
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

        (await act.Should().ThrowAsync<IOException>()).Which.Message.Should().Contain("Printer is busy");
    }

    [Fact]
    public async Task StartJob_FirmwareWithoutStartLocalPrint_Throws()
    {
        await using var harness = new MoonrakerHarness { StartResult = null };

        var act = () => harness.CreateConnector().StartJobAsync(harness.Details(), new MemoryStream("G28\n"u8.ToArray()), "Part", 1, null, CancellationToken.None);

        await act.Should().ThrowAsync<MoonrakerRpcException>();
    }

    [Fact]
    public async Task Controls_PostThePrintCommands()
    {
        await using var harness = new MoonrakerHarness();
        var connector = harness.CreateConnector();

        await connector.PauseJobAsync(harness.Details(), CancellationToken.None);
        await connector.ResumeJobAsync(harness.Details(), CancellationToken.None);
        await connector.StopJobAsync(harness.Details(), CancellationToken.None);

        harness.Commands.Should().Equal("/printer/print/pause", "/printer/print/resume", "/printer/print/cancel");
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
