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
    Task<PrintJob?> GetLastJobByDispatchedPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) => Task.FromResult<PrintJob?>(null);

    Task<PrintJob?> GetActiveJobByDispatchedPrinterIdAsync(Guid printerId, CancellationToken cancellationToken);

    /// <summary>Constitution Durable Recovery guardrail — jobs left dispatch-in-flight (DispatchedAtUtc set, still Queued) from a prior process lifetime.</summary>
    Task<IReadOnlyList<PrintJob>> ListDispatchInFlightAsync(CancellationToken cancellationToken);

    /// <summary>Queued, printing and paused jobs of finite lines, with their filament lines: the filament work still to come.</summary>
    Task<IReadOnlyList<PrintJob>> ListUnfinishedFiniteAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PrintJob>>([]);

    /// <summary>research.md R3 — new/requeued jobs are pre-spaced past the current top of a queue scope; null if the scope has no jobs yet.</summary>
    Task<double?> GetMaxQueuePositionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The flat, `QueuePosition`-ordered "dispatch order" lens (FR-008) — <paramref name="backfillOnly"/>
    /// selects jobs belonging to an unlimited requirement (US3's lazily-created tier), the default
    /// selects jobs belonging to a finite one, matching `view=queue`/`view=backfill`.
    /// </summary>
    Task<(IReadOnlyList<PrintJob> Jobs, int TotalCount, IReadOnlyDictionary<string, int> StatusCounts)> ListQueueAsync(
        bool backfillOnly, int page, int pageSize, CancellationToken cancellationToken, string? status = null, string? search = null);

    /// <summary>research.md R3 — the Queued job with the smallest QueuePosition greater than <paramref name="afterQueuePosition"/>, excluding <paramref name="excludeJobId"/> (the job being moved), or null if none.</summary>
    Task<PrintJob?> GetNextQueuedJobAsync(double afterQueuePosition, Guid excludeJobId, CancellationToken cancellationToken);

    /// <summary>The smallest QueuePosition among Queued jobs, or null if none — reorder's "move to very top" case (research.md R3).</summary>
    Task<double?> GetMinQueuedPositionAsync(CancellationToken cancellationToken);

    void TrackNewFilamentConsumption(IReadOnlyList<PrintFilament> lines) { }

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
