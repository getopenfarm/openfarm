using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class PrinterRepository(OpenFarmDbContext dbContext) : IPrinterRepository
{
    public async Task AddAsync(Printer printer, CancellationToken cancellationToken)
    {
        dbContext.Printers.Add(printer);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Printers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) =>
        dbContext.Printers.FirstOrDefaultAsync(p => p.SerialNumber == serialNumber, cancellationToken);

    public async Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Printers
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public Task RemoveAsync(Printer printer, CancellationToken cancellationToken)
    {
        dbContext.Printers.Remove(printer);
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
