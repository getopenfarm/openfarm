namespace OpenFarm.Domain.FilamentTracking;

public sealed class FilamentVariant
{
    private FilamentVariant(Guid id, Guid filamentId, string colorName, string colorHex, DateTimeOffset createdAtUtc)
    {
        Id = id;
        FilamentId = filamentId;
        ColorName = colorName;
        ColorHex = colorHex;
        CreatedAtUtc = createdAtUtc;
    }

    public decimal? PricePerKg { get; private set; }

    public void SetPricePerKg(decimal? price)
    {
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price must be non-negative.");
        PricePerKg = price;
    }

    /// <summary>Grams on hand below which this colour shows as low; null falls back to the filament's default.</summary>
    public decimal? LowStockGrams { get; private set; }

    public void SetLowStockGrams(decimal? grams)
    {
        if (grams < 0) throw new ArgumentOutOfRangeException(nameof(grams), "Low stock threshold must be non-negative.");
        LowStockGrams = grams;
    }

    public Guid Id { get; }

    public Guid FilamentId { get; }

    public string ColorName { get; }

    public string ColorHex { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    internal static FilamentVariant Create(Guid filamentId, string colorName, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorName))
        {
            throw new ArgumentException("Color name is required.", nameof(colorName));
        }

        if (string.IsNullOrWhiteSpace(colorHex))
        {
            throw new ArgumentException("Color hex is required.", nameof(colorHex));
        }

        return new FilamentVariant(Guid.NewGuid(), filamentId, colorName, colorHex, DateTimeOffset.UtcNow);
    }
}
