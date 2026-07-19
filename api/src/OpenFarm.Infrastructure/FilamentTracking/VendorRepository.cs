using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.FilamentTracking;

public sealed class VendorRepository(OpenFarmDbContext dbContext) : IVendorRepository
{
    public async Task AddAsync(Vendor vendor, CancellationToken cancellationToken)
    {
        dbContext.Vendors.Add(vendor);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Vendor?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Vendors.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<Vendor?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
        dbContext.Vendors.FirstOrDefaultAsync(v => v.Name == name, cancellationToken);

    public async Task<IReadOnlyList<Vendor>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Vendors.AsNoTracking().OrderBy(v => v.Name).ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
