using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public interface IFilamentProfileRepository
{
    Task AddAsync(FilamentProfile profile, CancellationToken cancellationToken);

    Task<FilamentProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken);

    // FR-009 duplicate lookups at preview time, and base-name resolution (research.md §3) —
    // may legitimately return more than one row, since Name isn't unique (FR-009).
    Task<IReadOnlyList<FilamentProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken);

    // Ingestion-only upsert lookups (research.md §1's identity key) — distinct from the paginated
    // picker SearchAsync (US2). FindByNameAsync above stays scoped to base-name resolution.
    Task<FilamentProfile?> FindByExternalIdAsync(ProfileSourceFormat sourceFormat, string vendorName, string externalId, CancellationToken cancellationToken);

    Task<FilamentProfile?> FindSystemByNameAsync(ProfileSourceFormat sourceFormat, string vendorName, string name, CancellationToken cancellationToken);

    // US2 picker query (research.md §6) — paginated/searchable, unlike ListAsync above.
    Task<(IReadOnlyList<FilamentProfile> Items, int TotalCount)> SearchAsync(
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
