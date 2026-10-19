namespace OpenFarm.Domain.Assemblies;

/// <summary>Metric normalization for package dimensions and per-product cut dimensions.</summary>
public static class MaterialMeasure
{
    public static decimal Metres(decimal value, string unit) => unit switch
    {
        "mm" => value / 1000m,
        "cm" => value / 100m,
        "m" => value,
        _ => throw new ArgumentException("Dimension unit must be mm, cm, or m.", nameof(unit)),
    };

    public static decimal Area(decimal length, string lengthUnit, decimal width, string widthUnit)
    {
        if (length <= 0 || width <= 0)
        {
            throw new ArgumentException("Length and width must be positive.");
        }

        return Metres(length, lengthUnit) * Metres(width, widthUnit);
    }
}
