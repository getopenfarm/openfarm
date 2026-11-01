using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Printers;

public sealed class CameraSnapshotStorageOptions
{
    public const string SectionName = "CameraSnapshotStorage";
    public string RootDirectory { get; set; } = "/data/camera-snapshots";
}

/// <summary>
/// The latest frame lives in memory and is served from there; disk only keeps a frame across restarts, written at most
/// once per <see cref="PersistInterval"/> per printer as a plain JPEG whose last-write time is the receipt time.
/// </summary>
public sealed partial class LocalDiskPrinterCameraSnapshotStore(
    IOptions<CameraSnapshotStorageOptions> options,
    ILogger<LocalDiskPrinterCameraSnapshotStore> logger) : IPrinterCameraSnapshotStore
{
    // ponytail: a restart loses up to this much of the last frame; flush on shutdown if that ever matters.
    internal static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(60);

    // Per-printer serialization makes replacement and deletion safe against in-flight frames.
    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool Deleted { get; set; }
        public PrinterCameraSnapshot? Latest { get; set; }
        public DateTimeOffset LastPersistedUtc { get; set; } = DateTimeOffset.MinValue;
    }
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private string FilePath(Guid printerId) =>
        Path.Combine(options.Value.RootDirectory, $"{printerId:N}.jpg");

    public async Task SaveAsync(Guid printerId, PrinterCameraSnapshot snapshot, CancellationToken ct)
    {
        if (!Valid(snapshot)) return;
        var entry = _entries.GetOrAdd(printerId, _ => new Entry());
        await entry.Gate.WaitAsync(ct);
        var path = FilePath(printerId);
        var temporary = path + ".tmp";
        try
        {
            if (entry.Deleted) return;
            entry.Latest = snapshot;
            var now = DateTimeOffset.UtcNow;
            if (now - entry.LastPersistedUtc < PersistInterval) return;
            entry.LastPersistedUtc = now;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(temporary, snapshot.FrameJpeg, ct);
            File.SetLastWriteTimeUtc(temporary, snapshot.ReceivedAtUtc.UtcDateTime);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogStorageFailure(logger, ex, printerId);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogStorageFailure(logger, ex, printerId); }
            entry.Gate.Release();
        }
    }

    public async Task<PrinterCameraSnapshot?> GetAsync(Guid printerId, CancellationToken ct)
    {
        var entry = _entries.GetOrAdd(printerId, _ => new Entry());
        await entry.Gate.WaitAsync(ct);
        try
        {
            if (entry.Deleted) return null;
            if (entry.Latest is not null) return entry.Latest;
            var path = FilePath(printerId);
            if (!File.Exists(path)) return null;
            var snapshot = new PrinterCameraSnapshot(await File.ReadAllBytesAsync(path, ct), File.GetLastWriteTimeUtc(path));
            if (!Valid(snapshot)) return null;
            entry.Latest = snapshot;
            return snapshot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogStorageFailure(logger, ex, printerId);
            return null;
        }
        finally { entry.Gate.Release(); }
    }

    public async Task DeleteAsync(Guid printerId, CancellationToken ct)
    {
        var entry = _entries.GetOrAdd(printerId, _ => new Entry());
        await entry.Gate.WaitAsync(ct);
        try
        {
            entry.Deleted = true;
            entry.Latest = null;
            File.Delete(FilePath(printerId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogStorageFailure(logger, ex, printerId); }
        finally { entry.Gate.Release(); }
    }

    private static bool Valid(PrinterCameraSnapshot snapshot) =>
        snapshot.ReceivedAtUtc != default
        && snapshot.FrameJpeg.Length <= BambuCameraFrameCodec.MaxPayloadSize
        && JpegFrame.IsValid(snapshot.FrameJpeg);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera snapshot storage failed for printer {PrinterId}")]
    private static partial void LogStorageFailure(ILogger logger, Exception exception, Guid printerId);
}
