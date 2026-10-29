using Microsoft.EntityFrameworkCore;

namespace OpenFarm.Infrastructure.Persistence;

/// <summary>Serialize model-cost removal with printer registration; callers hold a transaction.</summary>
internal static class PrinterModelCostLock
{
    public static async Task AcquireAsync(OpenFarmDbContext db, string brand, string modelCode, CancellationToken ct)
    {
        var key = $"printer-model-cost:{brand}/{modelCode}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }
}
