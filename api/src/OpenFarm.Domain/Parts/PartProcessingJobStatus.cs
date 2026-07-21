namespace OpenFarm.Domain.Parts;

/// <summary>
/// <c>Processing</c> is the only non-terminal state, entered for every file type
/// (data-model.md/research.md §3). <c>ReadyForReview</c> can still transition to <c>Failed</c> if a
/// requires-slicing job's still-running slice subsequently fails while unconfirmed (research.md §3).
/// </summary>
public enum PartProcessingJobStatus
{
    Processing,
    ReadyForReview,
    Confirmed,
    Failed,
    Discarded,
}
