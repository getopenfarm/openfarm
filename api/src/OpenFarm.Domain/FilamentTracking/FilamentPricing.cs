namespace OpenFarm.Domain.FilamentTracking;

/// <summary>Where a price per kilogram came from, most specific first.</summary>
public enum FilamentPriceSource
{
    Spool,
    Variant,
    Filament,
}

public readonly record struct ResolvedFilamentPrice(decimal PricePerKg, FilamentPriceSource Source);

/// <summary>
/// The price per kilogram a print's filament is costed at: what was paid for the spool that fed it, else its colour's price,
/// else its filament's. Zero counts as a price at every level, so only an unset (null) price falls through to the next one.
/// </summary>
public static class FilamentPricing
{
    public static ResolvedFilamentPrice? Resolve(decimal? spoolPrice, decimal spoolWeightGrams, decimal? variantPricePerKg, decimal? filamentPricePerKg) =>
        Spool.PricePerKgOf(spoolPrice, spoolWeightGrams) is { } fromSpool ? new ResolvedFilamentPrice(fromSpool, FilamentPriceSource.Spool)
        : variantPricePerKg is { } fromVariant ? new ResolvedFilamentPrice(fromVariant, FilamentPriceSource.Variant)
        : filamentPricePerKg is { } fromFilament ? new ResolvedFilamentPrice(fromFilament, FilamentPriceSource.Filament)
        : null;

    public static ResolvedFilamentPrice? Resolve(Spool spool, FilamentVariant? variant, Filament? filament) =>
        Resolve(spool.Price, spool.SpoolWeightGrams, variant?.PricePerKg, filament?.PricePerKg);
}
