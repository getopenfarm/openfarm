namespace OpenFarm.Application.Printers;

public sealed record PrinterCameraSnapshot(string FrameJpegBase64, DateTimeOffset ReceivedAtUtc);

/// <summary>One persistent last frame per organization/printer. Storage failures are logged, never fatal to streaming.</summary>
public interface IPrinterCameraSnapshotStore
{
    Task SaveAsync(Guid organizationId, Guid printerId, PrinterCameraSnapshot snapshot, CancellationToken ct);
    Task<PrinterCameraSnapshot?> GetAsync(Guid organizationId, Guid printerId, CancellationToken ct);
    Task DeleteAsync(Guid organizationId, Guid printerId, CancellationToken ct);
}
