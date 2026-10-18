using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Domain.Production;

#pragma warning disable CA1304, CA1311 // SQL LOWER uses the database collation; ToLowerInvariant is not SQL-translatable.

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
        IReadOnlyList<string> tags, int page, int pageSize, CancellationToken cancellationToken, OpenFarm.Application.Common.ListSort? sorting = null)
    {
        var query = Include(dbContext.Parts).AsQueryable();

        foreach (var tag in tags)
        {
            // AND-semantics: narrows the query once per required tag rather than a single
            // "contains all" set comparison — simple, correct, and fine at this feature's row counts.
            query = query.Where(p => p.Tags.Any(t => t.Name == tag));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var ordered = sorting is null ? query.OrderByDescending(p => p.CreatedAtUtc)
            : sorting.Descending ? query.OrderByDescending(p => p.Name.ToLower())
            : query.OrderBy(p => p.Name.ToLower());
        var parts = await ordered.ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (parts, totalCount);
    }

    public async Task<IReadOnlyList<Part>> ListWithResolvingEstimateAsync(CancellationToken cancellationToken) =>
        await Include(dbContext.Parts)
            .Where(p => p.EstimateStatus == EstimateStatus.Resolving)
            .ToListAsync(cancellationToken);

    public async Task RemoveAsync(Part part, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // FK inserts take key-share locks, so no new requirement can appear between this
        // locked dependency check, detachment and deletion. Creation locks in the same order.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Parts\" WHERE \"Id\" = {part.Id} FOR UPDATE", cancellationToken);
        var eligibility = (await new PartDeletionEligibilityReader(dbContext).CheckAsync([part.Id], cancellationToken))[0];
        if (eligibility.Status == "Absent") throw new PartNotFoundException(part.Id);
        if (eligibility.Status == "Blocked") throw new PartDeletionBlockedException(eligibility);
        var references = await dbContext.PartRequirements.Where(r => r.PartId == part.Id).ToListAsync(cancellationToken);
        foreach (var reference in references)
        {
            reference.CaptureName(part.Name);
            reference.DetachLibraryPart();
        }
        var requirementIds = references.Select(r => r.Id).ToList();
        var unavailableJobs = await dbContext.PrintJobs.Where(j => requirementIds.Contains(j.PartRequirementId)
            && j.ProductionSnapshotJson == null).ToListAsync(cancellationToken);
        var removedAtUtc = DateTimeOffset.UtcNow;
        foreach (var job in unavailableJobs) job.MarkSnapshotUnavailable(removedAtUtc);
        var lineIds = references.Select(r => r.RunLineId).ToList();
        var sourceLines = await dbContext.RunLines.Where(l => lineIds.Contains(l.Id)
            && l.TargetKind == RunLineTargetKind.Part && l.TargetId == part.Id).ToListAsync(cancellationToken);
        foreach (var line in sourceLines) line.CaptureTargetName(part.Name);
        dbContext.Parts.Remove(part);
        try
        {
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PartConcurrencyConflictException)
        {
            throw new PartConcurrencyConflictException(part.Id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.RestrictViolation,
            ConstraintName: "FK_BomLines_Parts_TargetPartId" or "FK_PartRequirements_Parts_LibraryPartId"
        })
        {
            // References can be added after the handler's preflight checks.
            throw new PartReferencedException(part.Id, "it is referenced by an assembly or production run. Refresh and review its dependencies.");
        }
    }

    public void TrackReplacementChildren(Part part)
    {
        // Reused thumbnail instances are already tracked; AddRange would flip them to Added.
        dbContext.PartFilamentRequirements.AddRange(part.FilamentRequirements.Where(r => dbContext.Entry(r).State == EntityState.Detached));
        dbContext.PartThumbnails.AddRange(part.Thumbnails.Where(t => dbContext.Entry(t).State == EntityState.Detached));
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
