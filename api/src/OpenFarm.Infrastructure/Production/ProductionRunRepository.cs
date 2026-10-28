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

    public async Task<IReadOnlyList<RunLine>> ListLinesPinnedToAsync(Guid printerId, CancellationToken cancellationToken) =>
        await dbContext.RunLines.Where(l => l.DefaultPinnedPrinterId == printerId).ToListAsync(cancellationToken);

    public void TrackNewLines(IReadOnlyList<RunLine> lines) => dbContext.RunLines.AddRange(lines);

    public async Task<(IReadOnlyList<ProductionRun> Runs, int TotalCount, IReadOnlyDictionary<string, int> StatusCounts)> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null,
        string? status = null, string? search = null)
    {
        var query = dbContext.ProductionRuns.Include(r => r.Lines).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
            query = query.Where(r => EF.Functions.ILike(r.Name ?? "", $"%{term}%", "\\")
                || r.Lines.Any(l => EF.Functions.ILike(l.TargetName ?? "", $"%{term}%", "\\")));
        }

        var counts = await query.GroupBy(r => r.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var statusCounts = new Dictionary<string, int>
        {
            ["Open"] = counts.Where(c => c.Status is ProductionRunStatus.Active or ProductionRunStatus.Complete).Sum(c => c.Count),
            ["Closed"] = counts.Where(c => c.Status == ProductionRunStatus.Closed).Sum(c => c.Count),
            ["Cancelled"] = counts.Where(c => c.Status == ProductionRunStatus.Cancelled).Sum(c => c.Count),
        };

        query = status switch
        {
            "Open" => query.Where(r => r.Status == ProductionRunStatus.Active || r.Status == ProductionRunStatus.Complete),
            "Closed" => query.Where(r => r.Status == ProductionRunStatus.Closed),
            "Cancelled" => query.Where(r => r.Status == ProductionRunStatus.Cancelled),
            _ => query,
        };

        var totalCount = await query.CountAsync(cancellationToken);
        if (sorting?.Field == "dueDate")
        {
            var byDue = sorting.Descending
                ? query.OrderBy(r => r.DueDate == null).ThenByDescending(r => r.DueDate)
                : query.OrderBy(r => r.DueDate == null).ThenBy(r => r.DueDate);
            var dueRuns = await byDue.ThenByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            return (dueRuns, totalCount, statusCounts);
        }

        if (sorting is not null)
        {
            // Raw read projection: do not materialize every run or compute job summaries before selecting the page.
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
                    LEFT JOIN "Assemblies" a ON l."TargetKind" = 'Assembly' AND a."Id" = l."TargetId"
                    WHERE l."ProductionRunId" = r."Id"
                ) lines ON true
                """);
            var filteredIds = query.Select(r => r.Id);
            var filtered = labels.Where(r => filteredIds.Contains(r.Id));
            var ordered = sorting.Descending ? filtered.OrderByDescending(r => r.DisplayLabel.ToLower())
                : filtered.OrderBy(r => r.DisplayLabel.ToLower());
            var ids = await ordered.ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => r.Id).ToListAsync(cancellationToken);
            var selected = await query.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);
            var byId = selected.ToDictionary(r => r.Id);
            return (ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList(), totalCount, statusCounts);
        }
        var runs = await query.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return (runs, totalCount, statusCounts);
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
