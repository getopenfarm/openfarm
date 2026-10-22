namespace OpenFarm.Domain.FilamentTracking;

/// <summary>
/// How much filament a print can draw from a loaded spool before it runs out, counting what continuation would feed next
/// (<see cref="ReloadPolicy"/>), and how dispatch ranks spools of the same filament. A switch to another colour never counts.
/// </summary>
public static class FilamentCover
{
    /// <summary>A print needs its estimate plus this share: slicer estimates and recorded weights both drift, and a runout costs more than a wait.</summary>
    public const decimal SafetyMargin = 1.10m; // ponytail: one farm-wide margin; a FarmSettings field if false blocks or runouts get reported

    public static decimal Needed(decimal estimatedGrams) => estimatedGrams * SafetyMargin;

    /// <summary>
    /// The spool's own grams plus what continuation would feed next: for a multi-material unit every loaded spool of the same
    /// colour; for a reloader the loaded slots in slot order up to the first other colour, since it switches to whatever is
    /// loaded; nothing for an external spool.
    /// </summary>
    /// <param name="siblings">The other loaded spools of the same feed unit, on active slots.</param>
    public static decimal Available(ReloadPolicy policy, Guid variantId, decimal remainingGrams,
        IEnumerable<(int SlotPosition, Guid VariantId, decimal RemainingGrams)> siblings)
    {
        var next = siblings.Where(s => s.RemainingGrams > 0).OrderBy(s => s.SlotPosition);
        return remainingGrams + policy switch
        {
            ReloadPolicy.MatchRequired => next.Where(s => s.VariantId == variantId).Sum(s => s.RemainingGrams),
            ReloadPolicy.Unconditional => next.TakeWhile(s => s.VariantId == variantId).Sum(s => s.RemainingGrams),
            _ => 0m,
        };
    }

    /// <summary>
    /// Sort key among spools that could feed a print: the emptiest that holds it alone, so part-used spools get used up; then
    /// the emptiest that holds it with what continuation adds; then the fullest of those that cannot, so a shortfall names the
    /// best there is. Without an estimate every spool ranks the same and the caller's order stands.
    /// </summary>
    public static (int Tier, decimal Grams) Rank(decimal? estimatedGrams, decimal remainingGrams, decimal availableGrams) =>
        estimatedGrams is not { } estimate ? (0, 0m)
        : remainingGrams >= Needed(estimate) ? (0, remainingGrams)
        : availableGrams >= Needed(estimate) ? (1, remainingGrams)
        : (2, -availableGrams);

    /// <summary>
    /// The first line whose pool cannot cover what the print takes from it, with that pool's summed estimate and need. Lines
    /// drawing on the same pool (feed unit and colour) add up; a line without an estimate is not checked. Null when all fit.
    /// </summary>
    public static (int Line, decimal Estimated, decimal Needed, decimal Available)? FirstShortfall(
        IReadOnlyList<(Guid FeedUnitId, Guid VariantId, decimal AvailableGrams, decimal? EstimatedGrams)> lines)
    {
        var estimated = new Dictionary<(Guid, Guid), decimal>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].EstimatedGrams is not { } grams) continue;
            var pool = (lines[i].FeedUnitId, lines[i].VariantId);
            var total = estimated[pool] = estimated.GetValueOrDefault(pool) + grams;
            if (Needed(total) > lines[i].AvailableGrams) return (i, total, Needed(total), lines[i].AvailableGrams);
        }

        return null;
    }
}
