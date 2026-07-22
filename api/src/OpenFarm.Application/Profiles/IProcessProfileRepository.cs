using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public interface IProcessProfileRepository
{
    Task AddAsync(ProcessProfile profile, CancellationToken cancellationToken);

    Task<ProcessProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProcessProfile>> ListAsync(CancellationToken cancellationToken);

    // FR-009 duplicate lookups at preview time, and base-name resolution (research.md §3) —
    // may legitimately return more than one row, since Name isn't unique (FR-009).
    Task<IReadOnlyList<ProcessProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken);

    // Ingestion-only upsert lookups (research.md §1's identity key) — distinct from the paginated
    // picker SearchAsync (US2). FindByNameAsync above stays scoped to base-name resolution.
    Task<ProcessProfile?> FindByExternalIdAsync(string vendorName, string externalId, CancellationToken cancellationToken);

    Task<ProcessProfile?> FindSystemByNameAsync(string vendorName, string name, CancellationToken cancellationToken);

    // US2 picker query (research.md §6) — paginated/searchable, unlike ListAsync above.
    // `materialType` has no meaning for process profiles and is ignored here; kept in the
    // signature so all three profile repositories share one shape.
    Task<(IReadOnlyList<ProcessProfile> Items, int TotalCount)> SearchAsync(
        ProfileOrigin? origin,
        bool? isSelectable,
        string? compatibleWithMachineName,
        string? materialType,
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
