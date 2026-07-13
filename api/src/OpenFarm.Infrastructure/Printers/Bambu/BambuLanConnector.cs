using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using OpenFarm.Application.Printers;

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
    private const int MqttPort = 8883;
    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly ILogger<BambuLanConnector> _logger;

    public BambuLanConnector(ILogger<BambuLanConnector> logger)
    {
        _logger = logger;
    }

    public async Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        using var client = new MqttClientFactory().CreateMqttClient();
        var receivedInfo = false;
        var receivedPrint = false;
        var completion = new TaskCompletionSource();

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
        verifyCts.CancelAfter(VerifyTimeout);

        try
        {
            await client.ConnectAsync(BuildClientOptions(details), verifyCts.Token);
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
        var channel = Channel.CreateUnbounded<PrinterConnectorEvent>();
        using var client = new MqttClientFactory().CreateMqttClient();

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
            LogConnected(_logger, details.SerialNumber);
            await SubscribeAndRequestAsync(client, details.SerialNumber, cancellationToken);
        };

        var maintainTask = MaintainConnectionAsync(client, BuildClientOptions(details), details.SerialNumber, _logger, cancellationToken);

        try
        {
            await foreach (var connectorEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return connectorEvent;
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            await maintainTask;
        }
    }

    private static async Task MaintainConnectionAsync(
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

                await Task.Delay(ReconnectDelay, cancellationToken);
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

    internal static MqttClientOptions BuildClientOptions(PrinterConnectionDetails details) =>
        new MqttClientOptionsBuilder()
            .WithTcpServer(details.IpAddress, MqttPort)
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
                .WithCertificateValidationHandler(ValidateServerCertificate)
                .WithSslProtocols(System.Security.Authentication.SslProtocols.Tls12))
            .Build();

    private static bool ValidateServerCertificate(MqttClientCertificateValidationEventArgs args) =>
        BambuCertificateTrust.Validate(args.Certificate);

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
