using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IPrintJobRepository
{
    Task AddAsync(PrintJob job, CancellationToken cancellationToken);

    Task AddRangeAsync(IReadOnlyList<PrintJob> jobs, CancellationToken cancellationToken);

    Task<PrintJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrintJob>> ListByPartRequirementIdAsync(Guid partRequirementId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrintJob>> ListByProductionRunIdAsync(Guid productionRunId, CancellationToken cancellationToken);

    /// <summary>The dispatcher's own reported-state lookup (research.md R12) — the job currently dispatched to this printer, non-terminal status.</summary>
    Task<PrintJob?> GetActiveJobByDispatchedPrinterIdAsync(Guid printerId, CancellationToken cancellationToken);

    /// <summary>Constitution Durable Recovery guardrail — jobs left dispatch-in-flight (DispatchedAtUtc set, still Queued) from a prior process lifetime.</summary>
    Task<IReadOnlyList<PrintJob>> ListDispatchInFlightAsync(CancellationToken cancellationToken);

    /// <summary>research.md R3 — new/requeued jobs are pre-spaced past the current top of a queue scope; null if the scope has no jobs yet.</summary>
    Task<double?> GetMaxQueuePositionAsync(Guid organizationId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
