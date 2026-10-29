namespace OpenFarm.Domain.Assemblies;

/// <summary>
/// How the selling price is set: a fixed <c>Price</c>, or a <c>Margin</c>/<c>Markup</c> percent that
/// makes the price follow cost. Sales-channel fees are applied by the estimate, not stored here, so a
/// margin that fees later make unreachable is a display state, not an invalid record (docs/PRICING.md).
/// </summary>
public sealed record AssemblyPricing
{
    public static readonly string[] Modes = ["Price", "Margin", "Markup"];

    public string Mode { get; }

    public decimal Value { get; }

    public AssemblyPricing(string mode, decimal value)
    {
        if (!Modes.Contains(mode)) throw new ArgumentException("Pricing mode must be Price, Margin or Markup.", nameof(mode));
        if (value < 0 || (mode == "Margin" && value >= 100))
            throw new ArgumentOutOfRangeException(nameof(value), "Price and markup must be nonnegative; margin must be at least 0 and below 100.");
        Mode = mode;
        Value = value;
    }
}
