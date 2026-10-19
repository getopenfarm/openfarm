namespace OpenFarm.Domain.Assemblies;

public sealed record CatalogPricing(
    string ItemType = "Physical", string Category = "Hardware", string Unit = "Piece",
    string PricingMode = "Direct", decimal? DirectRate = null,
    string? PackageLabel = null, decimal? PackagePrice = null, decimal? PackageQuantity = null,
    decimal? Length = null, string LengthUnit = "m", decimal? Width = null, string WidthUnit = "m")
{
    public decimal? EffectiveRate()
    {
        Validate();
        return PricingMode == "Direct" ? DirectRate : PackagePrice / NormalizedPackageQuantity();
    }

    public decimal? NormalizedPackageQuantity() => Length is { } l && Width is { } w
        ? MaterialMeasure.Area(l, LengthUnit, w, WidthUnit) : PackageQuantity;

    public void Validate()
    {
        if (ItemType is not ("Physical" or "Service")
            || Category is not ("Hardware" or "Packaging" or "Consumables" or "Labour" or "Shipping" or "Other")
            || Unit is not ("Piece" or "Metre" or "SquareMetre" or "Gram" or "Millilitre" or "Application" or "Hour")
            || PricingMode is not ("Direct" or "Package"))
        {
            throw new ArgumentException("Invalid catalog type, category, unit, or pricing mode.");
        }

        if ((ItemType == "Physical" && Unit is "Application" or "Hour")
            || (ItemType == "Service" && (Unit is not ("Application" or "Hour") || PricingMode != "Direct")))
        {
            throw new ArgumentException("Physical items require a material unit; services require an application or hourly rate.");
        }

        if (DirectRate < 0 || PackagePrice < 0)
        {
            throw new ArgumentException("Prices cannot be negative.");
        }

        if (PricingMode == "Package")
        {
            if (Length.HasValue != Width.HasValue || ((Length.HasValue || Width.HasValue) && Unit != "SquareMetre"))
            {
                throw new ArgumentException("Both dimensions are required and only apply to square metres.");
            }

            var quantity = NormalizedPackageQuantity();
            if (quantity is null or <= 0 || (Unit == "Piece" && quantity != decimal.Truncate(quantity.Value)))
            {
                throw new ArgumentException("Package quantity must be positive, and whole for pieces.");
            }
        }
    }
}
