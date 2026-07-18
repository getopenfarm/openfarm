using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public interface IVendorRepository
{
    Task AddAsync(Vendor vendor, CancellationToken cancellationToken);

    Task<Vendor?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Vendor?> GetByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Vendor>> ListAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
