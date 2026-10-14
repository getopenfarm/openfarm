using Microsoft.AspNetCore.SignalR;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Hubs;

public sealed class FarmHubPrinterEventPublisher(IHubContext<FarmHub> hubContext, IPrinterCameraSnapshotStore snapshots) : IPrinterEventPublisher
{
    public Task PrinterActivityChangedAsync(Guid organizationId, Guid printerId, CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync("PrinterActivityChanged", new { printerId }, cancellationToken);

    public Task PrinterStatusChangedAsync(
        Guid organizationId,
        Guid printerId,
        PrinterStatus status,
        bool isReachable,
        CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync(
            "PrinterStatusChanged",
            new { printerId, status = status.ToString(), isReachable },
            cancellationToken);

    public Task PrinterTelemetryUpdatedAsync(
        Guid organizationId,
        Guid printerId,
        PrinterTelemetrySnapshot telemetry,
        CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync(
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
                    nozzleTempC = telemetry.NozzleTempC,
                    bedTempC = telemetry.BedTempC,
                    printProgressPercent = telemetry.PrintProgressPercent,
                    currentStageLabel = telemetry.CurrentStageLabel,
                    remainingTimeMinutes = telemetry.RemainingTimeMinutes,
                    currentJobName = telemetry.CurrentJobName,
                },
                amsUnits = telemetry.AmsUnits,
                externalSpoolTray = telemetry.ExternalSpoolTray,
            },
            cancellationToken);

    public async Task CameraFrameReceivedAsync(
        Guid organizationId,
        Guid printerId,
        string frameJpegBase64,
        CancellationToken cancellationToken)
    {
        var receivedAtUtc = DateTimeOffset.UtcNow;
        await snapshots.SaveAsync(organizationId, printerId, new(frameJpegBase64, receivedAtUtc), cancellationToken);
        await Group(organizationId).SendAsync(
            "CameraFrameReceived",
            new { printerId, frameJpegBase64, receivedAtUtc },
            cancellationToken);
    }

    private IClientProxy Group(Guid organizationId) =>
        hubContext.Clients.Group(FarmHub.GroupName(organizationId.ToString()));
}
