using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <param name="CumulativeProducedUnits">
/// FR-006e — meaningful only when <paramref name="Quantity"/> is null (an unlimited/backfill line):
/// <c>SUM(Part.UnitsPerPrint)</c> over the line's own <c>Completed</c> jobs, the same coarse count
/// research.md R4's occupancy-cap/ceiling math already uses. Always a cumulative count, never a
/// completion percentage — an unlimited line has no denominator to compute one against.
/// </param>
public sealed record RunLineSummary(
    Guid Id,
    RunLineTargetKind TargetKind,
    Guid TargetId,
    string TargetName,
    int? Quantity,
    RevisionMode RevisionMode,
    int BackfillPrinterCap,
    int? BackfillCumulativeUnitCeiling,
    int CumulativeProducedUnits,
    IReadOnlyList<PartRequirementSummary> Requirements);
