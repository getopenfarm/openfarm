using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// The flat, dispatch-order queue view (FR-008, the "by dispatch order" lens) — powers the
/// virtualized (TanStack Virtual, constitution Principle V) queue list. One page-able,
/// <c>ORDER BY QueuePosition</c> query, no tree assembly needed for this lens.
/// </summary>
/// <param name="IsBlocked">Pinned + target printer unavailable (FR-007c) — always false until US2's Pin exists.</param>
/// <param name="LastBlockReason">Why the last attempt to send this job failed after a printer matched it; <c>null</c> when none did.</param>
/// <remarks>A print started on the printer has no queue position, part, plate or run: those fields are null for it.</remarks>
public sealed record QueueJobRow(
    Guid JobId,
    double? QueuePosition,
    PrintJobStatus Status,
    AssignmentMode AssignmentMode,
    Guid? PinnedPrinterId,
    Guid? DispatchedPrinterId,
    bool IsBlocked,
    Guid? PartId,
    string PartName,
    int PlateQuantity,
    Guid? RunLineId,
    Guid? ProductionRunId,
    string RunDisplayLabel, string? BlockingReason = null, bool SourcePartAvailable = true, string SnapshotState = "Ready", string? SnapshotRecoveryReason = null,
    PrintSource Source = PrintSource.Dispatched, DateTimeOffset? StartedAtUtc = null, DateTimeOffset? EndedAtUtc = null,
    string? LastBlockReason = null, string? DemandMode = null, bool CanRequeue = false, int? TargetUnits = null, int? ProducedUnits = null, int? MissingUnits = null, string? ThumbnailBase64 = null);
