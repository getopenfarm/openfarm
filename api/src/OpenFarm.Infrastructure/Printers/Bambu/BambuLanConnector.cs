using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using MQTTnet;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Real Bambu LAN connector: MQTT 3.1.1 over TLS on port 8883, username <c>bblp</c> / password =
/// the printer's LAN access code, topics <c>device/{serial}/report</c> (subscribe) and
/// <c>device/{serial}/request</c> (publish), per research.md §1. Never built/validated against real
/// hardware in this environment — see <c>Printers/Fixtures/README.md</c> and quickstart.md's manual
/// physical-hardware validation section.
/// </summary>
public sealed class BambuLanConnector : IPrinterConnector
{
    private const string MqttUsername = "bblp";
    private const int MqttPort = 8883;
    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

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
            return false;
        }
        catch (Exception)
        {
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
            if (BambuMqttMessageParser.TryParseGcodeState(payload, out var gcodeState))
            {
                channel.Writer.TryWrite(new PrinterConnectorEvent(DateTimeOffset.UtcNow, GcodeState: gcodeState));
            }

            return Task.CompletedTask;
        };

        client.ConnectedAsync += async _ => await SubscribeAndRequestAsync(client, details.SerialNumber, cancellationToken);

        var maintainTask = MaintainConnectionAsync(client, BuildClientOptions(details), cancellationToken);

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

    private static async Task MaintainConnectionAsync(IMqttClient client, MqttClientOptions options, CancellationToken cancellationToken)
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
                    catch (Exception)
                    {
                        // Reconnect attempt failed; retry after the delay below.
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

    private static MqttClientOptions BuildClientOptions(PrinterConnectionDetails details) =>
        new MqttClientOptionsBuilder()
            .WithTcpServer(details.IpAddress, MqttPort)
            .WithCredentials(MqttUsername, details.AccessCode)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(5))
            .WithCleanSession()
            .WithTlsOptions(tls => tls
                .UseTls()
                // Bambu printers present a device-specific cert, not one from a public CA. Pin the
                // bundled per-device cert set (vendored from pybambu/certs/) as a custom trust root,
                // mirroring create_local_ssl_context()'s load_verify_locations() over every bundled
                // *.cert file — never a blanket accept-all (research.md §1).
                .WithCertificateValidationHandler(ValidateServerCertificate)
                .WithSslProtocols(System.Security.Authentication.SslProtocols.Tls12))
            .Build();

    private static bool ValidateServerCertificate(MqttClientCertificateValidationEventArgs args)
    {
        if (args.Certificate is not X509Certificate2 serverCertificate)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(BundledTrustedCertificates.Value);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        // Bambu's certs may lack a Key Usage extension — pybambu clears VERIFY_X509_STRICT for the
        // same reason (research.md §1); hostname is intentionally never checked (printers are
        // addressed by LAN IP, matching pybambu's check_hostname = False).
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreInvalidBasicConstraints;

        return chain.Build(serverCertificate);
    }

    private static readonly Lazy<X509Certificate2Collection> BundledTrustedCertificates = new(LoadBundledCertificates);

    private static X509Certificate2Collection LoadBundledCertificates()
    {
        var collection = new X509Certificate2Collection();
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var resourceName in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".cert", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            collection.Add(X509CertificateLoader.LoadCertificate(memory.ToArray()));
        }

        return collection;
    }

    private static string ReportTopic(string serialNumber) => $"device/{serialNumber}/report";

    private static string RequestTopic(string serialNumber) => $"device/{serialNumber}/request";
}
