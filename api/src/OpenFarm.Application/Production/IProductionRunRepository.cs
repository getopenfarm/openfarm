using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IProductionRunRepository
{
    Task AddAsync(ProductionRun run, CancellationToken cancellationToken);

    Task<ProductionRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Newest first unless sorted by <c>displayLabel</c> or <c>dueDate</c> (undated last). <paramref name="status"/> is
    /// "Open" (Active or Complete), "Closed" or "Cancelled"; the counts cover the search but not the status filter.
    /// </summary>
    Task<(IReadOnlyList<ProductionRun> Runs, int TotalCount, IReadOnlyDictionary<string, int> StatusCounts)> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null,
        string? status = null, string? search = null);

    /// <summary>The run owning a given RunLine — used by the job-completion path to re-check FR-016a's completion condition.</summary>
    Task<ProductionRun?> GetByRunLineIdAsync(Guid runLineId, CancellationToken cancellationToken);

    /// <summary>Run lines whose new jobs are pinned to this printer — what removing the printer must release.</summary>
    Task<IReadOnlyList<RunLine>> ListLinesPinnedToAsync(Guid printerId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RunLine>>([]);

    /// <summary>Lines added to a run that is already tracked: their client-set keys would otherwise read as existing rows.</summary>
    void TrackNewLines(IReadOnlyList<RunLine> lines) { }

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
