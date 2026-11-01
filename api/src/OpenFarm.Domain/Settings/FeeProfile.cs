namespace OpenFarm.Domain.Settings;

/// <summary>Where products sell (Shopee, Direct) and the fees taken from each sale there (docs/PRICING.md).</summary>
public sealed class FeeProfile
{
    private FeeProfile() { }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Fee[] Fees { get; private set; } = [];

    /// <summary>Sum of the percent fees, taken off the sale price.</summary>
    public decimal FeePercent => Fees.Where(f => f.Type == "Percent").Sum(f => f.Amount);

    /// <summary>Sum of the fixed fees per order.</summary>
    public decimal FixedFee => Fees.Where(f => f.Type == "Fixed").Sum(f => f.Amount);

    public static FeeProfile Create(string name, IReadOnlyList<Fee> fees, Guid? id = null)
    {
        var profile = new FeeProfile { Id = id ?? Guid.NewGuid() };
        profile.Edit(name, fees);
        return profile;
    }

    public void Edit(string name, IReadOnlyList<Fee> fees)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new ArgumentException("Fee profile name must contain 1–100 characters.", nameof(name));
        if (fees.Where(f => f.Type == "Percent").Sum(f => f.Amount) >= 100)
            throw new ArgumentOutOfRangeException(nameof(fees), "Percent fees must total below 100.");
        Name = name.Trim();
        Fees = [.. fees];
    }
}
