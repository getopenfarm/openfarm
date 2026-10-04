namespace OpenFarm.Domain.Production;

/// <summary>
/// Pure, injected-nothing selection logic for the dispatcher (research.md R4/R11) — isolated from
/// <c>ProductionDispatchBackgroundService</c>'s I/O shell specifically so it's unit-testable
/// (constitution Principle II). US1 covers Unassigned-only matching and topmost-
/// <see cref="DispatchCandidate.QueuePosition"/> selection; US2 adds the Pinned-job branch
/// (<see cref="SelectNextJob"/>'s <c>pinnedCandidates</c> parameter, FR-007c) and the least-capable
/// tie-break among multiple qualifying idle printers (<see cref="IsPreferredAmongQualifyingPrinters"/>,
/// FR-007b — a separate pure function since it compares across printers, not jobs, and only ever
/// applies to an Unassigned match: a Pinned job is never contested by another printer); US3 adds the
/// backfill branch (FR-006b).
/// </summary>
public static class ProductionDispatchMatcher
{
    /// <summary>
    /// Returns the id of the topmost-queued Unassigned candidate whose required nozzle diameter
    /// matches the printer's exactly and whose entire filament requirement is satisfiable by what's
    /// loaded; failing that, the topmost-queued <paramref name="pinnedCandidates"/> entry (FR-007c —
    /// already scoped by the caller to this exact printer, and still subject to FR-010 validation); or <see langword="null"/> if neither yields anything. A <c>null</c>
    /// <c>MaterialType</c>/<c>ColorHex</c> on a requirement is a wildcard (Phase 4's "any
    /// material"/unconstrained color) and matches anything loaded.
    /// </summary>
    public static Guid? SelectNextJob(
        decimal printerNozzleDiameterMm,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> printerLoadedMaterials,
        IReadOnlyList<DispatchCandidate> candidates,
        IReadOnlyList<DispatchCandidate>? pinnedCandidates = null)
    {
        DispatchCandidate? best = null;
        foreach (var candidate in candidates.Concat(pinnedCandidates ?? []))
        {
            if (!Matches(printerNozzleDiameterMm, printerLoadedMaterials, candidate)) continue;
            if (best is null || candidate.QueuePosition < best.QueuePosition) best = candidate;
        }
        return best?.JobId;
    }

    /// <summary>
    /// FR-007b/research.md R11: true if no printer in <paramref name="otherQualifyingPrinters"/> is
    /// "less capable" (fewer total AMS trays, then lower <c>Guid</c> as the final deterministic
    /// tie-break) than this one — i.e., this printer is the one that should actually claim an
    /// Unassigned match it was just offered, rather than deferring to a simpler machine also
    /// currently idle and eligible for the identical job. Only ever consulted for an Unassigned
    /// match (a Pinned job has exactly one eligible printer by construction — FR-007c).
    /// </summary>
    public static bool IsPreferredAmongQualifyingPrinters(
        Guid printerId,
        int printerCapabilityScore,
        IReadOnlyList<(Guid PrinterId, int CapabilityScore)> otherQualifyingPrinters)
    {
        foreach (var other in otherQualifyingPrinters)
        {
            if (other.CapabilityScore < printerCapabilityScore)
            {
                return false;
            }

            if (other.CapabilityScore == printerCapabilityScore && other.PrinterId < printerId)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// FR-006b: only ever consulted once no finite (Unassigned or Pinned) job could be assigned to
    /// this printer. Returns the <c>PartRequirementId</c> of the first backfill-eligible candidate
    /// whose nozzle/filament requirement this printer satisfies and whose line hasn't yet met its
    /// occupancy cap or cumulative-unit ceiling (FR-006c/d) — first-match, not best-match; there is
    /// no ordering signal among backfill lines (FR-008a rules out priority-based scheduling).
    /// </summary>
    public static Guid? SelectNextBackfillLine(
        decimal printerNozzleDiameterMm,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> printerLoadedMaterials,
        IReadOnlyList<BackfillLineCandidate> backfillCandidates)
    {
        foreach (var candidate in backfillCandidates)
        {
            if (candidate.RequiredNozzleDiameterMm != printerNozzleDiameterMm)
            {
                continue;
            }

            if (!SatisfiesFilamentRequirement(printerLoadedMaterials, candidate.FilamentRequirement))
            {
                continue;
            }

            if (candidate.CurrentOccupancy >= candidate.BackfillPrinterCap)
            {
                continue;
            }

            if (candidate.BackfillCumulativeUnitCeiling is { } ceiling && candidate.CumulativeUnits >= ceiling)
            {
                continue;
            }

            return candidate.PartRequirementId;
        }

        return null;
    }

    private static bool Matches(
        decimal printerNozzleDiameterMm,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> printerLoadedMaterials,
        DispatchCandidate candidate) =>
        candidate.RequiredNozzleDiameterMm == printerNozzleDiameterMm
            && SatisfiesFilamentRequirement(printerLoadedMaterials, candidate.FilamentRequirement);

    private static bool SatisfiesFilamentRequirement(
        IReadOnlyList<(string? MaterialType, string? ColorHex)> printerLoadedMaterials,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> filamentRequirement)
    {
        foreach (var required in filamentRequirement)
        {
            var satisfiable = printerLoadedMaterials.Any(loaded =>
                (required.MaterialType is null || required.MaterialType == loaded.MaterialType) &&
                (required.ColorHex is null || required.ColorHex == loaded.ColorHex));

            if (!satisfiable)
            {
                return false;
            }
        }

        return true;
    }
}
