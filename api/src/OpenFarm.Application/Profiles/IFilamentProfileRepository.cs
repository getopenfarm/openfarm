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
    Task<FilamentProfile?> FindByExternalIdAsync(string vendorName, string externalId, CancellationToken cancellationToken);

    Task<FilamentProfile?> FindSystemByNameAsync(string vendorName, string name, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
