namespace OpenFarm.Domain.Assemblies;

/// <param name="Price">Null when no pricing is set or the margin is unreachable.</param>
/// <param name="Unreachable">Margin plus the profile's percent fees reach 100%: no price can hit it.</param>
/// <param name="Margin">Fractions, not percents, as is <paramref name="Markup"/> and <paramref name="MaxDiscount"/>.</param>
/// <param name="BreakEven">Lowest price that still covers cost and fees.</param>
/// <param name="MaxDiscount">Largest discount off the price before a loss; 0 or less means none.</param>
public sealed record ProfilePricing(decimal? Price, bool Unreachable, decimal? Fees, decimal? Profit, decimal? Margin, decimal? Markup,
    decimal BreakEven, decimal? MaxDiscount);

/// <summary>
/// Prices one fee profile (dev-notes/PRICING.md). Fees come off the sale price: a percent of it plus a fixed amount per order,
/// counted once per unit. Margin is profit after fees ÷ price; markup is on cost, before fees. Computed prices round to cents.
/// </summary>
public static class FeeProfilePricing
{
    public static ProfilePricing Calculate(decimal cost, decimal feePercent, decimal fixedFee, AssemblyPricing? pricing) =>
        Calculate(cost, feePercent, fixedFee, pricing?.Mode, pricing?.Value ?? 0);

    /// <summary>Prices a draft rule too: a value <see cref="AssemblyPricing"/> would refuse, such as a 150% margin, is priced as typed.</summary>
    public static ProfilePricing Calculate(decimal cost, decimal feePercent, decimal fixedFee, string? mode, decimal value)
    {
        var rate = feePercent / 100;
        var breakEven = (cost + fixedFee) / (1 - rate);
        decimal? price = null;
        var unreachable = false;
        switch (mode)
        {
            case "Price": price = value; break;
            case "Markup": price = Cents(cost * (1 + (value / 100))); break;
            case "Margin":
                var kept = 1 - rate - (value / 100);
                if (kept > 0) price = Cents((cost + fixedFee) / kept);
                else unreachable = true;
                break;
        }
        if (price is not { } p) return new(null, unreachable, null, null, null, null, breakEven, null);
        var fees = (p * rate) + fixedFee;
        var profit = p - cost - fees;
        return new(p, unreachable, fees, profit, p > 0 ? profit / p : null, cost > 0 ? (p - cost) / cost : null, breakEven,
            p > 0 ? 1 - (breakEven / p) : null);
    }

    private static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
