using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Threading.Channels;
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
public sealed class BambuCameraRelay : IPrinterCameraRelay
{
    private const int CameraPort = 6000;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        PrinterConnectionDetails details,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        var pumpTask = MaintainConnectionAsync(details, channel.Writer, cancellationToken);

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            await pumpTask;
        }
    }

    private static async Task MaintainConnectionAsync(PrinterConnectionDetails details, ChannelWriter<byte[]> writer, CancellationToken cancellationToken)
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
                catch (Exception)
                {
                    // Connection dropped or rejected (wrong access code/IP) — retry after a delay,
                    // mirroring ChamberImageThread's reconnect-with-backoff loop. Attempts aren't
                    // capped here since PrinterConnectionBackgroundService owns the printer's
                    // overall lifecycle/reachability tracking, not this relay.
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(ReconnectDelay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private static async Task ReadFramesUntilDisconnectedAsync(PrinterConnectionDetails details, ChannelWriter<byte[]> writer, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(details.IpAddress, CameraPort, cancellationToken);

        await using var sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, certificate, _, _) => BambuCertificateTrust.Validate(certificate));

        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = details.IpAddress,
                EnabledSslProtocols = SslProtocols.Tls12,
            },
            cancellationToken);

        await sslStream.WriteAsync(BambuCameraFrameCodec.BuildAuthBlob(details.AccessCode), cancellationToken);

        var header = new byte[BambuCameraFrameCodec.HeaderSize];
        while (!cancellationToken.IsCancellationRequested)
        {
            await ReadExactAsync(sslStream, header, cancellationToken);
            var payloadSize = BambuCameraFrameCodec.ParsePayloadSize(header);

            var payload = new byte[payloadSize];
            await ReadExactAsync(sslStream, payload, cancellationToken);

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
}
