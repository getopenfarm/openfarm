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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
