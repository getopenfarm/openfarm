using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public interface INonPrintedComponentRepository
{
    Task AddAsync(NonPrintedComponent component, CancellationToken cancellationToken);

    Task<NonPrintedComponent?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<NonPrintedComponent> Components, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task RemoveAsync(NonPrintedComponent component, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
