namespace OpenFarm.Application.Printers;

public sealed record PrinterCameraSnapshot(byte[] FrameJpeg, DateTimeOffset ReceivedAtUtc);

/// <summary>One last frame per printer, served to the UI and kept across restarts. Storage failures are logged, never fatal to streaming.</summary>
public interface IPrinterCameraSnapshotStore
{
    Task SaveAsync(Guid printerId, PrinterCameraSnapshot snapshot, CancellationToken ct);
    Task<PrinterCameraSnapshot?> GetAsync(Guid printerId, CancellationToken ct);
    Task DeleteAsync(Guid printerId, CancellationToken ct);
}
