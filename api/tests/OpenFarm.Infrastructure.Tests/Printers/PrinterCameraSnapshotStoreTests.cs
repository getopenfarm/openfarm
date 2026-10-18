using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers;

namespace OpenFarm.Infrastructure.Tests.Printers;

public sealed class PrinterCameraSnapshotStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openfarm-camera-{Guid.NewGuid():N}");
    private LocalDiskPrinterCameraSnapshotStore Store(string? root = null) => new(
        Options.Create(new CameraSnapshotStorageOptions { RootDirectory = root ?? _root }),
        NullLogger<LocalDiskPrinterCameraSnapshotStore>.Instance);
    private static PrinterCameraSnapshot Frame(int secondsAgo = 0) => new(
        Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Printers/Fixtures/sample-camera-frame.jpg"))),
        DateTimeOffset.UtcNow.AddSeconds(-secondsAgo));

    [Fact]
    public async Task LatestFrameAndTimestampSurviveNewStoreInstance()
    {
        var printer = Guid.NewGuid();
        var first = Frame(60); var latest = Frame();
        var store = Store();
        await store.SaveAsync(printer, first, default);
        await store.SaveAsync(printer, latest, default);
        Assert.Equal(latest, await Store().GetAsync(printer, default));
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.Null(await store.GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task InvalidFramesDoNotReplacePreviousSnapshot()
    {
        var printer = Guid.NewGuid(); var good = Frame();
        var store = Store();
        await store.SaveAsync(printer, good, default);
        await store.SaveAsync(printer, new("not base64", DateTimeOffset.UtcNow), default);
        await store.SaveAsync(printer, new(Convert.ToBase64String([1, 2, 3]), DateTimeOffset.UtcNow), default);
        Assert.Equal(good, await store.GetAsync(printer, default));
    }

    [Fact]
    public async Task DeletionPreventsLateWritesAndSurvivesRestart()
    {
        var printer = Guid.NewGuid(); var store = Store();
        var writes = Enumerable.Range(0, 10).Select(_ => store.SaveAsync(printer, Frame(), default)).ToArray();
        await store.DeleteAsync(printer, default);
        await Task.WhenAll(writes);
        await store.SaveAsync(printer, Frame(), default);
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
        var store = Store();
        await store.SaveAsync(printer, Frame(), default);
        await File.WriteAllTextAsync(Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories).Single(), "{broken");
        Assert.Null(await store.GetAsync(printer, default));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
