namespace OpenFarm.Domain.Parts;

public sealed class PartProcessingJobNotFoundException(Guid jobId)
    : Exception($"Part processing job '{jobId}' was not found.")
{
    public Guid JobId { get; } = jobId;
}
