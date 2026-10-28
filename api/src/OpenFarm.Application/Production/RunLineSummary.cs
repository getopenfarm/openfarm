using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <param name="CumulativeProducedUnits">
/// FR-006e — meaningful only when <paramref name="Quantity"/> is null (an unlimited/backfill line):
/// <c>SUM(PlateQuantity)</c> over the line's own <c>Completed</c> jobs. Always a cumulative count, never a
/// completion percentage — an unlimited line has no denominator to compute one against.
/// </param>
/// <param name="SpansMultipleRevisions">
/// FR-009e — a computed read-side field, never stored/persisted: <c>true</c> iff this line's own jobs
/// (across every one of its PartRequirements) carry more than one distinct <c>FileRevisionHash</c>,
/// which can only happen under Follow-latest when the Part was revised mid-run.
/// </param>
public sealed record RunLineSummary(
    Guid Id,
    RunLineTargetKind TargetKind,
    Guid TargetId,
    string TargetName,
    int? Quantity,
    RevisionMode RevisionMode,
    Guid? PinnedPrinterId,
    int BackfillPrinterCap,
    int? BackfillCumulativeUnitCeiling,
    int CumulativeProducedUnits,
    bool SpansMultipleRevisions,
    IReadOnlyList<PartRequirementSummary> Requirements);
