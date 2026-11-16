using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class PrinterCostRepository(OpenFarmDbContext db) : IPrinterCostRepository
{
    public async Task<IReadOnlySet<(string Brand, string ModelCode)>> ListFleetModelsAsync(CancellationToken ct)
    {
        var models = await db.Printers.AsNoTracking().Select(p => new { p.Brand, p.ModelCode }).Distinct().ToListAsync(ct);
        return models.Select(m => (m.Brand, m.ModelCode)).ToHashSet();
    }

    public async Task RemoveAsync(string brand, string modelCode, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await PrinterModelCostLock.AcquireAsync(db, brand, modelCode, ct);
        if (await db.Printers.AnyAsync(p => p.Brand == brand && p.ModelCode == modelCode, ct))
            throw new PrinterModelCostsInUseException(brand, modelCode);
        var existing = await db.PrinterModelCosts.FindAsync([brand, modelCode], ct);
        if (existing is not null)
        {
            db.PrinterModelCosts.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<PrinterModelCost>> ListAsync(CancellationToken ct) =>
        await db.PrinterModelCosts.AsNoTracking().OrderBy(x => x.Brand).ThenBy(x => x.ModelCode).ToListAsync(ct);

    public async Task<PrinterModelCost> UpsertAsync(string brand, string modelCode, PrinterCostRates rates, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await PrinterModelCostLock.AcquireAsync(db, brand, modelCode, ct);
        var model = await db.PrinterModelCosts.FindAsync([brand, modelCode], ct);
        if (model is null)
        {
            model = PrinterModelCost.Create(brand, modelCode);
            db.PrinterModelCosts.Add(model);
        }
        model.Update(rates);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return model;
    }
}
