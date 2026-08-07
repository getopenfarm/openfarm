using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IProductionRunRepository
{
    Task AddAsync(ProductionRun run, CancellationToken cancellationToken);

    Task<ProductionRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ProductionRun> Runs, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
