using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;
namespace OpenFarm.Infrastructure.Production;

public sealed class ProductionDemandRepository(OpenFarmDbContext db, IProductionRunRepository runs) : IProductionDemandRepository
{
    public async Task<IAsyncDisposable> LockAsync(Guid key, CancellationToken ct, bool lockRecipe = false)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        if (lockRecipe) await OpenFarm.Infrastructure.Assemblies.StructuralLockExtensions.AcquireStructuralLockAsync(db, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key.ToString()}, 0))", ct);
        // Also serialize with the dispatcher's short demand claim.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"RunLines\" WHERE \"Id\" = {key} FOR UPDATE", ct);
        return transaction;
    }
    public async Task ValidateSourcesAsync(ProductionPlan plan, CancellationToken ct)
    {
        foreach (var part in plan.Parts.OrderBy(p => p.PartId))
        {
            var fresh = await db.Parts.FromSqlInterpolated($"SELECT *, xmin FROM \"Parts\" WHERE \"Id\" = {part.PartId} FOR UPDATE")
                .AsNoTracking().FirstOrDefaultAsync(ct);
            if (fresh is null || fresh.UpdatedAtUtc != part.UpdatedAtUtc || fresh.RevisionHash != part.Revision)
                throw new ProductionPlanConflictException();
        }
    }
    public Task<ProductionDemand?> GetAsync(Guid id, CancellationToken ct) => db.ProductionDemands.FirstOrDefaultAsync(d => d.Id == id, ct);
    public Task<ProductionDemand?> FindSubmissionAsync(Guid submissionId, CancellationToken ct) => db.ProductionDemands.FirstOrDefaultAsync(d => d.SubmissionId == submissionId, ct);
    public async Task<IReadOnlyList<ProductionDemand>> ListBackfillAsync(CancellationToken ct) =>
        await db.ProductionDemands.Where(d => d.Mode == DemandMode.Backfill && d.State != DemandState.Removed
            && (d.ProductionRunId == null || db.ProductionRuns.Any(r => r.Id == d.ProductionRunId && (r.Status == ProductionRunStatus.Active || r.Status == ProductionRunStatus.Complete))))
            .OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.Id).ToListAsync(ct);
    public Task<bool> CanDispatchAsync(Guid id, CancellationToken ct) =>
        db.ProductionDemands.AnyAsync(d => d.Id == id && d.State == DemandState.Enabled
            && (d.ProductionRunId == null || db.ProductionRuns.Any(r => r.Id == d.ProductionRunId
                && (r.Status == ProductionRunStatus.Active || r.Status == ProductionRunStatus.Complete))), ct);
    public void Add(ProductionDemand demand) => db.ProductionDemands.Add(demand);
    public async Task FlushAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
    public async Task SaveAsync(CancellationToken ct)
    {
        await runs.SaveChangesAsync(ct);
        if (db.Database.CurrentTransaction is { } transaction) await transaction.CommitAsync(ct);
    }
}
