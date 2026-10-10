using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Real Bambu P1/A1-family camera relay: proprietary binary-JPEG-over-TLS push on TCP port 6000,
/// mirroring pybambu's <c>ChamberImageThread</c> connection/reconnect behavior (research.md §4).
/// Frame byte-layout parsing itself lives in <see cref="BambuCameraFrameCodec"/> so it can be
/// fixture-replay tested without a socket. X1-family <c>CAMERA_RTSP</c> is out of scope this phase
/// (plan.md's Complexity Tracking "optional fast-follow"). Never built/validated against real
/// hardware in this environment — see <c>Printers/Fixtures/README.md</c> and quickstart.md's manual
/// physical-hardware validation section.
/// </summary>
public sealed partial class BambuCameraRelay : IPrinterCameraRelay
{
    private readonly BambuTransportOptions _options;
    private readonly ILogger<BambuCameraRelay> _logger;

    public BambuCameraRelay() : this(new BambuTransportOptions())
    {
    }

    public BambuCameraRelay(ILogger<BambuCameraRelay> logger) : this(new BambuTransportOptions(), logger)
    {
    }

    internal BambuCameraRelay(BambuTransportOptions options, ILogger<BambuCameraRelay>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<BambuCameraRelay>.Instance;
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        PrinterConnectionDetails details,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        var pumpTask = MaintainConnectionAsync(details, channel.Writer, connectionCts.Token);

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }
        finally
        {
            await connectionCts.CancelAsync();
            channel.Writer.TryComplete();
            await pumpTask;
        }
    }

    private async Task MaintainConnectionAsync(PrinterConnectionDetails details, ChannelWriter<byte[]> writer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ReadFramesUntilDisconnectedAsync(details, writer, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogReconnectFailed(_logger, details.SerialNumber, _options.ReconnectDelay.TotalSeconds, ex);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(_options.ReconnectDelay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task ReadFramesUntilDisconnectedAsync(PrinterConnectionDetails details, ChannelWriter<byte[]> writer, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(_options.ConnectTimeout);
        await tcpClient.ConnectAsync(details.IpAddress, _options.CameraPort, connectCts.Token);

        await using var sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, certificate, _, _) => _options.ValidateCertificate(certificate));

        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = details.IpAddress,
                EnabledSslProtocols = SslProtocols.Tls12,
            },
            connectCts.Token);

        await sslStream.WriteAsync(BambuCameraFrameCodec.BuildAuthBlob(details.AccessCode), connectCts.Token);

        var header = new byte[BambuCameraFrameCodec.HeaderSize];
        while (!cancellationToken.IsCancellationRequested)
        {
            // A deadline for the whole frame also catches partial frames and slow trickles.
            using var frameCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            frameCts.CancelAfter(_options.CameraFrameTimeout);
            await ReadExactAsync(sslStream, header, frameCts.Token);
            var payloadSize = BambuCameraFrameCodec.ParsePayloadSize(header);
            if (payloadSize > BambuCameraFrameCodec.MaxPayloadSize)
            {
                throw new IOException(
                    $"Camera frame header claims a {payloadSize}-byte payload, exceeding the {BambuCameraFrameCodec.MaxPayloadSize}-byte sanity ceiling — treating as a desynced/malformed frame.");
            }

            var payload = new byte[payloadSize];
            await ReadExactAsync(sslStream, payload, frameCts.Token);

            if (BambuCameraFrameCodec.IsValidJpegFrame(payload))
            {
                await writer.WriteAsync(payload, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Reads until <paramref name="buffer"/> is fully populated. A zero-length read before that
    /// (stream closed by the printer — wrong access code/IP, per research.md §4) throws to trigger
    /// a reconnect rather than silently returning a short buffer.
    /// </summary>
    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("Camera connection closed by the printer (wrong access code or IP).");
            }

            offset += read;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera connection to printer {SerialNumber} failed or stalled; retrying in {DelaySeconds}s.")]
    private static partial void LogReconnectFailed(ILogger logger, string serialNumber, double delaySeconds, Exception exception);
}
