using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

#pragma warning disable CA1304, CA1311 // SQL LOWER uses the database collation; ToLowerInvariant is not SQL-translatable.

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

    public async Task<(IReadOnlyList<ProductionRun> Runs, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null)
    {
        var query = dbContext.ProductionRuns.Include(r => r.Lines).AsQueryable();

        var totalCount = await query.CountAsync(cancellationToken);
        if (sorting is not null)
        {
            // Raw read projection: every source and join is explicitly scoped to this tenant.
            // Do not materialize every run or compute job summaries before selecting the page.
            var organizationId = Guid.Parse(dbContext.TenantInfo?.Id
                ?? throw new InvalidOperationException("A tenant is required to list production runs."));
            var labels = dbContext.Database.SqlQuery<RunListLabel>($"""
                SELECT r."Id", COALESCE(r."Name",
                    CASE WHEN lines.count = 0 THEN 'Empty run'
                         WHEN lines.count = 1 THEN lines.names || COALESCE(' ×' || lines.quantity::text, '')
                         ELSE lines.count::text || ' lines — ' || lines.names END) AS "DisplayLabel"
                FROM "ProductionRuns" r
                LEFT JOIN LATERAL (
                    SELECT count(*) AS count,
                        string_agg(COALESCE(l."TargetName",
                            CASE WHEN l."TargetKind" = 'Part' THEN COALESCE(p."Name", 'Unknown part')
                                 ELSE COALESCE(a."Name", 'Unknown assembly') END), ', ' ORDER BY l."Id") AS names,
                        max(l."Quantity") AS quantity
                    FROM "RunLines" l
                    LEFT JOIN "Parts" p ON l."TargetKind" = 'Part' AND p."Id" = l."TargetId"
                        AND p."OrganizationId" = {organizationId}
                    LEFT JOIN "Assemblies" a ON l."TargetKind" = 'Assembly' AND a."Id" = l."TargetId"
                        AND a."OrganizationId" = {organizationId}
                    WHERE l."ProductionRunId" = r."Id" AND l."OrganizationId" = {organizationId}
                ) lines ON true
                WHERE r."OrganizationId" = {organizationId}
                """);
            var ordered = sorting.Descending ? labels.OrderByDescending(r => r.DisplayLabel.ToLower())
                : labels.OrderBy(r => r.DisplayLabel.ToLower());
            var ids = await ordered.ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => r.Id).ToListAsync(cancellationToken);
            var selected = await query.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);
            var byId = selected.ToDictionary(r => r.Id);
            return (ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList(), totalCount);
        }
        var runs = await query.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return (runs, totalCount);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var sourceIds = dbContext.ChangeTracker.Entries<PartRequirement>()
            .Where(e => e.State == EntityState.Added).Select(e => e.Entity.PartId).Distinct().Order().ToList();
        foreach (var id in sourceIds)
        {
            var source = await dbContext.Parts.FromSqlInterpolated(
                $"SELECT *, xmin FROM \"Parts\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            if (source is null) throw new PartNotFoundException(id);
        }
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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
