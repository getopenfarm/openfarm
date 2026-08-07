namespace OpenFarm.Domain.Production;

/// <summary>
/// Pure, injected-nothing selection logic for the dispatcher (research.md R4/R11) — isolated from
/// <c>ProductionDispatchBackgroundService</c>'s I/O shell specifically so it's unit-testable
/// (constitution Principle II). This US1 slice covers only Unassigned-only matching and topmost-
/// <see cref="DispatchCandidate.QueuePosition"/> selection; the least-capable tie-break (FR-007b) and
/// the Pinned-job branch (FR-007c) are added in US2, the backfill branch (FR-006b) in US3.
/// </summary>
public static class ProductionDispatchMatcher
{
    /// <summary>
    /// Returns the id of the topmost-queued candidate whose required nozzle diameter matches the
    /// printer's exactly and whose entire filament requirement is satisfiable by what's loaded, or
    /// <see langword="null"/> if none qualify. A <c>null</c> <c>MaterialType</c>/<c>ColorHex</c> on a
    /// requirement is a wildcard (Phase 4's "any material"/unconstrained color) and matches anything
    /// loaded.
    /// </summary>
    public static Guid? SelectNextJob(
        decimal printerNozzleDiameterMm,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> printerLoadedMaterials,
        IReadOnlyList<DispatchCandidate> candidates)
    {
        DispatchCandidate? best = null;

        foreach (var candidate in candidates)
        {
            if (!Matches(printerNozzleDiameterMm, printerLoadedMaterials, candidate))
            {
                continue;
            }

            if (best is null || candidate.QueuePosition < best.QueuePosition)
            {
                best = candidate;
            }
        }

        return best?.JobId;
    }

    private static bool Matches(
        decimal printerNozzleDiameterMm,
        IReadOnlyList<(string? MaterialType, string? ColorHex)> printerLoadedMaterials,
        DispatchCandidate candidate)
    {
        if (candidate.RequiredNozzleDiameterMm != printerNozzleDiameterMm)
        {
            return false;
        }

        foreach (var required in candidate.FilamentRequirement)
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
