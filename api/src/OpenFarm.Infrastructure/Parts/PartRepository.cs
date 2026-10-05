using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Parts;

public sealed class PartRepository(OpenFarmDbContext dbContext) : IPartRepository
{
    public async Task AddAsync(Part part, CancellationToken cancellationToken)
    {
        dbContext.Parts.Add(part);
        await SaveChangesAsync(cancellationToken);
    }

    public Task<Part?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Include(dbContext.Parts).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Part> Parts, int TotalCount)> ListAsync(
        IReadOnlyList<string> tags, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = Include(dbContext.Parts).AsQueryable();

        foreach (var tag in tags)
        {
            // AND-semantics: narrows the query once per required tag rather than a single
            // "contains all" set comparison — simple, correct, and fine at this feature's row counts.
            query = query.Where(p => p.Tags.Any(t => t.Name == tag));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var parts = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (parts, totalCount);
    }

    public async Task<IReadOnlyList<Part>> ListWithResolvingEstimateAsync(CancellationToken cancellationToken) =>
        await Include(dbContext.Parts)
            .Where(p => p.EstimateStatus == EstimateStatus.Resolving)
            .ToListAsync(cancellationToken);

    public Task RemoveAsync(Part part, CancellationToken cancellationToken)
    {
        dbContext.Parts.Remove(part);
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public void TrackReplacementChildren(Part part)
    {
        dbContext.PartFilamentRequirements.AddRange(part.FilamentRequirements);
        dbContext.PartThumbnails.AddRange(part.Thumbnails);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var part in dbContext.Parts.Local)
                foreach (var thumbnail in part.Thumbnails)
                    if (dbContext.Entry(thumbnail).State == EntityState.Detached)
                        dbContext.PartThumbnails.Add(thumbnail);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var partId = ex.Entries.Select(e => e.Entity).OfType<Part>().FirstOrDefault()?.Id ?? Guid.Empty;
            throw new PartConcurrencyConflictException(partId);
        }
    }

    private static IQueryable<Part> Include(IQueryable<Part> query) =>
        query
            .Include(p => p.FilamentRequirements)
            .Include(p => p.Thumbnails)
            .Include(p => p.Tags);
}
