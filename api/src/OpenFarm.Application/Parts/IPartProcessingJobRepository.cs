using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public interface IPartProcessingJobRepository
{
    Task AddAsync(PartProcessingJob job, CancellationToken cancellationToken);

    Task<PartProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
