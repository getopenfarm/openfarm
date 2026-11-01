namespace OpenFarm.Domain.Settings;

/// <summary>One fee a <see cref="FeeProfile"/> takes from a sale: <c>Percent</c> of the sale price, or a <c>Fixed</c> amount per order.</summary>
public sealed record Fee
{
    public static readonly string[] Types = ["Percent", "Fixed"];

    public string Name { get; }

    public string Type { get; }

    public decimal Amount { get; }

    public Fee(string name, string type, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Fee name is required.", nameof(name));
        if (!Types.Contains(type)) throw new ArgumentException("Fee type must be Percent or Fixed.", nameof(type));
        if (amount < 0 || (type == "Percent" && amount >= 100))
            throw new ArgumentOutOfRangeException(nameof(amount), "Fee amounts must be nonnegative; a percent fee must be below 100.");
        Name = name.Trim();
        Type = type;
        Amount = amount;
    }
}
