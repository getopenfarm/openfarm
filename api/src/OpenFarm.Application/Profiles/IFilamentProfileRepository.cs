using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public interface IFilamentProfileRepository
{
    Task AddAsync(FilamentProfile profile, CancellationToken cancellationToken);

    Task<FilamentProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // `scope == null` returns every row (ingestion's stale marking); otherwise User rows plus the scope's System rows.
    Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken, ProfileCatalogScope? scope = null);

    // Base-name and template resolution (research.md §3): User rows plus the System rows of ONE catalog version
    // (a `null` version matches User rows only). May legitimately return more than one row — Name isn't unique (FR-009).
    Task<IReadOnlyList<FilamentProfile>> FindByNameInCatalogAsync(
        ProfileSourceFormat sourceFormat, string? sourceVersion, string name, CancellationToken cancellationToken);

    // FR-009 duplicate lookups at preview time — every origin and every catalog version.
    Task<IReadOnlyList<FilamentProfile>> FindByNameAnyVersionAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken);

    // Version mapping: the candidate System rows of ONE catalog version for a preset's identity — its setting_id
    // (when it has one) or its vendor + name. ProfileVersionMapper picks the best candidate.
    Task<IReadOnlyList<FilamentProfile>> FindInCatalogByIdentityAsync(
        ProfileSourceFormat sourceFormat, string sourceVersion, string? externalId, string? vendorName, string name, CancellationToken cancellationToken);

    // Ingestion only: every System row of ONE catalog, TRACKED,
    // so a leaf is upserted in memory and committed by a single SaveChangesAsync.
    Task<IReadOnlyList<FilamentProfile>> ListSystemCatalogAsync(ProfileSourceFormat sourceFormat, string? sourceVersion, CancellationToken cancellationToken);

    // Tracks a new row WITHOUT saving (AddAsync saves at once) — the next SaveChangesAsync persists it.
    void Stage(FilamentProfile profile);

    // US2 picker query (research.md §6) — paginated/searchable, unlike ListAsync above.
    Task<(IReadOnlyList<FilamentProfile> Items, int TotalCount)> SearchAsync(
        ProfileOrigin? origin,
        bool? isSelectable,
        string? compatibleWithMachineName,
        string? materialType,
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null, ProfileCatalogScope? catalog = null);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
