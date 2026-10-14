using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public interface IMachineProfileRepository
{
    Task AddAsync(MachineProfile profile, CancellationToken cancellationToken);

    Task<MachineProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken);

    // FR-009 duplicate lookups at preview time, and base-name resolution (research.md §3) —
    // may legitimately return more than one row, since Name isn't unique (FR-009).
    Task<IReadOnlyList<MachineProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken);

    // Ingestion-only upsert lookups (research.md §1's identity key) — distinct from the paginated
    // picker SearchAsync (US2) and from FindByNameAsync above, which stays scoped to User-imported
    // base-name resolution and is keyed by SourceFormat, not VendorName.
    Task<MachineProfile?> FindByExternalIdAsync(ProfileSourceFormat sourceFormat, string vendorName, string externalId, CancellationToken cancellationToken);

    Task<MachineProfile?> FindSystemByNameAsync(ProfileSourceFormat sourceFormat, string vendorName, string name, CancellationToken cancellationToken);

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
        CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
