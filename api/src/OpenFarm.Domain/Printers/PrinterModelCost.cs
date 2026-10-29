namespace OpenFarm.Domain.Printers;

/// <summary>Retained cost assumptions for a model, independent of the fleet's physical printers.</summary>
public sealed class PrinterModelCost
{
    private PrinterModelCost() { }
    public string Brand { get; private set; } = string.Empty;
    public string ModelCode { get; private set; } = string.Empty;
    public decimal? PrinterPurchasePrice { get; private set; }
    public decimal? PrinterUpfrontCost { get; private set; }
    public decimal? LifetimeServiceCost { get; private set; }
    public decimal? LifetimePrintingHours { get; private set; }
    public decimal? AveragePrintingWatts { get; private set; }
    public PrinterCostRates Rates => new(PrinterPurchasePrice, PrinterUpfrontCost,
        LifetimeServiceCost, LifetimePrintingHours, AveragePrintingWatts);

    public static PrinterModelCost Create(string brand, string modelCode)
    {
        var model = PrinterModelCatalog.Resolve(brand, modelCode);
        return new() { Brand = model.Brand.ToString(), ModelCode = model.ModelCode };
    }

    public void Update(PrinterCostRates rates)
    {
        PrinterPurchasePrice = rates.PrinterPurchasePrice;
        PrinterUpfrontCost = rates.PrinterUpfrontCost;
        LifetimeServiceCost = rates.LifetimeServiceCost;
        LifetimePrintingHours = rates.LifetimePrintingHours;
        AveragePrintingWatts = rates.AveragePrintingWatts;
    }
}
