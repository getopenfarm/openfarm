using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers;

namespace OpenFarm.Infrastructure.Tests.Printers;

public sealed class PrinterCameraSnapshotStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openfarm-camera-{Guid.NewGuid():N}");
    private static readonly byte[] Jpeg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Printers/Fixtures/sample-camera-frame.jpg"));
    private LocalDiskPrinterCameraSnapshotStore Store(string? root = null) => new(
        Options.Create(new CameraSnapshotStorageOptions { RootDirectory = root ?? _root }),
        NullLogger<LocalDiskPrinterCameraSnapshotStore>.Instance);
    private static PrinterCameraSnapshot Frame(int secondsAgo = 0, byte[]? bytes = null) =>
        new(bytes ?? Jpeg, DateTimeOffset.UtcNow.AddSeconds(-secondsAgo));

    private static void AssertSame(PrinterCameraSnapshot expected, PrinterCameraSnapshot? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.FrameJpeg, actual.FrameJpeg);
        // The disk copy keeps its receipt time in the file's last-write time, at file-system precision.
        Assert.True((expected.ReceivedAtUtc - actual.ReceivedAtUtc).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LatestFrameIsServedFromMemory_AndDiskIsWrittenAtMostOncePerInterval()
    {
        var printer = Guid.NewGuid();
        var first = Frame(60);
        var latest = Frame(bytes: [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0xFF, 0xD9]);
        var store = Store();
        await store.SaveAsync(printer, first, default);
        await store.SaveAsync(printer, latest, default);

        AssertSame(latest, await store.GetAsync(printer, default));
        // A second save within the interval stays in memory: a new process still sees the first frame.
        AssertSame(first, await Store().GetAsync(printer, default));
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.Null(await store.GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task InvalidFramesDoNotReplacePreviousSnapshot()
    {
        var printer = Guid.NewGuid(); var good = Frame();
        var store = Store();
        await store.SaveAsync(printer, good, default);
        await store.SaveAsync(printer, Frame(bytes: [1, 2, 3]), default);
        await store.SaveAsync(printer, Frame(bytes: []), default);
        AssertSame(good, await store.GetAsync(printer, default));
    }

    [Fact]
    public async Task AnExifJpeg_IsKeptLikeAJfifOne()
    {
        // A Snapmaker U1 snapshot need not start with Bambu's JFIF APP0 segment.
        var printer = Guid.NewGuid();
        var exif = Frame(bytes: [0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x10, 0xFF, 0xD9]);

        await Store().SaveAsync(printer, exif, default);

        AssertSame(exif, await Store().GetAsync(printer, default));
    }

    [Fact]
    public async Task DeletionPreventsLateWritesAndSurvivesRestart()
    {
        var printer = Guid.NewGuid(); var store = Store();
        var writes = Enumerable.Range(0, 10).Select(_ => store.SaveAsync(printer, Frame(), default)).ToArray();
        await store.DeleteAsync(printer, default);
        await Task.WhenAll(writes);
        await store.SaveAsync(printer, Frame(), default);
        Assert.Null(await store.GetAsync(printer, default));
        Assert.Null(await Store().GetAsync(printer, default));
    }

    [Fact]
    public async Task StorageFailuresAreNonFatalAndCorruptFilesAreUnavailable()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "file");
        await File.WriteAllTextAsync(blocker, "block directory creation");
        var printer = Guid.NewGuid();
        await Store(blocker).SaveAsync(printer, Frame(), default);
        Assert.Null(await Store(blocker).GetAsync(printer, default));
        await Store().SaveAsync(printer, Frame(), default);
        await File.WriteAllTextAsync(Directory.GetFiles(_root, "*.jpg", SearchOption.AllDirectories).Single(), "broken");
        Assert.Null(await Store().GetAsync(printer, default));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
