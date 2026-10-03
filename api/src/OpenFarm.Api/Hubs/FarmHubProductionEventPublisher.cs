using Microsoft.AspNetCore.SignalR;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.Hubs;

/// <summary>Mirrors <see cref="FarmHubPrinterEventPublisher"/>'s shape — pushes production-run/print-job events over the existing <c>/hubs/farm</c> hub.</summary>
public sealed class FarmHubProductionEventPublisher(IHubContext<FarmHub> hubContext) : IProductionEventPublisher
{
    public Task ProductionRunStatusChangedAsync(
        Guid organizationId, Guid productionRunId, ProductionRunStatus status, CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync(
            "ProductionRunStatusChanged",
            new { productionRunId, status = status.ToString() },
            cancellationToken);

    public Task PrintJobStatusChangedAsync(
        Guid organizationId, Guid printJobId, PrintJobStatus status, CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync(
            "PrintJobStatusChanged",
            new { printJobId, status = status.ToString() },
            cancellationToken);

    public Task PrintJobDispatchedAsync(
        Guid organizationId, Guid printJobId, Guid printerId, CancellationToken cancellationToken) =>
        Group(organizationId).SendAsync(
            "PrintJobDispatched",
            new { printJobId, printerId },
            cancellationToken);

    private IClientProxy Group(Guid organizationId) =>
        hubContext.Clients.Group(FarmHub.GroupName(organizationId.ToString()));
}
