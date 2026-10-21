using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public interface IMachineProfileRepository
{
    Task AddAsync(MachineProfile profile, CancellationToken cancellationToken);

    Task<MachineProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // `scope == null` returns every row (ingestion's stale marking); otherwise User rows plus the scope's System rows.
    Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken, ProfileCatalogScope? scope = null);

    // Base-name and template resolution (research.md §3): User rows plus the System rows of ONE catalog version
    // (a `null` version matches User rows only). May legitimately return more than one row — Name isn't unique (FR-009).
    Task<IReadOnlyList<MachineProfile>> FindByNameInCatalogAsync(
        ProfileSourceFormat sourceFormat, string? sourceVersion, string name, CancellationToken cancellationToken);

    // FR-009 duplicate lookups at preview time — every origin and every catalog version.
    Task<IReadOnlyList<MachineProfile>> FindByNameAnyVersionAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken);

    // Version mapping: the candidate System rows of ONE catalog version for a preset's identity — its setting_id
    // (when it has one) or its vendor + name. ProfileVersionMapper picks the best candidate.
    Task<IReadOnlyList<MachineProfile>> FindInCatalogByIdentityAsync(
        ProfileSourceFormat sourceFormat, string sourceVersion, string? externalId, string? vendorName, string name, CancellationToken cancellationToken);

    // Ingestion only: every System row of ONE catalog, TRACKED,
    // so a leaf is upserted in memory and committed by a single SaveChangesAsync.
    Task<IReadOnlyList<MachineProfile>> ListSystemCatalogAsync(ProfileSourceFormat sourceFormat, string? sourceVersion, CancellationToken cancellationToken);

    // Tracks a new row WITHOUT saving (AddAsync saves at once) — the next SaveChangesAsync persists it.
    void Stage(MachineProfile profile);

    // US2 picker query (research.md §6) — paginated/searchable, unlike ListAsync above.
    // `compatibleWithMachineName`/`materialType` have no meaning for machine profiles (a machine
    // profile IS a machine) and are ignored here; kept in the signature so all three profile
    // repositories share one shape.
    Task<(IReadOnlyList<MachineProfile> Items, int TotalCount)> SearchAsync(
        ProfileOrigin? origin,
        bool? isSelectable,
        string? compatibleWithMachineName,
        string? materialType,
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null, ProfileCatalogScope? catalog = null);

    // The distinct non-null SourceVersion values ingested for one format — ProfileCatalogVersions' presence check.
    Task<IReadOnlyList<string>> ListIngestedVersionsAsync(ProfileSourceFormat sourceFormat, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
