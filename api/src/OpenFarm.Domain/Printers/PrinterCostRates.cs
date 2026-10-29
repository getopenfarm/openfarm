namespace OpenFarm.Domain.Printers;

/// <summary>Current model-level estimates, independent of any physical printer.</summary>
public sealed record PrinterCostRates
{
    public decimal? PrinterPurchasePrice { get; }
    public decimal? PrinterUpfrontCost { get; }
    public decimal? LifetimeServiceCost { get; }
    public decimal? LifetimePrintingHours { get; }
    public decimal? AveragePrintingWatts { get; }

    public PrinterCostRates(decimal? printerPurchasePrice, decimal? printerUpfrontCost,
        decimal? lifetimeServiceCost, decimal? lifetimePrintingHours, decimal? averagePrintingWatts)
    {
        if (printerPurchasePrice < 0 || printerUpfrontCost < 0 || lifetimeServiceCost < 0
            || averagePrintingWatts < 0 || lifetimePrintingHours <= 0)
            throw new ArgumentException("Costs and power must be nonnegative and lifetime printing hours must be positive.");
        PrinterPurchasePrice = printerPurchasePrice;
        PrinterUpfrontCost = printerUpfrontCost;
        LifetimeServiceCost = lifetimeServiceCost;
        LifetimePrintingHours = lifetimePrintingHours;
        AveragePrintingWatts = averagePrintingWatts;
    }

    public decimal? DepreciationPerHour =>
        (PrinterPurchasePrice + PrinterUpfrontCost + LifetimeServiceCost) / LifetimePrintingHours;

    public decimal? ElectricityPerHour(decimal? pricePerKwh)
    {
        if (pricePerKwh < 0) throw new ArgumentOutOfRangeException(nameof(pricePerKwh));
        return AveragePrintingWatts / 1000m * pricePerKwh;
    }
}
