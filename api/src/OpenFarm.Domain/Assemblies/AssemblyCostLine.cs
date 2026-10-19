namespace OpenFarm.Domain.Assemblies;

public sealed record AssemblyCostInput(Guid CatalogItemId, string Purpose = "Production",
    decimal Quantity = 1, string InputMode = "Quantity", decimal? Length = null,
    string LengthUnit = "cm", decimal? Width = null, string WidthUnit = "cm", int Pieces = 1)
{
    public decimal Usage(NonPrintedComponent item)
    {
        if (Purpose is not ("Production" or "FinalProduct") || InputMode is not ("Quantity" or "Dimensions"))
            throw new ArgumentException("Invalid cost purpose or input mode.");
        decimal usage;
        if (InputMode == "Dimensions")
        {
            if (item.Unit != "SquareMetre" || Length is null || Width is null || Pieces < 1)
                throw new ArgumentException("Area usage requires dimensions and a positive piece count.");
            usage = MaterialMeasure.Area(Length.Value, LengthUnit, Width.Value, WidthUnit) * Pieces;
        }
        else
        {
            usage = Quantity;
        }

        // Hourly service usage is entered as minutes per product.
        item.ValidateUsage(usage);
        return item.Unit == "Hour" ? usage / 60m : usage;
    }
}

public sealed class AssemblyCostLine
{
    private AssemblyCostLine() { }
    public Guid Id { get; private set; }
    public Guid AssemblyId { get; private set; }
    public Guid CatalogItemId { get; private set; }
    public string Purpose { get; private set; } = "Production";
    public decimal Quantity { get; private set; }
    public string InputMode { get; private set; } = "Quantity";
    public decimal? Length { get; private set; }
    public string LengthUnit { get; private set; } = "cm";
    public decimal? Width { get; private set; }
    public string WidthUnit { get; private set; } = "cm";
    public int Pieces { get; private set; } = 1;
    public AssemblyCostInput Input => new(CatalogItemId, Purpose, Quantity, InputMode, Length, LengthUnit, Width, WidthUnit, Pieces);

    public static AssemblyCostLine Create(Guid assemblyId, AssemblyCostInput input)
    {
        var line = new AssemblyCostLine { Id = Guid.NewGuid(), AssemblyId = assemblyId };
        line.Update(input);
        return line;
    }

    public void Update(AssemblyCostInput input)
    {
        CatalogItemId = input.CatalogItemId;
        Purpose = input.Purpose;
        Quantity = input.Quantity;
        InputMode = input.InputMode;
        Length = input.Length;
        LengthUnit = input.LengthUnit;
        Width = input.Width;
        WidthUnit = input.WidthUnit;
        Pieces = input.Pieces;
    }
}
