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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
