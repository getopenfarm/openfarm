using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeProcessProfileRepository : IProcessProfileRepository
{
    private readonly List<ProcessProfile> _rows = [];

    public IReadOnlyList<ProcessProfile> Rows => _rows;

    public Task AddAsync(ProcessProfile profile, CancellationToken cancellationToken)
    {
        _rows.Add(profile);
        return Task.CompletedTask;
    }

    public Task<ProcessProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<ProcessProfile>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessProfile>>(_rows.ToList());

    public Task<IReadOnlyList<ProcessProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

    public Task<ProcessProfile?> FindByExternalIdAsync(string vendorName, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p =>
            p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == externalId));

    public Task<ProcessProfile?> FindSystemByNameAsync(string vendorName, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p =>
            p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
