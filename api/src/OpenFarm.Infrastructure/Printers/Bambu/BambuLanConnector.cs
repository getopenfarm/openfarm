using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FluentFTP;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using OpenFarm.Application.Printers;
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

        client.ApplicationMessageReceivedAsync += e =>
        {
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            if (!BambuMqttMessageParser.IsPrintReport(payload))
            {
                return Task.CompletedTask;
            }

            var hasGcodeState = BambuMqttMessageParser.TryParseGcodeState(payload, out var gcodeState);
            var telemetry = BambuMqttMessageParser.TryParseTelemetry(payload);

            if (hasGcodeState || telemetry is not null)
            {
                channel.Writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, GcodeState: gcodeState, Telemetry: telemetry));
            }

            return Task.CompletedTask;
        };

        client.ConnectedAsync += async _ =>
        {
            channel.Writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, ConnectionStarted: true));
            LogConnected(_logger, details.SerialNumber);
            await SubscribeAndRequestAsync(client, details.SerialNumber, connectionToken);
        };

        client.DisconnectedAsync += _ =>
        {
            channel.Writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, ConnectionEnded: true));
            return Task.CompletedTask;
        };

        var maintainTask = MaintainConnectionAsync(client, BuildClientOptions(details, _options), details.SerialNumber, _logger, connectionToken);

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
    public async Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, CancellationToken cancellationToken)
    {
        using var buffered = new MemoryStream();
        await fileContent.CopyToAsync(buffered, cancellationToken);
        using var project = new MemoryStream(BambuPrintFile.PrepareProject(buffered.ToArray(), jobName));
#pragma warning disable CA5351 // MD5 here is a Bambu protocol-mandated file-integrity checksum
        // (project_file's own "md5" field), not a security use.
        var md5 = Convert.ToHexStringLower(await MD5.HashDataAsync(project, cancellationToken));
#pragma warning restore CA5351
        project.Position = 0;

        var remoteFileName = PrintFileName.ForBambu(jobName);
        var remotePath = $"{FtpsCacheDirectory}/{remoteFileName}";
        var uploadStatus = await (_options.UploadFileAsync ?? UploadFileAsync)(
            details, project, remotePath, cancellationToken);
        if (uploadStatus != FtpStatus.Success)
        {
            throw new IOException($"Printer file upload did not succeed ({uploadStatus}).");
        }

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
                use_ams = false,
            },
        });
        await PublishCommandAsync(details, payload, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!client.IsConnected)
                {
                    try
                    {
                        await client.ConnectAsync(options, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogReconnectFailed(logger, serialNumber, ex);
                    }
                }

                await Task.Delay(_options.ReconnectDelay, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(cancellationToken: CancellationToken.None);
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
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(5))
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT reconnect attempt to printer {SerialNumber} failed.")]
    private static partial void LogReconnectFailed(ILogger logger, string serialNumber, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verify connection to {IpAddress} failed.")]
    private static partial void LogVerifyFailed(ILogger logger, string ipAddress, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verify connection to {IpAddress} timed out.")]
    private static partial void LogVerifyTimedOut(ILogger logger, string ipAddress);
}
