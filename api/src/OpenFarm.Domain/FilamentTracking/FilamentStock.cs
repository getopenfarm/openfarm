namespace OpenFarm.Domain.FilamentTracking;

/// <summary>
/// What one colour has on the shelf and in printers: the grams left on its unfinished spools, how many there are, and what they
/// are worth at each spool's own price (<see cref="FilamentPricing"/>). A spool with grams left and no price at any level is
/// counted in <see cref="UnpricedSpools"/>, never valued at zero, so a value with unpriced spools is partial.
/// </summary>
/// <param name="UsedGrams">Grams prints and manual usage took off its spools recently; the window is the caller's.</param>
public sealed record FilamentStock(decimal OnHandGrams, int ActiveSpools, decimal StockValue, int UnpricedSpools, decimal UsedGrams)
{
    /// <param name="spools">Each unfinished spool's remaining grams and its resolved price per kilogram, null when it has none.</param>
    public static FilamentStock Of(IEnumerable<(decimal RemainingGrams, decimal? PricePerKg)> spools, decimal usedGrams = 0)
    {
        decimal onHand = 0, value = 0;
        int count = 0, unpriced = 0;
        foreach (var (remaining, pricePerKg) in spools)
        {
            count++;
            onHand += remaining;
            if (pricePerKg is { } price)
                value += remaining * price / 1000m;
            else if (remaining > 0)
                unpriced++;
        }

        return new FilamentStock(onHand, count, value, unpriced, usedGrams);
    }

    /// <summary>Low once the grams on hand drop under the threshold. No threshold never alerts, and a threshold of 0 never fires.</summary>
    public bool IsBelow(decimal? thresholdGrams) => thresholdGrams is { } threshold && OnHandGrams < threshold;

    /// <summary>How long the grams on hand last at the pace of <see cref="UsedGrams"/> over <paramref name="windowDays"/>; null without use.</summary>
    public decimal? DaysLeft(int windowDays) => UsedGrams > 0 ? OnHandGrams * windowDays / UsedGrams : null;

    /// <summary>
    /// Low by grams (<see cref="IsBelow"/>) or by days: fewer days left than <paramref name="thresholdDays"/>. A days threshold of 0
    /// never fires, and a colour without use has no days left to compare.
    /// </summary>
    public bool IsLow(decimal? thresholdGrams, int? thresholdDays, int windowDays) =>
        IsBelow(thresholdGrams) || (thresholdDays is > 0 and var days && DaysLeft(windowDays) is { } left && left < days);

    /// <summary>
    /// The colour's own threshold, else its filament's default. The default covers only colours stocked at least once, so a
    /// colour the farm never kept does not show as low.
    /// </summary>
    public static decimal? Threshold(decimal? variantGrams, decimal? filamentGrams, bool everStocked) =>
        variantGrams ?? (everStocked ? filamentGrams : null);

    /// <summary>The same rule for a threshold in days left.</summary>
    public static int? Threshold(int? variantDays, int? filamentDays, bool everStocked) =>
        variantDays ?? (everStocked ? filamentDays : null);
}
