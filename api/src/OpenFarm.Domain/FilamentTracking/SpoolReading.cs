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

    /// <summary>How far inventory and a reading may differ, as a share of the full weight, before it is worth asking.</summary>
    public const decimal DriftShare = 0.10m; // ponytail: the AMS percent is an estimate; tune if real spools prompt too often or too late

    /// <summary>True when recorded and read grams differ by more than <see cref="DriftShare"/> of the full weight, either way.</summary>
    public static bool Drifted(decimal inventoryGrams, decimal readGrams, decimal fullGrams) =>
        Math.Abs(inventoryGrams - readGrams) > fullGrams * DriftShare;
}
