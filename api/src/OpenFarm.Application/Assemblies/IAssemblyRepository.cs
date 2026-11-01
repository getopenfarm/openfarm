using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public interface IAssemblyRepository
{
    Task<(IReadOnlyList<Assembly> Assemblies, int TotalCount)> ListInFolderAsync(int page, int pageSize, OpenFarm.Application.Common.ListSort? sorting, string? folder, CancellationToken cancellationToken) =>
        folder is null ? ListAsync(page, pageSize, cancellationToken, sorting) : throw new NotSupportedException("Folder queries require a persistent repository.");

    Task AddAsync(Assembly assembly, CancellationToken cancellationToken);

    Task<Assembly?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Assembly> Assemblies, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null);

    /// <summary>FR-011 — names of every assembly whose BOM has a line targeting <paramref name="targetKind"/>/<paramref name="targetId"/>, for a delete-blocking error message.</summary>
    Task<IReadOnlyList<string>> GetReferencingAssemblyNamesAsync(BomLineTargetKind targetKind, Guid targetId, CancellationToken cancellationToken);

    Task RemoveAsync(Assembly assembly, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stages a <see cref="BomLine"/> just added to an already-tracked <see cref="Assembly"/>
    /// (<see cref="Assembly.ReplaceLines"/>) directly on its own <c>DbSet</c>, rather than
    /// relying on change-tracker fixup discovery through the <see cref="Assembly.Lines"/> navigation —
    /// a line with a client-assigned key would be discovered as Modified, not Added.
    /// </summary>
    /// <remarks>Does not save — the caller's own SaveChanges commits it.</remarks>
    void StageLine(BomLine line);

    void StageCostLine(AssemblyCostLine line);

    /// <summary>
    /// research.md R3 — opens a DB transaction, takes the Postgres advisory lock,
    /// runs <paramref name="operation"/>, then commits. Used only by structural writes that add or
    /// change an Assembly-kind BOM line, so <see cref="AssemblyGraph"/> validation and the write it
    /// guards happen atomically against concurrent structural saves elsewhere.
    /// </summary>
    Task<TResult> RunWithStructuralLockAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken);
}
