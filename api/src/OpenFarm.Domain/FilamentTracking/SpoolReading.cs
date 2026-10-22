namespace OpenFarm.Domain.FilamentTracking;

/// <summary>What a printer's reading of a spool says about its weight.</summary>
public static class SpoolReading
{
    /// <summary>
    /// The grams left by a remaining-percent reading of a spool of <paramref name="fullGrams"/>, to whole grams. Null when the
    /// reading is not a measurement: the AMS reports -1 or 0 until it has measured, and nothing when remaining tracking is off.
    /// </summary>
    public static decimal? Grams(int? remainPercent, decimal fullGrams) =>
        remainPercent is >= 1 and <= 100 ? decimal.Round(fullGrams * remainPercent.Value / 100m) : null;
}
