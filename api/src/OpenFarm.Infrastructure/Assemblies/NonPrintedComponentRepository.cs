using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Infrastructure.Persistence;

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

    public async Task<(IReadOnlyList<NonPrintedComponent> Components, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.NonPrintedComponents.AsQueryable();

        var totalCount = await query.CountAsync(cancellationToken);
        var components = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (components, totalCount);
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
