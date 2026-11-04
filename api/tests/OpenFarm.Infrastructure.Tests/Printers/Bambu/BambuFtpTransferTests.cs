using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

public class BambuFtpTransferTests
{
    private static readonly PrinterConnectionDetails Details = new("Bambu", "P1S", "127.0.0.1", "FTPS-TEST", "test-code");
    private static BambuFtpTransfer Transfer(BambuFtpsHarness server, TimeSpan? idle = null) =>
        new(new BambuTransportOptions
        {
            FtpsPort = server.Port,
            UploadInactivityTimeout = idle ?? TimeSpan.FromSeconds(3),
            UploadRetryDelay = TimeSpan.FromMilliseconds(10),
        }, NullLogger.Instance);

    [Fact]
    public async Task PassiveZeroAddress_UsesControlPeerAndEncryptedDataAfterStor()
    {
        await using var server = new BambuFtpsHarness { AdvertiseUnspecifiedAddress = true };
        using var bytes = new MemoryStream("print bytes"u8.ToArray());
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Transfer(server).EnsureAsync(Details, bytes, "/openfarm-test.3mf", null, ct.Token);
        server.UploadedBytes.Should().Equal(bytes.ToArray());
        server.Commands.Should().Contain("PASV").And.Contain("PROT P").And.Contain("SIZE /openfarm-test.3mf");
        server.Commands.Should().NotContain(c => c.StartsWith("NLST", StringComparison.Ordinal) || c == "EPSV");
        server.TransferEvents.Should().Equal("STOR", "data TLS ready");
    }

    [Fact]
    public async Task MissingFinalReply_ReconnectsAndVerifiesWithoutUploadingAgain()
    {
        await using var server = new BambuFtpsHarness { WithholdFinalReply = true };
        using var bytes = new MemoryStream("complete upload"u8.ToArray());
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Transfer(server, TimeSpan.FromMilliseconds(500)).EnsureAsync(Details, bytes, "/openfarm-complete.3mf", null, ct.Token);
        server.TransferCount.Should().Be(1);
        server.ConnectionCount.Should().Be(2);
    }

    [Fact]
    public async Task SizeMismatch_RetriesThreeTimesAndNeverAcceptsTheFile()
    {
        await using var server = new BambuFtpsHarness { ReportedSizeDelta = -1 };
        using var bytes = new MemoryStream("incomplete"u8.ToArray());
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upload = () => Transfer(server).EnsureAsync(Details, bytes, "/openfarm-mismatch.3mf", null, ct.Token);
        await upload.Should().ThrowAsync<BambuTransferException>().WithMessage("*3 attempts*");
        server.TransferCount.Should().Be(3);
    }

    [Theory]
    [InlineData("550", "writable")]
    [InlineData("552", "full")]
    [InlineData("553", "unavailable")]
    public async Task StorageError_IsPermanent(string code, string message)
    {
        await using var server = new BambuFtpsHarness { RejectStoreCode = code };
        using var bytes = new MemoryStream("data"u8.ToArray());
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upload = () => Transfer(server).EnsureAsync(Details, bytes, "/openfarm-storage.3mf", null, ct.Token);
        await upload.Should().ThrowAsync<BambuTransferException>().WithMessage($"*{message}*");
        server.ConnectionCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentSamePrinterUploads_SerializeAndReuseVerifiedFile()
    {
        await using var server = new BambuFtpsHarness();
        var transfer = Transfer(server);
        using var first = new MemoryStream("same artifact"u8.ToArray());
        using var second = new MemoryStream(first.ToArray());
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(transfer.EnsureAsync(Details, first, "/openfarm-shared.3mf", null, ct.Token),
            transfer.EnsureAsync(Details, second, "/openfarm-shared.3mf", null, ct.Token));
        server.TransferCount.Should().Be(1);
    }

    [Fact]
    public async Task StalledData_TimesOutAfterThreeAttempts()
    {
        await using var server = new BambuFtpsHarness { StallData = true };
        using var bytes = new MemoryStream(new byte[1024 * 1024]);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var upload = () => Transfer(server, TimeSpan.FromMilliseconds(400))
            .EnsureAsync(Details, bytes, "/openfarm-stall.3mf", null, ct.Token);
        await upload.Should().ThrowAsync<BambuTransferException>().WithMessage("*3 attempts*");
        server.TransferCount.Should().Be(3);
    }

    [Fact]
    public async Task CancellationDuringTransfer_PropagatesAndReleasesPrinterLock()
    {
        await using var server = new BambuFtpsHarness { StallData = true };
        var transfer = Transfer(server);
        using var first = new MemoryStream(new byte[1024 * 1024]);
        using var ct = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var upload = () => transfer.EnsureAsync(Details, first, "/openfarm-cancel.3mf", null, ct.Token);
        await upload.Should().ThrowAsync<OperationCanceledException>();
        using var second = new MemoryStream([1]);
        using var secondCt = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var retry = () => transfer.EnsureAsync(Details, second, "/openfarm-next.3mf", null, secondCt.Token);
        await retry.Should().ThrowAsync<OperationCanceledException>();
        server.Commands.Should().Contain("STOR /openfarm-next.3mf");
    }
}
