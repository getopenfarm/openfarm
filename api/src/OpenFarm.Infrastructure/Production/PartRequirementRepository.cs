using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

public sealed class PartRequirementRepository(OpenFarmDbContext dbContext) : IPartRequirementRepository
{
    public Task<bool> HasPartReferenceAsync(Guid partId, CancellationToken cancellationToken) =>
        GetBlockingReferences(partId).AnyAsync(cancellationToken);

    internal IQueryable<PartRequirement> GetBlockingReferences(Guid partId) =>
        from requirement in dbContext.PartRequirements
        join line in dbContext.ProductionDemands on requirement.RunLineId equals line.Id
        join owner in dbContext.ProductionRuns on line.ProductionRunId equals owner.Id into owners
        from run in owners.DefaultIfEmpty()
        where requirement.PartId == partId && (
            line.Mode == DemandMode.Backfill && line.State != DemandState.Removed && requirement.RecipeActive
                && (run == null || run.Status == ProductionRunStatus.Active || run.Status == ProductionRunStatus.Complete)
            || dbContext.PrintJobs.Any(j => j.PartRequirementId == requirement.Id && j.ProductionSnapshotJson == null
                && j.Status != PrintJobStatus.Cancelled && j.Status != PrintJobStatus.Failed && j.Status != PrintJobStatus.Completed))
        select requirement;

    public Task AddRangeAsync(IReadOnlyList<PartRequirement> requirements, CancellationToken cancellationToken)
    {
        dbContext.PartRequirements.AddRange(requirements);
        return Task.CompletedTask;
    }

    public Task<PartRequirement?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.PartRequirements.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<IReadOnlyList<PartRequirement>> ListByRunLineIdAsync(Guid runLineId, CancellationToken cancellationToken) =>
        ToListAsync(dbContext.PartRequirements.Where(r => r.RunLineId == runLineId), cancellationToken);

    public Task<IReadOnlyList<PartRequirement>> ListByProductionRunIdAsync(Guid productionRunId, CancellationToken cancellationToken) =>
        ToListAsync(
            from requirement in dbContext.PartRequirements
            join line in dbContext.ProductionDemands on requirement.RunLineId equals line.Id
            where line.ProductionRunId == productionRunId
            select requirement,
            cancellationToken);

    private static async Task<IReadOnlyList<PartRequirement>> ToListAsync(IQueryable<PartRequirement> query, CancellationToken cancellationToken) =>
        await query.ToListAsync(cancellationToken);
}
