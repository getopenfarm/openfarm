using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeFilamentProfileRepository : IFilamentProfileRepository
{
    private readonly List<FilamentProfile> _rows = [];

    public IReadOnlyList<FilamentProfile> Rows => _rows;

    public Task AddAsync(FilamentProfile profile, CancellationToken cancellationToken)
    {
        _rows.Add(profile);
        return Task.CompletedTask;
    }

    public Task<FilamentProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FilamentProfile>>(_rows.ToList());

    public Task<IReadOnlyList<FilamentProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FilamentProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
