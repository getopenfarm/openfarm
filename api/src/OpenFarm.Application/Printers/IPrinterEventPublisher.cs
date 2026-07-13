using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>
/// Application-defined contract for pushing real-time printer events (contracts/farm-hub.md),
/// implemented in <c>OpenFarm.Api</c> over the <c>/hubs/farm</c> SignalR hub so
/// <c>PrinterConnectionBackgroundService</c> (Infrastructure) never references SignalR/Api types
/// directly.
/// </summary>
public interface IPrinterEventPublisher
{
    Task PrinterStatusChangedAsync(
        Guid organizationId,
        Guid printerId,
        PrinterStatus status,
        bool isReachable,
        CancellationToken cancellationToken);

    Task PrinterTelemetryUpdatedAsync(
        Guid organizationId,
        Guid printerId,
        PrinterTelemetrySnapshot telemetry,
        CancellationToken cancellationToken);

    Task CameraFrameReceivedAsync(
        Guid organizationId,
        Guid printerId,
        string frameJpegBase64,
        CancellationToken cancellationToken);
}
