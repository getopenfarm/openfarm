using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Parts.Fakes;

public sealed class FakeOrganizationFilamentDefaultRepository : IOrganizationFilamentDefaultRepository
{
    private readonly List<OrganizationFilamentDefault> _rows = [];

    public IReadOnlyList<OrganizationFilamentDefault> Rows => _rows;

    public Task AddAsync(OrganizationFilamentDefault entry, CancellationToken cancellationToken)
    {
        _rows.Add(entry);
        return Task.CompletedTask;
    }

    public Task<OrganizationFilamentDefault?> FindByMaterialTypeAsync(string materialType, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(d => d.MaterialType == materialType));

    public Task<IReadOnlyList<OrganizationFilamentDefault>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OrganizationFilamentDefault>>(_rows.ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
