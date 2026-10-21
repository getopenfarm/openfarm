using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Infrastructure.Persistence;

#pragma warning disable CA1304, CA1311 // SQL LOWER uses the database collation; ToLowerInvariant is not SQL-translatable.

namespace OpenFarm.Infrastructure.Assemblies;

public sealed class AssemblyRepository(OpenFarmDbContext dbContext) : IAssemblyRepository
{
    public async Task AddAsync(Assembly assembly, CancellationToken cancellationToken)
    {
        dbContext.Assemblies.Add(assembly);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Assembly?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Assemblies.Include(a => a.Lines).Include(a => a.AdditionalCosts).AsSplitQuery().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Assemblies.AnyAsync(a => a.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Assembly> Assemblies, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null)
    {
        var query = dbContext.Assemblies.Include(a => a.Lines).Include(a => a.AdditionalCosts).AsSplitQuery();

        var totalCount = await query.CountAsync(cancellationToken);
        var ordered = sorting is null ? query.OrderByDescending(a => a.CreatedAtUtc)
            : sorting.Descending ? query.OrderByDescending(a => a.Name.ToLower())
            : query.OrderBy(a => a.Name.ToLower());
        var assemblies = await ordered.ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (assemblies, totalCount);
    }

    public async Task<IReadOnlyList<string>> GetReferencingAssemblyNamesAsync(BomLineTargetKind targetKind, Guid targetId, CancellationToken cancellationToken)
    {
        var linesQuery = targetKind switch
        {
            BomLineTargetKind.Part => dbContext.BomLines.Where(l => l.TargetKind == BomLineTargetKind.Part && l.TargetPartId == targetId),
            BomLineTargetKind.Assembly => dbContext.BomLines.Where(l => l.TargetKind == BomLineTargetKind.Assembly && l.TargetAssemblyId == targetId),
            BomLineTargetKind.NonPrintedComponent => dbContext.BomLines.Where(l => l.TargetKind == BomLineTargetKind.NonPrintedComponent && l.TargetNonPrintedComponentId == targetId),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind)),
        };

        var referencingAssemblyIds = await linesQuery.Select(l => l.AssemblyId).Distinct().ToListAsync(cancellationToken);
        if (targetKind == BomLineTargetKind.NonPrintedComponent)
        {
            referencingAssemblyIds.AddRange(await dbContext.Set<AssemblyCostLine>()
                .Where(l => l.CatalogItemId == targetId).Select(l => l.AssemblyId).Distinct().ToListAsync(cancellationToken));
        }
        if (referencingAssemblyIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Assemblies
            .Where(a => referencingAssemblyIds.Contains(a.Id))
            .Select(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public Task RemoveAsync(Assembly assembly, CancellationToken cancellationToken)
    {
        dbContext.Assemblies.Remove(assembly);
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var assemblyId = ex.Entries.Select(e => e.Entity).OfType<Assembly>().FirstOrDefault()?.Id ?? Guid.Empty;
            throw new AssemblyConcurrencyConflictException(assemblyId);
        }
    }

    public void StageCostLine(AssemblyCostLine line) => dbContext.Set<AssemblyCostLine>().Add(line);

    public void StageLine(BomLine line) => dbContext.BomLines.Add(line);

    public async Task<TResult> RunWithStructuralLockAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await dbContext.AcquireStructuralLockAsync(cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var assemblyId = ex.Entries.Select(e => e.Entity).OfType<Assembly>().FirstOrDefault()?.Id ?? Guid.Empty;
            throw new AssemblyConcurrencyConflictException(assemblyId);
        }
    }
}
