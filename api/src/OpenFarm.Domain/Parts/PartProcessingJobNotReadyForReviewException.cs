namespace OpenFarm.Domain.Parts;

public sealed class PartProcessingJobNotReadyForReviewException(Guid jobId)
    : Exception($"Part processing job '{jobId}' is not currently ready for review.")
{
    public Guid JobId { get; } = jobId;
}
