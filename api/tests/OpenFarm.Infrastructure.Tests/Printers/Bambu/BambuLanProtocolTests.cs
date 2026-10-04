using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentFTP;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Adapter;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

public class BambuLanProtocolTests
{
    [Fact]
    public async Task Verify_AuthenticatesSubscribesRequestsBothReportsAndDisconnects()
    {
        await using var harness = new BambuMqttHarness();
        var connector = harness.CreateConnector();

        (await connector.VerifyAsync(harness.Details, CancellationToken.None)).Should().BeTrue();

        harness.Handshakes.Should().ContainSingle().Which.Should().Be(("MQTT", (byte)4, "bblp"));
        harness.Subscriptions.Should().ContainSingle().Which.Should().Be("device/TEST-P1S/report");
        var version = await harness.NextRequestAsync();
        var pushAll = await harness.NextRequestAsync();
        version.Topic.Should().Be("device/TEST-P1S/request");
        version.Payload.Should().Be("""{"info":{"sequence_id":"0","command":"get_version"}}""");
        pushAll.Payload.Should().Be("""{"pushing":{"sequence_id":"0","command":"pushall"}}""");
        await WaitUntilAsync(() => harness.ActiveConnections == 0);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Verify_MissingEitherReport_TimesOutAndDisconnects(bool version, bool print)
    {
        await using var harness = new BambuMqttHarness { ReplyToVersion = version, ReplyToPushAll = print };
        var connector = harness.CreateConnector(harness.Options with { VerifyTimeout = TimeSpan.FromMilliseconds(400) });

        (await connector.VerifyAsync(harness.Details, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)))
            .Should().BeFalse();
        await WaitUntilAsync(() => harness.ActiveConnections == 0);
    }

    [Fact]
    public async Task Verify_BadCredentials_ReturnsFalseWithoutSubscribing()
    {
        await using var harness = new BambuMqttHarness();

        (await harness.CreateConnector().VerifyAsync(harness.Details with { AccessCode = "wrong" }, CancellationToken.None))
            .Should().BeFalse();
        harness.Subscriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Verify_ProductionTrust_RejectsUnrelatedCertificate()
    {
        await using var harness = new BambuMqttHarness();
        var connector = harness.CreateConnector(harness.Options with { ValidateCertificate = BambuCertificateTrust.Validate });

        (await connector.VerifyAsync(harness.Details, CancellationToken.None)).Should().BeFalse();
        harness.Handshakes.Should().BeEmpty();
    }

    [Fact]
    public async Task Verify_CallerCancellation_CleansUpWhileWaitingForReports()
    {
        await using var harness = new BambuMqttHarness { ReplyToVersion = false, ReplyToPushAll = false };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var verification = harness.CreateConnector().VerifyAsync(harness.Details, cts.Token);
        await harness.NextRequestAsync();
        await harness.NextRequestAsync();

        await cts.CancelAsync();
        // Preserve VerifyAsync's existing bool result contract on cancellation.
        (await verification.WaitAsync(TimeSpan.FromSeconds(3))).Should().BeFalse();
        await WaitUntilAsync(() => harness.ActiveConnections == 0);
    }

    [Fact]
    public async Task Connect_ReplaysFixtureIgnoresMalformedMessagesAndResubscribesAfterDrop()
    {
        var fixtureFile = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Printers", "Fixtures", "printing-a1.json"));
        using var fixtureDocument = JsonDocument.Parse(fixtureFile);
        var fixture = fixtureDocument.RootElement.GetProperty("pushall").GetRawText();
        await using var harness = new BambuMqttHarness { InitialReport = fixture };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connector = harness.CreateConnector();
        await using var events = connector.ConnectAsync(harness.Details, cts.Token).GetAsyncEnumerator();

        (await ReadNextAsync(events, cts)).Should().BeTrue();
        events.Current.GcodeState.Should().Be("RUNNING");
        events.Current.Telemetry.Should().BeEquivalentTo(BambuMqttMessageParser.TryParseTelemetry(fixture));

        await harness.PublishReportAsync("{broken");
        await harness.PublishReportAsync("[]");
        await harness.PublishReportAsync("""{"print":null}""");
        await harness.PublishReportAsync("""{"info":{"command":"get_version"}}""");
        await harness.PublishReportAsync("""{"print":{"mc_percent":42}}""");
        (await ReadNextAsync(events, cts)).Should().BeTrue();
        events.Current.GcodeState.Should().BeNull();
        events.Current.Telemetry!.PrintProgressPercent.Should().Be(42);
        events.Current.Telemetry.NozzleTempC.Should().BeNull("a partial report must preserve absence for the merger");

        harness.DropConnections();
        (await ReadNextAsync(events, cts)).Should().BeTrue();
        events.Current.GcodeState.Should().Be("RUNNING");
        harness.SubscriptionCount.Should().Be(2);
        harness.Subscriptions.Should().OnlyContain(topic => topic == "device/TEST-P1S/report");

        await events.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => harness.ActiveConnections == 0);
    }

    [Fact]
    public async Task Commands_UseSeparateConnectionsWhileTelemetryRemainsConnected()
    {
        await using var harness = new BambuMqttHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connector = harness.CreateConnector();
        await using var events = connector.ConnectAsync(harness.Details, cts.Token).GetAsyncEnumerator();
        await ReadNextAsync(events, cts);
        var version = await harness.NextRequestAsync();
        await harness.NextRequestAsync();

        await connector.PauseJobAsync(harness.Details, cts.Token);
        await connector.ResumeJobAsync(harness.Details, cts.Token);
        await connector.StopJobAsync(harness.Details, cts.Token);

        foreach (var command in new[] { "pause", "resume", "stop" })
        {
            var request = await harness.NextRequestAsync();
            request.ConnectionId.Should().NotBe(version.ConnectionId);
            request.Topic.Should().Be("device/TEST-P1S/request");
            using var document = JsonDocument.Parse(request.Payload);
            document.RootElement.GetProperty("print").GetProperty("command").GetString().Should().Be(command);
        }

        await WaitUntilAsync(() => harness.ActiveConnections == 1);
        // Command publication must not manufacture a status event.
        var nextEvent = events.MoveNextAsync().AsTask();
        await harness.PublishReportAsync("""{"print":{"gcode_state":"PAUSE"}}""");
        (await nextEvent.WaitAsync(TimeSpan.FromSeconds(3))).Should().BeTrue();
        events.Current.GcodeState.Should().Be("PAUSE");
    }

    [Fact]
    public async Task StartJob_UploadsExactBytesBeforePublishingEscapedNameAndChecksum()
    {
        await using var harness = new BambuMqttHarness();
        byte[] fileBytes = Encoding.UTF8.GetBytes("a test 3mf payload");
        byte[]? uploadedBytes = null;
        string? uploadedPath = null;
        var uploadCompleted = false;
        var connector = harness.CreateConnector(harness.Options with
        {
            UploadFileAsync = async (details, content, path, token) =>
            {
                details.Should().Be(harness.Details);
                harness.ConnectionCount.Should().Be(0, "upload must precede MQTT command connection");
                using var copy = new MemoryStream();
                await content.CopyToAsync(copy, token);
                uploadedBytes = copy.ToArray();
                uploadedPath = path;
                uploadCompleted = true;
                return FtpStatus.Success;
            },
        });
        using var file = new MemoryStream(fileBytes);
        const string jobName = "part \"quoted\" \\ folder\n雪.3mf";

        await connector.StartJobAsync(harness.Details, file, jobName, CancellationToken.None);

        uploadCompleted.Should().BeTrue();
        uploadedBytes.Should().Equal(fileBytes);
        using var document = JsonDocument.Parse((await harness.NextRequestAsync()).Payload);
        var print = document.RootElement.GetProperty("print");
        print.GetProperty("subtask_name").GetString().Should().Be(jobName);
        print.GetProperty("command").GetString().Should().Be("project_file");
        print.GetProperty("param").GetString().Should().Be("Metadata/plate_1.gcode");
        print.GetProperty("url").GetString().Should().Be($"ftp:///{Path.GetFileName(uploadedPath)}");
        uploadedPath.Should().StartWith("/cache/").And.EndWith(".3mf");
        print.GetProperty("md5").GetString().Should().Be("d08a01130a952f060cd359e51e0540d9");
        print.GetProperty("use_ams").GetBoolean().Should().BeFalse();
        await WaitUntilAsync(() => harness.ActiveConnections == 0);
    }

    [Theory]
    [InlineData(FtpStatus.Failed)]
    [InlineData(FtpStatus.Skipped)]
    public async Task StartJob_UnsuccessfulUpload_DoesNotCreateMqttClient(FtpStatus status)
    {
        var connector = new BambuLanConnector(NullLogger<BambuLanConnector>.Instance, new BambuTransportOptions
        {
            CreateMqttClient = () => throw new InvalidOperationException("Must not publish after failed upload"),
            UploadFileAsync = (_, _, _, _) => Task.FromResult(status),
        });
        using var file = new MemoryStream([1, 2, 3]);

        var start = () => connector.StartJobAsync(TestDetails, file, "part", CancellationToken.None);

        await start.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task StartJob_UploadThrows_DoesNotPublish()
    {
        var connector = new BambuLanConnector(NullLogger<BambuLanConnector>.Instance, new BambuTransportOptions
        {
            CreateMqttClient = () => throw new InvalidOperationException("Must not publish"),
            UploadFileAsync = (_, _, _, _) => throw new IOException("Upload connection failed"),
        });
        using var file = new MemoryStream([1, 2, 3]);

        var start = () => connector.StartJobAsync(TestDetails, file, "part", CancellationToken.None);
        await start.Should().ThrowAsync<IOException>().WithMessage("Upload connection failed");
    }

    [Fact]
    public async Task StartJob_CancellationDuringUpload_DoesNotPublish()
    {
        var uploading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connector = new BambuLanConnector(NullLogger<BambuLanConnector>.Instance, new BambuTransportOptions
        {
            CreateMqttClient = () => throw new InvalidOperationException("Must not publish"),
            UploadFileAsync = async (_, _, _, token) =>
            {
                uploading.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return FtpStatus.Success;
            },
        });
        using var file = new MemoryStream([1, 2, 3]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var start = connector.StartJobAsync(TestDetails, file, "part", cts.Token);
        await uploading.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await cts.CancelAsync();

        var pendingStart = async () => await start.WaitAsync(TimeSpan.FromSeconds(3));
        await pendingStart.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Command_MissingConnack_TimesOutAndDisconnects()
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LoopbackTlsServer(async (stream, token) =>
        {
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, token); // Deliberately never send CONNACK.
            bytesRead.Should().BeGreaterThan(0, "the client must send CONNECT");
            connected.TrySetResult();
            try
            {
                while (await stream.ReadAsync(buffer, token) != 0) { }
            }
            catch (IOException) { /* TLS may signal the peer closing through a reset on macOS. */ }
            finally { closed.TrySetResult(); }
        });
        var connector = new BambuLanConnector(NullLogger<BambuLanConnector>.Instance, new BambuTransportOptions
        {
            MqttPort = server.Port,
            ValidateCertificate = server.TrustCertificate,
            CommandTimeout = TimeSpan.FromMilliseconds(500),
        });
        var command = connector.PauseJobAsync(TestDetails, CancellationToken.None);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var pendingCommand = async () => await command.WaitAsync(TimeSpan.FromSeconds(3));
        // MQTTnet wraps cancellation while awaiting CONNACK as an authentication failure.
        await pendingCommand.Should().ThrowAsync<MqttConnectingFailedException>();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Command_AuthenticationFailure_PropagatesWithoutPublishing()
    {
        await using var harness = new BambuMqttHarness();

        var pause = () => harness.CreateConnector().PauseJobAsync(harness.Details with { AccessCode = "wrong" }, CancellationToken.None);
        await pause.Should().ThrowAsync<Exception>();
        harness.Subscriptions.Should().BeEmpty();
        harness.ActiveConnections.Should().Be(0);
    }

    [Fact]
    public async Task Connect_Cancellation_DisconnectsAndDisposesClient()
    {
        await using var harness = new BambuMqttHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clients = new ConcurrentBag<IMqttClient>();
        var connector = harness.CreateConnector(harness.Options with
        {
            CreateMqttClient = () =>
            {
                var client = new MqttClientFactory().CreateMqttClient();
                clients.Add(client);
                return client;
            },
        });
        await using var events = connector.ConnectAsync(harness.Details, cts.Token).GetAsyncEnumerator();
        await ReadNextAsync(events, cts);
        await cts.CancelAsync();

        var next = async () => await events.MoveNextAsync();
        await next.Should().ThrowAsync<OperationCanceledException>();
        await WaitUntilAsync(() => harness.ActiveConnections == 0);
        clients.Should().ContainSingle().Which.IsConnected.Should().BeFalse();
    }

    private static readonly PrinterConnectionDetails TestDetails = new("Bambu", "P1S", "127.0.0.1", "TEST-P1S", "test-code");

    private static async Task<bool> ReadNextAsync(IAsyncEnumerator<PrinterConnectorEvent> events, CancellationTokenSource lifetime)
    {
        var read = events.MoveNextAsync().AsTask();
        try
        {
            return await read.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            await lifetime.CancelAsync();
            try
            {
                await read.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }
    }

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
