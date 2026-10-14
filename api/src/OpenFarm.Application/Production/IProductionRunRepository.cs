using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IProductionRunRepository
{
    Task AddAsync(ProductionRun run, CancellationToken cancellationToken);

    Task<ProductionRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ProductionRun> Runs, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null);

    /// <summary>The run owning a given RunLine — used by the job-completion path to re-check FR-016a's completion condition.</summary>
    Task<ProductionRun?> GetByRunLineIdAsync(Guid runLineId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
