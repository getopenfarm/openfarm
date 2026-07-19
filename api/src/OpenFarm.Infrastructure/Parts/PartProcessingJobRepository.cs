using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Parts;

public sealed class PartProcessingJobRepository(OpenFarmDbContext dbContext) : IPartProcessingJobRepository
{
    public async Task AddAsync(PartProcessingJob job, CancellationToken cancellationToken)
    {
        dbContext.PartProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<PartProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.PartProcessingJobs
            .Include(j => j.PreviewThumbnails)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
