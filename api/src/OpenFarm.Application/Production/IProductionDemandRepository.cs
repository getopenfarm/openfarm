using OpenFarm.Domain.Production;
namespace OpenFarm.Application.Production;

public interface IProductionDemandRepository
{
    Task<IAsyncDisposable> LockAsync(Guid key, CancellationToken ct, bool lockRecipe = false);
    Task ValidateSourcesAsync(ProductionPlan plan, CancellationToken ct);
    Task<ProductionDemand?> GetAsync(Guid id, CancellationToken ct);
    Task<ProductionDemand?> FindSubmissionAsync(Guid submissionId, CancellationToken ct);
    Task<IReadOnlyList<ProductionDemand>> ListBackfillAsync(CancellationToken ct);
    Task<bool> CanDispatchAsync(Guid id, CancellationToken ct);
    void Add(ProductionDemand demand);
    Task FlushAsync(CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}
