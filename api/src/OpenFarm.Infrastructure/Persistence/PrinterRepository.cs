using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class PrinterRepository(OpenFarmDbContext dbContext) : IPrinterRepository
{
    public async Task AddAsync(Printer printer, CancellationToken cancellationToken)
    {
        dbContext.Printers.Add(printer);
        await SaveChangesAsync(cancellationToken);
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

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var models = dbContext.ChangeTracker.Entries<Printer>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => (e.Entity.Brand, e.Entity.ModelCode)).Distinct().ToList();
        if (models.Count == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        foreach (var (brand, code) in models.OrderBy(m => m.Brand, StringComparer.Ordinal).ThenBy(m => m.ModelCode, StringComparer.Ordinal))
        {
            await PrinterModelCostLock.AcquireAsync(dbContext, brand, code, cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PrinterModelCosts" ("Brand", "ModelCode") VALUES ({brand}, {code})
                ON CONFLICT ("Brand", "ModelCode") DO NOTHING
                """, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}
