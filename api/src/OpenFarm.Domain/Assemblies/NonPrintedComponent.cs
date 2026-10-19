namespace OpenFarm.Domain.Assemblies;

/// <summary>
/// Reusable physical material or service rate. The legacy entity/table identity preserves BOM
/// references; package prices normalize into UnitCost in the configured consumption unit.
/// No stock or purchasing state is tracked.
/// </summary>
public sealed class NonPrintedComponent
{
    private NonPrintedComponent(Guid id, string name, decimal? unitCost, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        UnitCost = unitCost;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public decimal? UnitCost { get; private set; }

    public string ItemType { get; private set; } = "Physical";
    public string Category { get; private set; } = "Hardware";
    public string Unit { get; private set; } = "Piece";
    public string PricingMode { get; private set; } = "Direct";
    public string? PackageLabel { get; private set; }
    public decimal? PackagePrice { get; private set; }
    public decimal? PackageQuantity { get; private set; }
    public decimal? Length { get; private set; }
    public string LengthUnit { get; private set; } = "m";
    public decimal? Width { get; private set; }
    public string WidthUnit { get; private set; } = "m";

    public CatalogPricing Pricing => new(ItemType, Category, Unit, PricingMode,
        PricingMode == "Direct" ? UnitCost : null, PackageLabel, PackagePrice, PackageQuantity,
        Length, LengthUnit, Width, WidthUnit);

    public bool IsLegacyCompatible => ItemType == "Physical" && Unit == "Piece" && PricingMode == "Direct";

    public void Configure(CatalogPricing pricing)
    {
        var rate = pricing.EffectiveRate();
        ItemType = pricing.ItemType;
        Category = pricing.Category;
        Unit = pricing.Unit;
        PricingMode = pricing.PricingMode;
        UnitCost = rate;
        PackageLabel = pricing.PricingMode == "Package" ? pricing.PackageLabel : null;
        PackagePrice = pricing.PricingMode == "Package" ? pricing.PackagePrice : null;
        PackageQuantity = pricing.PricingMode == "Package" ? pricing.NormalizedPackageQuantity() : null;
        Length = pricing.PricingMode == "Package" ? pricing.Length : null;
        Width = pricing.PricingMode == "Package" ? pricing.Width : null;
        LengthUnit = pricing.LengthUnit;
        WidthUnit = pricing.WidthUnit;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ValidateUsage(decimal quantity, bool bom = false)
    {
        if (quantity <= 0 || (Unit == "Piece" && quantity != decimal.Truncate(quantity)) || (bom && ItemType != "Physical"))
        {
            throw new ArgumentException("Usage must be positive; pieces must be whole and services cannot be BOM components.");
        }
    }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static NonPrintedComponent Create(string name, decimal? unitCost)
    {
        Validate(name);
        if (unitCost < 0) throw new ArgumentException("Price cannot be negative.", nameof(unitCost));
        return new NonPrintedComponent(Guid.NewGuid(), name, unitCost, DateTimeOffset.UtcNow);
    }

    public void Edit(string name, decimal? unitCost)
    {
        Validate(name);
        if (unitCost < 0) throw new ArgumentException("Price cannot be negative.", nameof(unitCost));

        Name = name;
        UnitCost = unitCost;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }
    }
}
