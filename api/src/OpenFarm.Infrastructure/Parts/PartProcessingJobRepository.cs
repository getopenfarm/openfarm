using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Parts;

public sealed class PartProcessingJobRepository(OpenFarmDbContext dbContext) : IPartProcessingJobRepository
{
    public async Task AddAsync(PartProcessingJob job, CancellationToken cancellationToken)
    {
        dbContext.PartProcessingJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PartConcurrencyConflictException(job.PartId ?? Guid.Empty);
        }
    }

    public async Task<PartProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var tracked = dbContext.PartProcessingJobs.Local.FirstOrDefault(j => j.Id == id);
        if (tracked is not null) await dbContext.Entry(tracked).ReloadAsync(cancellationToken);
        return await dbContext.PartProcessingJobs.Include(j => j.PreviewThumbnails)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<PartProcessingJob>> ListOrphanedInFlightAsync(CancellationToken cancellationToken) =>
        await dbContext.PartProcessingJobs
            .Where(j => j.Status == PartProcessingJobStatus.Processing
                || (j.Status == PartProcessingJobStatus.ReadyForReview && j.PreviewEstimateStatus == EstimateStatus.Resolving))
            .ToListAsync(cancellationToken);

    public Task<bool> HasActiveReprocessAsync(Guid partId, CancellationToken cancellationToken) =>
        dbContext.PartProcessingJobs.AnyAsync(j => j.PartId == partId && j.Kind == PartProcessingJobKind.Reprocess
            && (j.Status == PartProcessingJobStatus.Processing || j.Status == PartProcessingJobStatus.ReadyForReview), cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var job in dbContext.PartProcessingJobs.Local)
                foreach (var thumbnail in job.PreviewThumbnails)
                    if (dbContext.Entry(thumbnail).State == EntityState.Detached)
                        dbContext.PartProcessingJobPreviewThumbnails.Add(thumbnail);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new PartConcurrencyConflictException(ex.Entries.Select(e => e.Entity).OfType<PartProcessingJob>().FirstOrDefault()?.PartId ?? Guid.Empty);
        }
    }
}
