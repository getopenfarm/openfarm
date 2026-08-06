using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public interface IAssemblyRepository
{
    Task AddAsync(Assembly assembly, CancellationToken cancellationToken);

    Task<Assembly?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Assembly> Assemblies, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>FR-011 — names of every assembly whose BOM has a line targeting <paramref name="targetKind"/>/<paramref name="targetId"/>, for a delete-blocking error message.</summary>
    Task<IReadOnlyList<string>> GetReferencingAssemblyNamesAsync(BomLineTargetKind targetKind, Guid targetId, CancellationToken cancellationToken);

    Task RemoveAsync(Assembly assembly, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists a <see cref="BomLine"/> just added to an already-tracked <see cref="Assembly"/>
    /// (<see cref="Assembly.AddLine"/>) by staging it directly on its own <c>DbSet</c>, rather than
    /// relying on change-tracker fixup discovery through the <see cref="Assembly.Lines"/> navigation —
    /// the multi-tenant shadow column is only populated reliably for entities added this way.
    /// </summary>
    Task AddLineAsync(BomLine line, CancellationToken cancellationToken);

    /// <summary>
    /// research.md R3 — opens a DB transaction, takes the per-organization Postgres advisory lock,
    /// runs <paramref name="operation"/>, then commits. Used only by structural writes that add or
    /// change an Assembly-kind BOM line, so <see cref="AssemblyGraph"/> validation and the write it
    /// guards happen atomically against concurrent structural saves elsewhere in the organization.
    /// </summary>
    Task<TResult> RunWithStructuralLockAsync<TResult>(
        Guid organizationId, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken);
}
