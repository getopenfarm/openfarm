using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeMachineProfileRepository : IMachineProfileRepository
{
    private readonly List<MachineProfile> _rows = [];

    public IReadOnlyList<MachineProfile> Rows => _rows;

    public Task AddAsync(MachineProfile profile, CancellationToken cancellationToken)
    {
        _rows.Add(profile);
        return Task.CompletedTask;
    }

    public Task<MachineProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineProfile>>(_rows.ToList());

    public Task<IReadOnlyList<MachineProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
