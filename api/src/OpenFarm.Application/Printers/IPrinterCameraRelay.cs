namespace OpenFarm.Application.Printers;

/// <summary>
/// Application-defined contract for a printer's live camera feed, implemented in Infrastructure
/// (e.g. <c>BambuCameraRelay</c> for the <c>CAMERA_IMAGE</c> binary-JPEG transport). Kept separate
/// from <see cref="IPrinterConnector"/> because camera transport is chosen per-model
/// (<c>PrinterModelCatalog.CameraTransport</c>) independently of the MQTT status/telemetry
/// connection, and X1-family RTSP support (<c>CAMERA_RTSP</c>) is deferred this phase — only
/// <c>CAMERA_IMAGE</c> (P1/A1-family) ships.
/// </summary>
public interface IPrinterCameraRelay
{
    /// <summary>
    /// Long-lived stream of decoded JPEG frame bytes. Reconnect/backoff is the implementation's
    /// responsibility; the stream completes when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<byte[]> StreamFramesAsync(PrinterConnectionDetails details, CancellationToken cancellationToken);
}
