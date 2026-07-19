using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed record RecordSpoolUsageCommand(Guid SpoolId, decimal GramsConsumed, Guid? JobId);

public sealed record AutoContinuationResult(bool Occurred, Guid? ContinuedSpoolId, Guid? ContinuedFeedSlotId, bool MaterialMismatch)
{
    public static AutoContinuationResult None { get; } = new(false, null, null, false);
}

public sealed record RecordSpoolUsageResult(Spool Spool, AutoContinuationResult AutoContinuation);
