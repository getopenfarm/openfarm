using OpenFarm.Application.Printers;
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
    Task ProductionRunStatusChangedAsync(Guid productionRunId, ProductionRunStatus status, CancellationToken cancellationToken);

    Task PrintJobStatusChangedAsync(Guid printJobId, PrintJobStatus status, CancellationToken cancellationToken);

    /// <summary>Dispatch assignment (ActorType.System) — fired the moment the dispatcher sends a job to a printer, distinct from the status change itself.</summary>
    Task PrintJobDispatchedAsync(Guid printJobId, Guid printerId, CancellationToken cancellationToken);

    /// <summary>The action-required list may have changed; clients re-read it. A default no-op keeps existing implementations compiling.</summary>
    Task NotificationsChangedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// How far sending <paramref name="printJobId"/> to its printer is, while it is being sent. <paramref name="attempt"/> counts from 1;
    /// <paramref name="failure"/> says why the previous attempt failed when <paramref name="stage"/> is <see cref="PrintSendStage.Retrying"/>.
    /// </summary>
    Task PrintJobSendProgressAsync(Guid printJobId, PrintSendStage stage, int? percent, int attempt, string? failure, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
