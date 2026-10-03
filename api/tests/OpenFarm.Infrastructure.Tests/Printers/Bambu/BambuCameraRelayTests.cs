using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Security;
using FluentAssertions;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

public class BambuCameraRelayTests
{
    private static readonly PrinterConnectionDetails Details = new("Bambu", "P1S", "127.0.0.1", "TEST-P1S", "test-code");

    [Fact]
    public async Task StreamFrames_AuthenticatesAndReadsFragmentedFramesSkippingInvalidJpeg()
    {
        var authentications = new ConcurrentQueue<byte[]>();
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9];
        await using var server = new LoopbackTlsServer(async (stream, token) =>
        {
            var auth = new byte[80];
            await stream.ReadExactlyAsync(auth, token);
            authentications.Enqueue(auth);
            await WriteFrameAsync(stream, [1, 2, 3], token);
            await WriteFrameAsync(stream, jpeg, token);
            // Keep the connection open so ending the enumerator, rather than server EOF, owns shutdown.
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var relay = CreateRelay(server);
        await using var frames = relay.StreamFramesAsync(Details, CancellationToken.None).GetAsyncEnumerator();

        (await frames.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3))).Should().BeTrue();
        frames.Current.Should().Equal(jpeg);
        var authBlob = authentications.Should().ContainSingle().Which;
        BinaryPrimitives.ReadUInt32LittleEndian(authBlob.AsSpan(0, 4)).Should().Be(0x40);
        BinaryPrimitives.ReadUInt32LittleEndian(authBlob.AsSpan(4, 4)).Should().Be(0x3000);
        System.Text.Encoding.ASCII.GetString(authBlob, 16, 32).TrimEnd('\0').Should().Be("bblp");
        System.Text.Encoding.ASCII.GetString(authBlob, 48, 32).TrimEnd('\0').Should().Be(Details.AccessCode);

        await frames.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData("eof")]
    [InlineData("partial-header")]
    [InlineData("partial-payload")]
    [InlineData("oversized")]
    public async Task StreamFrames_ReconnectsAfterTruncationOrOversizedHeader(string failure)
    {
        var connections = 0;
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xD9];
        await using var server = new LoopbackTlsServer(async (stream, token) =>
        {
            await stream.ReadExactlyAsync(new byte[80], token);
            if (Interlocked.Increment(ref connections) == 1)
            {
                switch (failure)
                {
                    case "partial-header":
                        await stream.WriteAsync(new byte[5], token);
                        break;
                    case "partial-payload":
                        await stream.WriteAsync(BuildHeader(jpeg.Length), token);
                        await stream.WriteAsync(jpeg.AsMemory(0, 3), token);
                        break;
                    case "oversized":
                        await stream.WriteAsync(BuildHeader(BambuCameraFrameCodec.MaxPayloadSize + 1), token);
                        break;
                }

                return;
            }

            await WriteFrameAsync(stream, jpeg, token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        await using var frames = CreateRelay(server).StreamFramesAsync(Details, CancellationToken.None).GetAsyncEnumerator();

        (await frames.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3))).Should().BeTrue();
        frames.Current.Should().Equal(jpeg);
        Volatile.Read(ref connections).Should().Be(2);
    }

    [Fact]
    public async Task StreamFrames_CancellationDuringBlockedRead_StopsPromptly()
    {
        var authenticated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LoopbackTlsServer(async (stream, token) =>
        {
            await stream.ReadExactlyAsync(new byte[80], token);
            authenticated.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var frames = CreateRelay(server).StreamFramesAsync(Details, cts.Token).GetAsyncEnumerator();
        var read = frames.MoveNextAsync().AsTask();
        await authenticated.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await cts.CancelAsync();

        var pendingRead = async () => await read.WaitAsync(TimeSpan.FromSeconds(3));
        await pendingRead.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StreamFrames_ProductionTrustRejectsTestCertificateBeforeSendingCredentials()
    {
        var authenticated = 0;
        await using var server = new LoopbackTlsServer(async (stream, token) =>
        {
            await stream.ReadExactlyAsync(new byte[80], token);
            Interlocked.Increment(ref authenticated);
        });
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var relay = new BambuCameraRelay(new BambuTransportOptions
        {
            CameraPort = server.Port,
            ReconnectDelay = TimeSpan.FromMilliseconds(30),
            ValidateCertificate = certificate =>
            {
                var trusted = BambuCertificateTrust.Validate(certificate);
                if (!trusted)
                {
                    rejected.TrySetResult();
                }

                return trusted;
            },
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var frames = relay.StreamFramesAsync(Details, cts.Token).GetAsyncEnumerator();
        var read = frames.MoveNextAsync().AsTask();
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cts.CancelAsync();

        var pendingRead = async () => await read.WaitAsync(TimeSpan.FromSeconds(3));
        await pendingRead.Should().ThrowAsync<OperationCanceledException>();
        Volatile.Read(ref authenticated).Should().Be(0);
    }

    private static BambuCameraRelay CreateRelay(LoopbackTlsServer server) => new(new BambuTransportOptions
    {
        CameraPort = server.Port,
        ValidateCertificate = server.TrustCertificate,
        ReconnectDelay = TimeSpan.FromMilliseconds(30),
    });

    private static byte[] BuildHeader(int payloadSize)
    {
        var header = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(header, payloadSize);
        return header;
    }

    private static async Task WriteFrameAsync(SslStream stream, byte[] jpeg, CancellationToken cancellationToken)
    {
        var header = BuildHeader(jpeg.Length);
        // Separate TLS writes split both the header and payload across actual stream reads.
        await stream.WriteAsync(header.AsMemory(0, 5), cancellationToken);
        await Task.Delay(5, cancellationToken);
        await stream.WriteAsync(header.AsMemory(5), cancellationToken);
        await stream.WriteAsync(jpeg.AsMemory(0, 2), cancellationToken);
        await Task.Delay(5, cancellationToken);
        await stream.WriteAsync(jpeg.AsMemory(2), cancellationToken);
    }
}
