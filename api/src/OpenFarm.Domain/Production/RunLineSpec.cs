namespace OpenFarm.Domain.Production;

/// <summary>
/// Input to <see cref="ProductionRun.Create"/> — a not-yet-persisted run line. Exactly one target
/// kind is meaningful (<see cref="RunLine"/>'s private constructor is the actual enforcement
/// point, mirroring <c>BomLineSpec</c>). <see cref="Quantity"/> null means unlimited/backfill
/// (FR-006). <see cref="RevisionMode"/> null defers to <see cref="RunLine"/>'s own default
/// (Snapshot for finite, FollowLatest for unlimited — FR-009a).
/// </summary>
public sealed record RunLineSpec(
    RunLineTargetKind TargetKind,
    Guid TargetId,
    int? Quantity,
    RevisionMode? RevisionMode,
    AssignmentMode DefaultAssignmentMode,
    Guid? DefaultPinnedPrinterId,
    int BackfillPrinterCap,
    int? BackfillCumulativeUnitCeiling);
