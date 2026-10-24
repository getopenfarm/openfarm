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
    Task PrinterActivityChangedAsync(Guid printerId, CancellationToken cancellationToken) => Task.CompletedTask;

    Task PrinterStatusChangedAsync(
        Guid printerId,
        PrinterStatus status,
        bool isReachable,
        CancellationToken cancellationToken);

    /// <param name="model">The printer's catalog model, which names its feed units and slots; null when the catalog doesn't know it.</param>
    Task PrinterTelemetryUpdatedAsync(
        Guid printerId,
        PrinterModel? model,
        PrinterTelemetrySnapshot telemetry,
        CancellationToken cancellationToken);

    Task CameraFrameReceivedAsync(
        Guid printerId,
        string frameJpegBase64,
        CancellationToken cancellationToken);
}
