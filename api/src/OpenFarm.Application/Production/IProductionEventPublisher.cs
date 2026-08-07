using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// Application-defined contract for pushing real-time production-run/print-job events, implemented
/// in <c>OpenFarm.Api</c> over the existing <c>/hubs/farm</c> SignalR hub — mirrors
/// <c>IPrinterEventPublisher</c>'s shape (constitution Principle III: one push mechanism, not a
/// parallel one per feature).
/// </summary>
public interface IProductionEventPublisher
{
    Task ProductionRunStatusChangedAsync(Guid organizationId, Guid productionRunId, ProductionRunStatus status, CancellationToken cancellationToken);

    Task PrintJobStatusChangedAsync(Guid organizationId, Guid printJobId, PrintJobStatus status, CancellationToken cancellationToken);

    /// <summary>Dispatch assignment (ActorType.System) — fired the moment the dispatcher sends a job to a printer, distinct from the status change itself.</summary>
    Task PrintJobDispatchedAsync(Guid organizationId, Guid printJobId, Guid printerId, CancellationToken cancellationToken);
}
