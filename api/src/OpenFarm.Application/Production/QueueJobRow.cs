using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// The flat, dispatch-order queue view (FR-008, the "by dispatch order" lens) — powers the
/// virtualized (TanStack Virtual, constitution Principle V) queue list. One page-able,
/// <c>ORDER BY QueuePosition</c> query, no tree assembly needed for this lens.
/// </summary>
/// <param name="IsBlocked">Pinned + target printer unavailable (FR-007c) — always false until US2's Pin exists.</param>
public sealed record QueueJobRow(
    Guid JobId,
    double QueuePosition,
    PrintJobStatus Status,
    AssignmentMode AssignmentMode,
    Guid? PinnedPrinterId,
    Guid? DispatchedPrinterId,
    bool IsBlocked,
    Guid PartId,
    string PartName,
    int PlateQuantity,
    Guid RunLineId,
    Guid ProductionRunId,
    string RunDisplayLabel, string? BlockingReason = null, bool SourcePartAvailable = true, string SnapshotState = "Ready", string? SnapshotRecoveryReason = null);
