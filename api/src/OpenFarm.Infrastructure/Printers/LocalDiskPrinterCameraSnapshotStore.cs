using System.Collections.Concurrent;
using System.Text.Json;
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

public sealed partial class LocalDiskPrinterCameraSnapshotStore(
    IOptions<CameraSnapshotStorageOptions> options,
    ILogger<LocalDiskPrinterCameraSnapshotStore> logger) : IPrinterCameraSnapshotStore
{
    // Per-printer serialization makes replacement and deletion safe against in-flight frames.
    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool Deleted { get; set; }
    }
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private string FilePath(Guid printerId) =>
        Path.Combine(options.Value.RootDirectory, $"{printerId:N}.json");

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
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot), ct);
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
            var path = FilePath(printerId);
            if (!File.Exists(path)) return null;
            var snapshot = JsonSerializer.Deserialize<PrinterCameraSnapshot>(await File.ReadAllTextAsync(path, ct));
            return snapshot is not null && Valid(snapshot) ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
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
            File.Delete(FilePath(printerId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogStorageFailure(logger, ex, printerId); }
        finally { entry.Gate.Release(); }
    }

    private static bool Valid(PrinterCameraSnapshot snapshot)
    {
        if (snapshot.ReceivedAtUtc == default || string.IsNullOrEmpty(snapshot.FrameJpegBase64)
            || snapshot.FrameJpegBase64.Length > BambuCameraFrameCodec.MaxPayloadSize * 4 / 3 + 4) return false;
        try { return JpegFrame.IsValid(Convert.FromBase64String(snapshot.FrameJpegBase64)); }
        catch (FormatException) { return false; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera snapshot storage failed for printer {PrinterId}")]
    private static partial void LogStorageFailure(ILogger logger, Exception exception, Guid printerId);
}
