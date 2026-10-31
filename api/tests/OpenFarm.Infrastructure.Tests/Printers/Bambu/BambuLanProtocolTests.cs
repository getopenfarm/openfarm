using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Adapter;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

public class BambuLanProtocolTests
{
    [Fact]
    public async Task StartAndLiveExclusions_UseNativeObjectIds()
    {
        await using var mqtt = new BambuMqttHarness();
        var connector = mqtt.CreateConnector(mqtt.Options with { UploadFileAsync = (_, _, _, _) => Task.FromResult(FtpStatus.Success) });
        using var file = new MemoryStream("G28\n"u8.ToArray());
        await connector.StartSelectedJobAsync(mqtt.Details, file, "selected.gcode", 1, null, ["206"], CancellationToken.None);
        using var start = JsonDocument.Parse((await mqtt.NextRequestAsync()).Payload);
        start.RootElement.GetProperty("print").GetProperty("skip_objects").EnumerateArray().Select(i => i.GetInt32()).Should().Equal(206);
        await connector.SkipObjectsAsync(mqtt.Details, ["206", "300"], CancellationToken.None);
        using var skip = JsonDocument.Parse((await mqtt.NextRequestAsync()).Payload);
        skip.RootElement.GetProperty("print").GetProperty("command").GetString().Should().Be("skip_objects");
        skip.RootElement.GetProperty("print").GetProperty("obj_list").EnumerateArray().Select(i => i.GetInt32()).Should().Equal(206, 300);
    }

    [Theory]
    [InlineData("/cache/../escape.3mf")]
    [InlineData("/cache/file.3mf\r\nDELE other.3mf")]
    public async Task UploadNewFile_UnsafeDestination_StillRejected(string path)
    {
        await using var client = new AsyncFtpClient("127.0.0.1", "bblp", "test-code");
        using var content = new MemoryStream([1, 2, 3]);
        var upload = () => BambuLanConnector.UploadNewFileAsync(client, content, path, CancellationToken.None);

        await upload.Should().ThrowAsync<FtpSanitizeException>();
    }

    [Fact]
    public async Task StartJob_ExistingPrinterFilenameWithRepeatedDots_DoesNotBlockUpload()
    {
        await using var mqtt = new BambuMqttHarness();
        await using var ftp = new BambuFtpsHarness();
        var connector = mqtt.CreateConnector(mqtt.Options with
        {
            UploadFileAsync = async (details, content, path, token) =>
            {
                await using var client = new AsyncFtpClient(details.IpAddress, "bblp", details.AccessCode, ftp.Port);
                client.Config.EncryptionMode = FtpEncryptionMode.Implicit;
                client.Config.ValidateAnyCertificate = true;
                await client.Connect(token);

                // Prove the peer recreates the reported failure under the normal existence check.
                var check = () => client.FileExists(path, token);
                await check.Should().ThrowAsync<FtpSanitizeException>()
                    .WithMessage($"*{BambuFtpsHarness.ExistingFileName}*");
                ftp.Commands.Clear();
                return await BambuLanConnector.UploadNewFileAsync(client, content, path, token);
            },
        });
        byte[] bytes = Encoding.UTF8.GetBytes("print payload");
        using var file = new MemoryStream(bytes);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await connector.StartJobAsync(mqtt.Details, file, "new part.gcode", 1, null, cts.Token);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"FTPS commands: {string.Join(", ", ftp.Commands)}", ex);
        }

        using (var uploaded = new MemoryStream(ftp.UploadedBytes!))
        using (var archive = new ZipArchive(uploaded))
        using (var plate = archive.GetEntry(BambuPrintFile.PlatePath(1))!.Open())
        using (var copy = new MemoryStream())
        {
            await plate.CopyToAsync(copy);
            copy.ToArray().Should().Equal(bytes);
        }
        ftp.UploadedPath.Should().StartWith("/cache/").And.EndWith(".3mf");
        ftp.UploadedPath.Should().MatchRegex(@"^/cache/new_part_\d{8}-\d{9}\.gcode\.3mf$");
        ftp.Commands.Should().NotContain(command => command.StartsWith("NLST", StringComparison.Ordinal));
        ftp.Commands.Should().NotContain(command => command.StartsWith("DELE", StringComparison.Ordinal));
        using var document = JsonDocument.Parse((await mqtt.NextRequestAsync()).Payload);
        document.RootElement.GetProperty("print").GetProperty("url").GetString()
            .Should().Be($"file:///sdcard{ftp.UploadedPath}");
    }

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
    public async Task Connect_AttemptTimeout_RetriesWithoutEndingTheStream()
    {
        await using var harness = new BambuMqttHarness { WithholdFirstConnack = true };
        var connector = harness.CreateConnector(harness.Options with { ConnectTimeout = TimeSpan.FromMilliseconds(200) });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = connector.ConnectAsync(harness.Details, cts.Token).GetAsyncEnumerator();
        (await ReadNextAsync(events, cts)).Should().BeTrue();
        events.Current.GcodeState.Should().Be("IDLE");
        harness.ConnectionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Connect_OpenButSilentBroker_ReconnectsAndRequestsFreshReports()
    {
        await using var harness = new BambuMqttHarness();
        var connector = harness.CreateConnector(harness.Options with
        {
            TelemetryTimeout = TimeSpan.FromMilliseconds(200),
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = connector.ConnectAsync(harness.Details, cts.Token).GetAsyncEnumerator();
        (await ReadNextAsync(events, cts)).Should().BeTrue();
        await WaitUntilAsync(() => harness.SubscriptionCount >= 2);
        (await ReadNextAsync(events, cts)).Should().BeTrue();
        events.Current.GcodeState.Should().Be("IDLE");
        harness.ConnectionCount.Should().BeGreaterThanOrEqualTo(2);
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
        byte[] fileBytes = Encoding.UTF8.GetBytes("; test G-code\nG28\n");
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
        const string jobName = "part \"quoted\" \\ folder\n雪.gcode";

        await connector.StartJobAsync(harness.Details, file, jobName, 1, null, CancellationToken.None);

        uploadCompleted.Should().BeTrue();
        using (var archive = new ZipArchive(new MemoryStream(uploadedBytes!)))
        using (var plate = archive.GetEntry(BambuPrintFile.PlatePath(1))!.Open())
        using (var copy = new MemoryStream())
        {
            await plate.CopyToAsync(copy);
            copy.ToArray().Should().Equal(fileBytes);
        }
        using var document = JsonDocument.Parse((await harness.NextRequestAsync()).Payload);
        var print = document.RootElement.GetProperty("print");
        print.GetProperty("subtask_name").GetString().Should().Be(jobName);
        print.GetProperty("command").GetString().Should().Be("project_file");
        print.GetProperty("param").GetString().Should().Be("Metadata/plate_1.gcode");
        print.GetProperty("url").GetString().Should().Be($"file:///sdcard{uploadedPath}");
        uploadedPath.Should().StartWith("/cache/").And.EndWith(".3mf");
        uploadedPath.Should().MatchRegex(@"^/cache/part_quoted_folder_\d{8}-\d{9}\.gcode\.3mf$");
#pragma warning disable CA5351 // Verify the Bambu protocol integrity checksum against uploaded bytes.
        print.GetProperty("md5").GetString().Should().Be(Convert.ToHexStringLower(MD5.HashData(uploadedBytes!)));
#pragma warning restore CA5351
        print.GetProperty("use_ams").GetBoolean().Should().BeFalse();
        await WaitUntilAsync(() => harness.ActiveConnections == 0);
    }

    [Theory]
    [InlineData("P1P", "file:///sdcard")]
    [InlineData("P1S", "file:///sdcard")]
    [InlineData("A1", "file:///sdcard")]
    [InlineData("A1MINI", "file:///sdcard")]
    [InlineData("X1", "ftp://")]
    [InlineData("X1C", "ftp://")]
    [InlineData("X1E", "ftp://")]
    public async Task StartJob_ProjectUrlPointsToExactUploadPath(string model, string prefix)
    {
        await using var harness = new BambuMqttHarness();
        string? uploadPath = null;
        byte[]? uploadedBytes = null;
        var connector = harness.CreateConnector(harness.Options with
        {
            UploadFileAsync = async (_, content, path, token) =>
            {
                uploadPath = path;
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, token);
                uploadedBytes = buffer.ToArray();
                return FtpStatus.Success;
            },
        });
        var project = BambuPrintFile.PrepareProject(Encoding.UTF8.GetBytes("G28\n"), "part.gcode", 1);
        using var file = new MemoryStream(project);

        await connector.StartJobAsync(harness.Details with { ModelCode = model }, file, "part.gcode.3mf", 1, null, default);

        uploadedBytes.Should().Equal(project, "an existing project must retain settings, thumbnails and checksums");
        uploadPath.Should().StartWith("/cache/");
        using var request = JsonDocument.Parse((await harness.NextRequestAsync()).Payload);
        request.RootElement.GetProperty("print").GetProperty("url").GetString().Should().Be(prefix + uploadPath);
    }

    private static async Task<JsonElement> StartWithMappingAsync(FilamentFeedMapping? mapping, string model = "P1S")
    {
        await using var harness = new BambuMqttHarness();
        var connector = harness.CreateConnector(harness.Options with { UploadFileAsync = (_, _, _, _) => Task.FromResult(FtpStatus.Success) });
        using var file = new MemoryStream(BambuPrintFile.PrepareProject(Encoding.UTF8.GetBytes("G28\n"), "part.gcode", 1));

        await connector.StartJobAsync(harness.Details with { ModelCode = model }, file, "part.gcode.3mf", 1, mapping, default);

        using var request = JsonDocument.Parse((await harness.NextRequestAsync()).Payload);
        return request.RootElement.GetProperty("print").Clone();
    }

    private static FeedSlotRef AmsSlot(int unit, int slot) => new(FeedUnitKindCatalog.MultiMaterialUnit, unit, slot);

    private static readonly FeedSlotRef ExternalPort = new(FeedUnitKindCatalog.ExternalSpool, 0, 0);

    [Fact]
    public async Task StartJob_WithAmsMapping_UsesAmsAndSendsBothMappingArrays()
    {
        // Black on AMS 0 slot 2, white on AMS 0 slot 0; a third project filament is unused.
        var mapping = new FilamentFeedMapping([AmsSlot(0, 2), AmsSlot(0, 0), null]);

        var print = await StartWithMappingAsync(mapping);

        print.GetProperty("use_ams").GetBoolean().Should().BeTrue();
        print.GetProperty("ams_mapping").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(2, 0, -1);
        print.GetProperty("ams_mapping2").EnumerateArray().Select(e => (e.GetProperty("ams_id").GetInt32(), e.GetProperty("slot_id").GetInt32()))
            .Should().Equal((0, 2), (0, 0), (255, 255));
    }

    [Fact]
    public async Task StartJob_ExternalOnlyMapping_DoesNotUseAmsButIsSpelledOut()
    {
        var print = await StartWithMappingAsync(new FilamentFeedMapping([ExternalPort]));

        print.GetProperty("use_ams").GetBoolean().Should().BeFalse();
        print.GetProperty("ams_mapping").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(-1);
        print.GetProperty("ams_mapping2")[0].GetProperty("ams_id").GetInt32().Should().Be(255);
        print.GetProperty("ams_mapping2")[0].GetProperty("slot_id").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData(0, 3, 3)]
    [InlineData(1, 0, 4)]
    [InlineData(1, 3, 7)]
    [InlineData(3, 3, 15)]
    public void EncodeAmsMapping_FourSlotUnits_UseUnitTimesFourPlusSlot(int unit, int slot, int tray)
    {
        var encoded = BambuLanConnector.EncodeAmsMapping(new([AmsSlot(unit, slot)]));

        encoded.Trays.Should().Equal(tray);
        encoded.Targets.Should().Equal((unit, slot));
    }

    [Fact]
    public void EncodeAmsMapping_SingleSlotUnit_UsesItsUnitIdAndSlotZero()
    {
        var encoded = BambuLanConnector.EncodeAmsMapping(new([AmsSlot(128, 0)]));

        encoded.UseAms.Should().BeTrue();
        encoded.Trays.Should().Equal(128);
        encoded.Targets.Should().Equal((128, 0));
    }

    [Fact]
    public async Task StartJob_WithoutMapping_SendsNoMappingFields()
    {
        var print = await StartWithMappingAsync(null);

        print.GetProperty("use_ams").GetBoolean().Should().BeFalse();
        print.TryGetProperty("ams_mapping", out _).Should().BeFalse();
        print.TryGetProperty("ams_mapping2", out _).Should().BeFalse();
    }

    [Fact]
    public async Task StartJob_ExportedSinglePlate_UploadsItUnchangedAndPrintsThatPlate()
    {
        byte[]? uploadedBytes = null;
        await using var harness = new BambuMqttHarness();
        var connector = harness.CreateConnector(harness.Options with
        {
            UploadFileAsync = async (_, content, _, token) =>
            {
                using var copy = new MemoryStream();
                await content.CopyToAsync(copy, token);
                uploadedBytes = copy.ToArray();
                return FtpStatus.Success;
            },
        });
        using var project = new MemoryStream();
        using (var archive = new ZipArchive(project, ZipArchiveMode.Create, leaveOpen: true))
        using (var plate = new StreamWriter(archive.CreateEntry("Metadata/plate_4.gcode").Open())) plate.Write("G28\n");
        var projectBytes = project.ToArray();
        using var file = new MemoryStream(projectBytes);

        await connector.StartJobAsync(harness.Details, file, "part.gcode.3mf", 4, null, default);

        uploadedBytes.Should().Equal(projectBytes);
        using var document = JsonDocument.Parse((await harness.NextRequestAsync()).Payload);
        document.RootElement.GetProperty("print").GetProperty("param").GetString().Should().Be("Metadata/plate_4.gcode");
    }

    [Fact]
    public async Task StartJob_ProjectMissingRequestedPlate_DoesNotUploadOrPublish()
    {
        var uploadCalled = false;
        await using var harness = new BambuMqttHarness();
        var connector = harness.CreateConnector(harness.Options with
        {
            UploadFileAsync = (_, _, _, _) => { uploadCalled = true; return Task.FromResult(FtpStatus.Success); },
        });
        using var file = new MemoryStream();
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true)) archive.CreateEntry("3D/3dmodel.model");
        file.Position = 0;
        var start = () => connector.StartJobAsync(harness.Details, file, "unsliced.3mf", 1, null, default);

        await start.Should().ThrowAsync<IOException>().WithMessage("*Metadata/plate_1.gcode*");
        uploadCalled.Should().BeFalse();
        harness.ConnectionCount.Should().Be(0);
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

        var start = () => connector.StartJobAsync(TestDetails, file, "part", 1, null, CancellationToken.None);

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

        var start = () => connector.StartJobAsync(TestDetails, file, "part", 1, null, CancellationToken.None);
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
        var start = connector.StartJobAsync(TestDetails, file, "part", 1, null, cts.Token);
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

        var next = async () => { while (await events.MoveNextAsync()) { } };
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
            while (await read.WaitAsync(TimeSpan.FromSeconds(3)))
            {
                if (!events.Current.ConnectionStarted && !events.Current.ConnectionEnded) return true;
                read = events.MoveNextAsync().AsTask();
            }
            return false;
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
