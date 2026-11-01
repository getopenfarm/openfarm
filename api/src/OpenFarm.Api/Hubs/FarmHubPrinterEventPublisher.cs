using Microsoft.AspNetCore.SignalR;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Hubs;

public sealed class FarmHubPrinterEventPublisher(IFarmEventClients clients, IPrinterCameraSnapshotStore snapshots) : IPrinterEventPublisher
{
    public Task PrinterActivityChangedAsync(Guid printerId, CancellationToken cancellationToken) =>
        Group.SendAsync("PrinterActivityChanged", new { printerId }, cancellationToken);

    public Task PrinterStatusChangedAsync(
        Guid printerId,
        PrinterStatus status,
        bool isReachable,
        CancellationToken cancellationToken) =>
        Group.SendAsync(
            "PrinterStatusChanged",
            new { printerId, status = status.ToString(), isReachable },
            cancellationToken);

    public Task PrinterTelemetryUpdatedAsync(
        Guid printerId,
        PrinterModel? model,
        PrinterTelemetrySnapshot telemetry,
        CancellationToken cancellationToken) =>
        Group.SendAsync(
            "PrinterTelemetryUpdated",
            new
            {
                printerId,
                telemetry = new
                {
                    reportedState = telemetry.ReportedState,
                    stateObservedAtUtc = telemetry.StateObservedAtUtc,
                    activeAlerts = telemetry.ActiveAlerts ?? [],
                    nozzleDiameterMm = telemetry.NozzleDiameterMm,
                    nozzleObservedAtUtc = telemetry.NozzleObservedAtUtc,
                    nozzleConfirmed = telemetry.NozzleConfirmed,
                    extruders = ExtruderResponse.From(telemetry.Extruders),
                    nozzleTempC = telemetry.NozzleTempC,
                    bedTempC = telemetry.BedTempC,
                    printProgressPercent = telemetry.PrintProgressPercent,
                    currentStageLabel = telemetry.CurrentStageLabel,
                    remainingTimeMinutes = telemetry.RemainingTimeMinutes,
                    currentJobName = telemetry.CurrentJobName,
                },
                feedReadings = FeedReadingResponse.From(telemetry.FeedReadings, model),
            },
            cancellationToken);

    public async Task CameraFrameReceivedAsync(
        Guid printerId,
        string frameJpegBase64,
        CancellationToken cancellationToken)
    {
        var receivedAtUtc = DateTimeOffset.UtcNow;
        await snapshots.SaveAsync(printerId, new(frameJpegBase64, receivedAtUtc), cancellationToken);
        await Group.SendAsync(
            "CameraFrameReceived",
            new { printerId, frameJpegBase64, receivedAtUtc },
            cancellationToken);
    }

    private IClientProxy Group => clients;
}
