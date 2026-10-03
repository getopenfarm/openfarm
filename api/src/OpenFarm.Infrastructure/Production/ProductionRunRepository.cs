using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Implements <see cref="IProductionRunRepository"/>, mirroring <c>AssemblyRepository</c>'s shape.
/// <see cref="AddAsync"/> only stages the run (and its cascaded <see cref="RunLine"/>s) — it does
/// NOT save, since creating a run also stages <see cref="PartRequirement"/>s and <see cref="PrintJob"/>s
/// through the sibling repositories in the same command (data-model.md), all committed together by one
/// <see cref="SaveChangesAsync"/> call sharing this same scoped <see cref="OpenFarmDbContext"/>.
/// </summary>
public sealed class ProductionRunRepository(OpenFarmDbContext dbContext) : IProductionRunRepository
{
    public Task AddAsync(ProductionRun run, CancellationToken cancellationToken)
    {
        dbContext.ProductionRuns.Add(run);
        return Task.CompletedTask;
    }

    public Task<ProductionRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProductionRuns.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<ProductionRun?> GetByRunLineIdAsync(Guid runLineId, CancellationToken cancellationToken) =>
        dbContext.ProductionRuns.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Lines.Any(l => l.Id == runLineId), cancellationToken);

    public async Task<(IReadOnlyList<ProductionRun> Runs, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.ProductionRuns.Include(r => r.Lines).AsQueryable();

        var totalCount = await query.CountAsync(cancellationToken);
        var runs = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (runs, totalCount);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var runId = ex.Entries.Select(e => e.Entity).OfType<ProductionRun>().FirstOrDefault()?.Id
                ?? ex.Entries.Select(e => e.Entity).OfType<PrintJob>().FirstOrDefault()?.Id
                ?? Guid.Empty;
            throw new ProductionRunConcurrencyConflictException(runId);
        }
    }
}
