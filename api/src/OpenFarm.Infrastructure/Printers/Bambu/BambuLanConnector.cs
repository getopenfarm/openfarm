using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Real Bambu LAN connector: MQTT 3.1.1 over TLS on port 8883, username <c>bblp</c> / password =
/// the printer's LAN access code, topics <c>device/{serial}/report</c> (subscribe) and
/// <c>device/{serial}/request</c> (publish), per research.md §1. First validated against real
/// hardware (a Bambu A1) via quickstart.md's manual physical-hardware validation section — that pass
/// found and fixed the TLS trust-store root/intermediate bug (<see cref="BambuCertificateTrust"/>)
/// and this class's missing MQTT protocol-version pin (research.md §1's two post-implementation
/// notes); see <c>Printers/Fixtures/README.md</c> for the fixture-replay corpus used elsewhere.
/// </summary>
public sealed partial class BambuLanConnector : IPrinterConnector
{
    private const string MqttUsername = "bblp";
    private const string FtpsCacheDirectory = "/cache";

    private static readonly JsonSerializerOptions IgnoreNullOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly ILogger<BambuLanConnector> _logger;
    private readonly BambuTransportOptions _options;

    public BambuLanConnector(ILogger<BambuLanConnector> logger)
        : this(logger, new BambuTransportOptions())
    {
    }

    internal BambuLanConnector(ILogger<BambuLanConnector> logger, BambuTransportOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        using var client = _options.CreateMqttClient();
        var receivedInfo = false;
        var receivedPrint = false;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.ApplicationMessageReceivedAsync += e =>
        {
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            if (BambuMqttMessageParser.IsGetVersionResponse(payload))
            {
                receivedInfo = true;
            }

            if (BambuMqttMessageParser.IsPrintReport(payload))
            {
                receivedPrint = true;
            }

            if (receivedInfo && receivedPrint)
            {
                completion.TrySetResult();
            }

            return Task.CompletedTask;
        };

        using var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        verifyCts.CancelAfter(_options.VerifyTimeout);

        try
        {
            await client.ConnectAsync(BuildClientOptions(details, _options), verifyCts.Token);
            await SubscribeAndRequestAsync(client, details.SerialNumber, verifyCts.Token);

            using (verifyCts.Token.Register(() => completion.TrySetCanceled()))
            {
                await completion.Task;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            LogVerifyTimedOut(_logger, details.IpAddress);
            return false;
        }
        catch (Exception ex)
        {
            LogVerifyFailed(_logger, details.IpAddress, ex);
            return false;
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            }
        }
    }

    public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(
        PrinterConnectionDetails details,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connectionToken = connectionCts.Token;
        var channel = Channel.CreateUnbounded<PrinterConnectorEvent>();
        using var client = _options.CreateMqttClient();
        var lastReportTicks = DateTimeOffset.UtcNow.UtcTicks;

        client.ApplicationMessageReceivedAsync += e =>
        {
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            if (!BambuMqttMessageParser.IsPrintReport(payload))
            {
                return Task.CompletedTask;
            }

            var hasGcodeState = BambuMqttMessageParser.TryParseGcodeState(payload, out var gcodeState);
            var telemetry = BambuMqttMessageParser.TryParseTelemetry(payload);
            var alerts = BambuAlertParser.Parse(payload, details.ModelCode);

            if (hasGcodeState || telemetry is not null)
            {
                Interlocked.Exchange(ref lastReportTicks, DateTimeOffset.UtcNow.UtcTicks);
                channel.Writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, GcodeState: gcodeState, Telemetry: telemetry, HmsAlerts: alerts.Hms, PrintErrors: alerts.PrintErrors));
            }

            return Task.CompletedTask;
        };

        client.ConnectedAsync += async _ =>
        {
            Interlocked.Exchange(ref lastReportTicks, DateTimeOffset.UtcNow.UtcTicks);
            channel.Writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, ConnectionStarted: true));
            LogConnected(_logger, details.SerialNumber);
            await SubscribeAndRequestAsync(client, details.SerialNumber, connectionToken);
        };

        client.DisconnectedAsync += e =>
        {
            if (e.ClientWasConnected) LogDisconnected(_logger, details.SerialNumber, e.Reason, e.Exception);
            channel.Writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, ConnectionEnded: true));
            return Task.CompletedTask;
        };

        var maintainTask = MaintainConnectionAsync(client, BuildClientOptions(details, _options), details.SerialNumber,
            _logger, () => new DateTimeOffset(Interlocked.Read(ref lastReportTicks), TimeSpan.Zero), channel.Writer, connectionToken);

        try
        {
            await foreach (var connectorEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return connectorEvent;
            }
        }
        finally
        {
            await connectionCts.CancelAsync();
            channel.Writer.TryComplete();
            await maintainTask;
        }
    }

    /// <summary>
    /// FTPS upload (port 990, implicit TLS — Bambu's LAN-mode FTP server convention) followed by an
    /// MQTT <c>print.project_file</c> publish (research.md R5). Uses the same short-lived
    /// connect/command/disconnect shape as <see cref="VerifyAsync"/> — whether the printer's broker
    /// tolerates this alongside an already-open <see cref="ConnectAsync"/> streaming connection is a
    /// real, open verification question for physical hardware (research.md R5's own documented risk),
    /// not settled here.
    /// </summary>
    public async Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken)
    {
        using var buffered = new MemoryStream();
        await fileContent.CopyToAsync(buffered, cancellationToken);
        using var project = new MemoryStream(BambuPrintFile.PrepareProject(buffered.ToArray(), jobName));
#pragma warning disable CA5351 // MD5 here is a Bambu protocol-mandated file-integrity checksum
        // (project_file's own "md5" field), not a security use.
        var md5 = Convert.ToHexStringLower(await MD5.HashDataAsync(project, cancellationToken));
#pragma warning restore CA5351
        project.Position = 0;

        var remoteFileName = PrintFileName.ForUpload(jobName);
        var remotePath = $"{FtpsCacheDirectory}/{remoteFileName}";
        var uploadStatus = await (_options.UploadFileAsync ?? UploadFileAsync)(
            details, project, remotePath, cancellationToken);
        if (uploadStatus != FtpStatus.Success)
        {
            throw new IOException($"Printer file upload did not succeed ({uploadStatus}).");
        }

        // Spelled out even for the external spool alone, so the printer never falls back to a tray of its own choosing.
        (bool UseAms, int[] Trays, (int AmsId, int SlotId)[] Targets)? mapping = feedMapping is null ? null : EncodeAmsMapping(feedMapping);
        var payload = JsonSerializer.Serialize(new
        {
            print = new
            {
                sequence_id = "0",
                command = "project_file",
                param = BambuPrintFile.PlatePath,
                project_id = "0",
                profile_id = "0",
                task_id = "0",
                subtask_id = "0",
                subtask_name = jobName,
                url = details.ModelCode is "P1P" or "P1S" or "A1" or "A1MINI"
                    ? $"file:///sdcard{remotePath}"
                    : $"ftp://{remotePath}",
                md5,
                timelapse = false,
                bed_type = "auto",
                bed_leveling = true,
                flow_cali = false,
                vibration_cali = false,
                layer_inspect = false,
                use_ams = mapping?.UseAms ?? false,
                ams_mapping = mapping?.Trays,
                ams_mapping2 = mapping?.Targets.Select(t => new { ams_id = t.AmsId, slot_id = t.SlotId }),
            },
        }, IgnoreNullOptions);
        await PublishCommandAsync(details, payload, cancellationToken);
    }

    /// <summary>
    /// Bambu Studio's encoding of a feed mapping. <c>ams_mapping</c> holds absolute tray ids — <c>unit * 4 + slot</c> for a
    /// 4-slot AMS or AMS Lite, the unit id for a single-slot AMS HT (ids from 128), -1 for an unused or external filament;
    /// <c>ams_mapping2</c> holds <c>{ams_id, slot_id}</c>, where the external spool is <c>{255, 0}</c> and unmapped is
    /// <c>{255, 255}</c>. <c>use_ams</c> is true when any filament comes from an AMS.
    /// </summary>
    internal static (bool UseAms, int[] Trays, (int AmsId, int SlotId)[] Targets) EncodeAmsMapping(FilamentFeedMapping mapping)
    {
        const int firstSingleSlotUnit = 128;
        const int unmapped = 255;
        var trays = new int[mapping.Tools.Count];
        var targets = new (int AmsId, int SlotId)[mapping.Tools.Count];
        for (var tool = 0; tool < mapping.Tools.Count; tool++)
        {
            (trays[tool], targets[tool]) = mapping.Tools[tool] switch
            {
                null => (-1, (unmapped, unmapped)),
                { IsMultiMaterialUnit: false } => (-1, (unmapped, 0)),
                { UnitIndex: >= firstSingleSlotUnit } slot => (slot.UnitIndex, (slot.UnitIndex, 0)),
                { } slot => (slot.UnitIndex * 4 + slot.SlotPosition, (slot.UnitIndex, slot.SlotPosition)),
            };
        }

        return (mapping.Tools.Any(t => t?.IsMultiMaterialUnit == true), trays, targets);
    }

    private async Task<FtpStatus> UploadFileAsync(
        PrinterConnectionDetails details, Stream content, string remotePath, CancellationToken cancellationToken)
    {
        await using var ftp = new AsyncFtpClient(details.IpAddress, MqttUsername, details.AccessCode, _options.FtpsPort);
        ftp.Config.EncryptionMode = FtpEncryptionMode.Implicit;
        // Preserve the existing FTPS trust behavior. MQTT and camera use bundled Bambu trust.
        ftp.Config.ValidateAnyCertificate = true;
        await ftp.AutoConnect(cancellationToken);
        return await UploadNewFileAsync(ftp, content, remotePath, cancellationToken);
    }

    internal static Task<FtpStatus> UploadNewFileAsync(
        AsyncFtpClient ftp, Stream content, string remotePath, CancellationToken cancellationToken) =>
        // StartJobAsync generates a distinct timestamped path for every upload. An existence check is
        // unnecessary and may reject unrelated printer filenames (e.g. slicer names containing
        // "....") when FileExists falls back to NLST. NoCheck still sanitizes our destination.
        ftp.UploadStream(content, remotePath, existsMode: FtpRemoteExists.NoCheck, token: cancellationToken);

    /// <inheritdoc />
    public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PublishCommandAsync(details, """{"print":{"sequence_id":"0","command":"pause"}}""", cancellationToken);

    /// <inheritdoc />
    public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PublishCommandAsync(details, """{"print":{"sequence_id":"0","command":"resume"}}""", cancellationToken);

    /// <inheritdoc />
    public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PublishCommandAsync(details, """{"print":{"sequence_id":"0","command":"stop"}}""", cancellationToken);

    /// <inheritdoc />
    public async Task<byte[]?> FetchPrintFileAsync(PrinterConnectionDetails details, string? jobName, string? gcodeFile, CancellationToken cancellationToken)
    {
        var candidates = CandidateFilePaths(jobName, gcodeFile).Distinct().ToList();
        if (candidates.Count == 0) return null;

        await using var ftp = new AsyncFtpClient(details.IpAddress, MqttUsername, details.AccessCode, _options.FtpsPort);
        ftp.Config.EncryptionMode = FtpEncryptionMode.Implicit;
        ftp.Config.ValidateAnyCertificate = true;
        await ftp.AutoConnect(cancellationToken);
        foreach (var path in candidates)
        {
            // No FileExists first: it falls back to listing the folder and rejects the whole check when any unrelated file there
            // has a name FluentFTP treats as a path-traversal attempt (e.g. "...Parametric....gcode.bbl"), which hid every
            // file under /cache on some printers. A missing file answers 550 at once.
            byte[]? bytes;
            try
            {
                bytes = await ftp.DownloadBytes(path, cancellationToken);
            }
            catch (FtpException ex) when (IsFileMissing(ex))
            {
                continue;
            }

            if (bytes is { Length: > 0 }) return bytes;
        }

        return null;
    }

    /// <summary>The server's 550 reply to a download of a file that is not there (or not readable).</summary>
    internal static bool IsFileMissing(FtpException exception) =>
        exception.InnerException is FtpCommandException { CompletionCode: "550" };

    /// <summary>
    /// Where a print the printer started itself is likely stored. Bambu Studio and Handy leave the project as
    /// <c>/cache/&lt;name&gt;.gcode.3mf</c> (or <c>.3mf</c>) while <c>gcode_file</c> usually names the plate inside it, so the
    /// reported name leads; a <c>gcode_file</c> that is itself a file on the card (SD prints) is tried too. Unverified on hardware.
    /// </summary>
    internal static IEnumerable<string> CandidateFilePaths(string? jobName, string? gcodeFile)
    {
        const string card = "/sdcard";
        if (!string.IsNullOrWhiteSpace(gcodeFile) && gcodeFile.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            yield return gcodeFile.StartsWith(card, StringComparison.Ordinal) ? gcodeFile[card.Length..] : gcodeFile;
        if (!string.IsNullOrWhiteSpace(jobName) && !jobName.Contains('/', StringComparison.Ordinal))
            foreach (var name in new[] { $"{jobName}.gcode.3mf", $"{jobName}.3mf" })
            {
                yield return $"{FtpsCacheDirectory}/{name}";
                yield return $"/{name}";
            }

        if (!string.IsNullOrWhiteSpace(gcodeFile) && gcodeFile.StartsWith(card + "/", StringComparison.Ordinal)
            && gcodeFile.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase))
            yield return gcodeFile[card.Length..];
    }

    /// <summary>Short-lived connect, publish one command, disconnect — mirrors <see cref="VerifyAsync"/>'s shape.</summary>
    private async Task PublishCommandAsync(PrinterConnectionDetails details, string payload, CancellationToken cancellationToken)
    {
        using var client = _options.CreateMqttClient();
        using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        commandCts.CancelAfter(_options.CommandTimeout);

        try
        {
            await client.ConnectAsync(BuildClientOptions(details, _options), commandCts.Token);
            await PublishRequestAsync(client, details.SerialNumber, payload, commandCts.Token);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            }
        }
    }

    private async Task MaintainConnectionAsync(
        IMqttClient client,
        MqttClientOptions options,
        string serialNumber,
        ILogger logger,
        Func<DateTimeOffset> lastReportUtc,
        ChannelWriter<PrinterConnectorEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (client.IsConnected && DateTimeOffset.UtcNow - lastReportUtc() > _options.TelemetryTimeout)
                    {
                        LogTelemetryStalled(logger, serialNumber);
                        using var disconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        disconnectCts.CancelAfter(_options.ConnectTimeout);
                        await client.DisconnectAsync(cancellationToken: disconnectCts.Token);
                    }
                    if (!client.IsConnected)
                    {
                        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        connectCts.CancelAfter(_options.ConnectTimeout);
                        await client.ConnectAsync(options, connectCts.Token);
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // A per-attempt timeout is recoverable; only lifecycle cancellation ends retries.
                    LogReconnectFailed(logger, serialNumber, ex);
                }

                await Task.Delay(_options.ReconnectDelay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        finally
        {
            writer.TryComplete();
            if (client.IsConnected)
            {
                using var cleanupCts = new CancellationTokenSource(_options.ConnectTimeout);
                await client.DisconnectAsync(cancellationToken: cleanupCts.Token);
            }
        }
    }

    private static async Task SubscribeAndRequestAsync(IMqttClient client, string serialNumber, CancellationToken cancellationToken)
    {
        await client.SubscribeAsync(
            new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(ReportTopic(serialNumber))
                .Build(),
            cancellationToken);

        await PublishRequestAsync(client, serialNumber, """{"info":{"sequence_id":"0","command":"get_version"}}""", cancellationToken);
        await PublishRequestAsync(client, serialNumber, """{"pushing":{"sequence_id":"0","command":"pushall"}}""", cancellationToken);
    }

    private static Task<MqttClientPublishResult> PublishRequestAsync(IMqttClient client, string serialNumber, string payload, CancellationToken cancellationToken) =>
        client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(RequestTopic(serialNumber))
                .WithPayload(payload)
                .Build(),
            cancellationToken);

    internal static MqttClientOptions BuildClientOptions(PrinterConnectionDetails details, BambuTransportOptions? options = null) =>
        new MqttClientOptionsBuilder()
            .WithTcpServer(details.IpAddress, options?.MqttPort ?? 8883)
            .WithCredentials(MqttUsername, details.AccessCode)
            // Lenient so brief Wi-Fi blips don't drop the session; TelemetryTimeout catches dead ones.
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(options?.ConnectTimeout ?? TimeSpan.FromSeconds(10))
            .WithCleanSession()
            // MQTTnet defaults to MQTT 5, but Bambu's LAN broker only speaks v3.1.1 (research.md §1;
            // confirmed against pybambu's mqtt.Client(protocol=mqtt.MQTTv311)) — a v5 CONNECT gets a
            // successful-looking CONNACK but the broker then can't parse the v5-shaped SUBSCRIBE that
            // follows and silently closes the socket, surfacing as MqttClientNotConnectedException on
            // the very next call.
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithClientId($"openfarm-{Guid.NewGuid()}")
            .WithTlsOptions(tls => tls
                .UseTls()
                // Bambu printers present a device-specific cert, not one from a public CA. Pin the
                // bundled per-device cert set (vendored from pybambu/certs/) as a custom trust root,
                // mirroring create_local_ssl_context()'s load_verify_locations() over every bundled
                // *.cert file — never a blanket accept-all (research.md §1).
                .WithCertificateValidationHandler(args => (options?.ValidateCertificate ?? BambuCertificateTrust.Validate)(args.Certificate))
                .WithSslProtocols(System.Security.Authentication.SslProtocols.Tls12))
            .Build();

    private static string ReportTopic(string serialNumber) => $"device/{serialNumber}/report";

    private static string RequestTopic(string serialNumber) => $"device/{serialNumber}/request";

    [LoggerMessage(Level = LogLevel.Information, Message = "MQTT connected to printer {SerialNumber}.")]
    private static partial void LogConnected(ILogger logger, string serialNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT disconnected from printer {SerialNumber} ({Reason}).")]
    private static partial void LogDisconnected(ILogger logger, string serialNumber, MqttClientDisconnectReason reason, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT reconnect attempt to printer {SerialNumber} failed.")]
    private static partial void LogReconnectFailed(ILogger logger, string serialNumber, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {SerialNumber} telemetry stalled despite an MQTT connection; reconnecting and requesting a full report.")]
    private static partial void LogTelemetryStalled(ILogger logger, string serialNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verify connection to {IpAddress} failed.")]
    private static partial void LogVerifyFailed(ILogger logger, string ipAddress, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verify connection to {IpAddress} timed out.")]
    private static partial void LogVerifyTimedOut(ILogger logger, string ipAddress);
}
