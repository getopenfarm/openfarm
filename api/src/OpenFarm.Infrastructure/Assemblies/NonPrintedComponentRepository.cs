using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Infrastructure.Persistence;

#pragma warning disable CA1304, CA1311 // SQL LOWER uses the database collation; ToLowerInvariant is not SQL-translatable.

namespace OpenFarm.Infrastructure.Assemblies;

public sealed class NonPrintedComponentRepository(OpenFarmDbContext dbContext) : INonPrintedComponentRepository
{
    public async Task AddAsync(NonPrintedComponent component, CancellationToken cancellationToken)
    {
        dbContext.NonPrintedComponents.Add(component);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<NonPrintedComponent?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.NonPrintedComponents.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.NonPrintedComponents.AnyAsync(c => c.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<NonPrintedComponent> Components, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null)
    {
        var query = dbContext.NonPrintedComponents.Where(c => c.ItemType == "Physical" && c.Unit == "Piece" && c.PricingMode == "Direct");

        var totalCount = await query.CountAsync(cancellationToken);
        var ordered = sorting is null ? query.OrderByDescending(c => c.CreatedAtUtc)
            : sorting.Descending ? query.OrderByDescending(c => c.Name.ToLower())
            : query.OrderBy(c => c.Name.ToLower());
        var components = await ordered.ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (components, totalCount);
    }

    public async Task<(IReadOnlyList<NonPrintedComponent> Components, int TotalCount)> ListCatalogAsync(
        int page, int pageSize, string? itemType, string? category, OpenFarm.Application.Common.ListSort? sorting, CancellationToken cancellationToken)
    {
        var query = dbContext.NonPrintedComponents.AsQueryable();
        if (!string.IsNullOrEmpty(itemType)) query = query.Where(c => c.ItemType == itemType);
        if (!string.IsNullOrEmpty(category)) query = query.Where(c => c.Category == category);
        var total = await query.CountAsync(cancellationToken);
        var ordered = sorting is null ? query.OrderByDescending(c => c.CreatedAtUtc)
            : sorting.Descending ? query.OrderByDescending(c => c.Name.ToLower()) : query.OrderBy(c => c.Name.ToLower());
        return (await ordered.ThenBy(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken), total);
    }

    public Task RemoveAsync(NonPrintedComponent component, CancellationToken cancellationToken)
    {
        dbContext.NonPrintedComponents.Remove(component);
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
            var componentId = ex.Entries.Select(e => e.Entity).OfType<NonPrintedComponent>().FirstOrDefault()?.Id ?? Guid.Empty;
            throw new NonPrintedComponentConcurrencyConflictException(componentId);
        }
    }
}
