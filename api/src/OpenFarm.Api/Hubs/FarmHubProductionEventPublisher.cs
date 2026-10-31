using Microsoft.AspNetCore.SignalR;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.Hubs;

/// <summary>Mirrors <see cref="FarmHubPrinterEventPublisher"/>'s shape — pushes production-run/print-job events over the existing <c>/hubs/farm</c> hub.</summary>
public sealed class FarmHubProductionEventPublisher(IHubContext<FarmHub> hubContext) : IProductionEventPublisher
{
    public Task ProductionRunStatusChangedAsync(
        Guid productionRunId, ProductionRunStatus status, CancellationToken cancellationToken) =>
        Group.SendAsync(
            "ProductionRunStatusChanged",
            new { productionRunId, status = status.ToString() },
            cancellationToken);

    public Task PrintJobStatusChangedAsync(
        Guid printJobId, PrintJobStatus status, CancellationToken cancellationToken) =>
        Group.SendAsync(
            "PrintJobStatusChanged",
            new { printJobId, status = status.ToString() },
            cancellationToken);

    public Task PrintJobDispatchedAsync(
        Guid printJobId, Guid printerId, CancellationToken cancellationToken) =>
        Group.SendAsync(
            "PrintJobDispatched",
            new { printJobId, printerId },
            cancellationToken);

    public Task DemandChangedAsync(Guid demandId, CancellationToken cancellationToken) =>
        Group.SendAsync("ProductionDemandChanged", new { demandId }, cancellationToken);

    public Task NotificationsChangedAsync(CancellationToken cancellationToken) =>
        Group.SendAsync("NotificationsChanged", cancellationToken);

    private IClientProxy Group => hubContext.Clients.Group(FarmHub.Group);
}
