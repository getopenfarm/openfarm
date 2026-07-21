using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public interface IOrganizationFilamentDefaultRepository
{
    Task AddAsync(OrganizationFilamentDefault entry, CancellationToken cancellationToken);

    Task<OrganizationFilamentDefault?> FindByMaterialTypeAsync(string materialType, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationFilamentDefault>> ListAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
