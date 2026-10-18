namespace OpenFarm.Application.Printers;

public sealed record PrinterCameraSnapshot(string FrameJpegBase64, DateTimeOffset ReceivedAtUtc);

/// <summary>One persistent last frame per printer. Storage failures are logged, never fatal to streaming.</summary>
public interface IPrinterCameraSnapshotStore
{
    Task SaveAsync(Guid printerId, PrinterCameraSnapshot snapshot, CancellationToken ct);
    Task<PrinterCameraSnapshot?> GetAsync(Guid printerId, CancellationToken ct);
    Task DeleteAsync(Guid printerId, CancellationToken ct);
}
