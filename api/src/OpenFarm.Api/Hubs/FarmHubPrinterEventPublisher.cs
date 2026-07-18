using Microsoft.AspNetCore.SignalR;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Hubs;

public sealed class FarmHubPrinterEventPublisher(IHubContext<FarmHub> hubContext) : IPrinterEventPublisher
{
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
                    nozzleTempC = telemetry.NozzleTempC,
                    bedTempC = telemetry.BedTempC,
                    printProgressPercent = telemetry.PrintProgressPercent,
                    currentStageLabel = telemetry.CurrentStageLabel,
                    remainingTimeMinutes = telemetry.RemainingTimeMinutes,
                    currentJobName = telemetry.CurrentJobName,
                },
                amsUnits = telemetry.AmsUnits,
            },
            cancellationToken);

    public Task CameraFrameReceivedAsync(
        Guid organizationId,
        Guid printerId,
        string frameJpegBase64,
        CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync(
            "CameraFrameReceived",
            new { printerId, frameJpegBase64 },
            cancellationToken);

    private IClientProxy Group(Guid organizationId) =>
        hubContext.Clients.Group(FarmHub.GroupName(organizationId.ToString()));
}
