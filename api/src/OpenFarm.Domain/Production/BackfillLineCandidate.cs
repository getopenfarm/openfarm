namespace OpenFarm.Domain.Production;

/// <summary>
/// One backfill-eligible (RunLine, PartRequirement) pair the dispatcher is considering for a
/// specific idle printer once no finite job matched (FR-006, research.md R4 step 5) — built by
/// <c>ProductionDispatchQueryService</c> (Infrastructure), consumed by the pure
/// <see cref="ProductionDispatchMatcher"/> (Domain). <see cref="CurrentOccupancy"/>/<see cref="CumulativeUnits"/>
/// are per-<c>RunLine</c> aggregates (research.md R4's occupancy-cap/cumulative-ceiling formulas),
/// shared by every requirement belonging to the same line — a single backfill cycle for an
/// Assembly-targeted line means one lazily-created job per distinct printed part, all counted
/// together against the same cap (data-model.md).
/// </summary>
public sealed record BackfillLineCandidate(
    Guid RunLineId,
    Guid PartRequirementId,
    decimal RequiredNozzleDiameterMm,
    IReadOnlyList<(string? MaterialType, string? ColorHex)> FilamentRequirement,
    int BackfillPrinterCap,
    int? BackfillCumulativeUnitCeiling,
    int CurrentOccupancy,
    int CumulativeUnits);
