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
        await db.PrinterModelCosts.Where(x => x.Brand == brand && x.ModelCode == modelCode).ExecuteDeleteAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<PrinterModelCost>> ListAsync(CancellationToken ct) =>
        await db.PrinterModelCosts.AsNoTracking().OrderBy(x => x.Brand).ThenBy(x => x.ModelCode).ToListAsync(ct);

    public async Task<PrinterModelCost> UpsertAsync(string brand, string modelCode, PrinterCostRates rates, CancellationToken ct)
    {
        var model = PrinterModelCost.Create(brand, modelCode);
        model.Update(rates);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PrinterModelCosts" ("Brand", "ModelCode", "PrinterPurchasePrice", "PrinterUpfrontCost",
                "LifetimeServiceCost", "LifetimePrintingHours", "AveragePrintingWatts")
            VALUES ({model.Brand}, {model.ModelCode}, {rates.PrinterPurchasePrice}, {rates.PrinterUpfrontCost},
                {rates.LifetimeServiceCost}, {rates.LifetimePrintingHours}, {rates.AveragePrintingWatts})
            ON CONFLICT ("Brand", "ModelCode") DO UPDATE SET
                "PrinterPurchasePrice" = EXCLUDED."PrinterPurchasePrice", "PrinterUpfrontCost" = EXCLUDED."PrinterUpfrontCost",
                "LifetimeServiceCost" = EXCLUDED."LifetimeServiceCost", "LifetimePrintingHours" = EXCLUDED."LifetimePrintingHours",
                "AveragePrintingWatts" = EXCLUDED."AveragePrintingWatts"
            """, ct);
        return model;
    }
}
