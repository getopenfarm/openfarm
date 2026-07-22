namespace OpenFarm.Domain.Printers;

/// <summary>
/// Compile-time (Brand, ModelCode) -&gt; PrinterModel lookup. Scoped to Bambu X1/P1/A1-series
/// this phase; adding a brand/model is a new entry here, never a rewrite (data-model.md).
/// </summary>
public static class PrinterModelCatalog
{
    private static readonly Dictionary<(PrinterBrand Brand, string ModelCode), PrinterModel> Entries =
        new Dictionary<(PrinterBrand, string), PrinterModel>
        {
            [(PrinterBrand.Bambu, "X1")] = new PrinterModel(PrinterBrand.Bambu, "X1", CameraTransport.Rtsp, VendorMachineModelName: "Bambu Lab X1"),
            [(PrinterBrand.Bambu, "X1C")] = new PrinterModel(PrinterBrand.Bambu, "X1C", CameraTransport.Rtsp, VendorMachineModelName: "Bambu Lab X1 Carbon"),
            [(PrinterBrand.Bambu, "X1E")] = new PrinterModel(PrinterBrand.Bambu, "X1E", CameraTransport.Rtsp, VendorMachineModelName: "Bambu Lab X1E"),
            [(PrinterBrand.Bambu, "P1P")] = new PrinterModel(PrinterBrand.Bambu, "P1P", CameraTransport.BinaryImage, VendorMachineModelName: "Bambu Lab P1P"),
            [(PrinterBrand.Bambu, "P1S")] = new PrinterModel(PrinterBrand.Bambu, "P1S", CameraTransport.BinaryImage, VendorMachineModelName: "Bambu Lab P1S"),
            [(PrinterBrand.Bambu, "A1")] = new PrinterModel(PrinterBrand.Bambu, "A1", CameraTransport.BinaryImage, VendorMachineModelName: "Bambu Lab A1"),
            [(PrinterBrand.Bambu, "A1MINI")] = new PrinterModel(PrinterBrand.Bambu, "A1MINI", CameraTransport.BinaryImage, VendorMachineModelName: "Bambu Lab A1 mini"),
            [(PrinterBrand.System, "Simulation")] = new PrinterModel(PrinterBrand.System, "Simulation", CameraTransport.BinaryImage),
        };

    public static PrinterModel Resolve(string brand, string modelCode)
    {
        if (!TryResolve(brand, modelCode, out var model))
        {
            throw new UnknownPrinterModelException(brand, modelCode);
        }

        return model!;
    }

    public static bool TryResolve(string brand, string modelCode, out PrinterModel? model)
    {
        if (Enum.TryParse<PrinterBrand>(brand, ignoreCase: true, out var parsedBrand)
            && Entries.TryGetValue((parsedBrand, modelCode), out var found))
        {
            model = found;
            return true;
        }

        model = null;
        return false;
    }

    /// <summary>
    /// Every (Brand, ModelCode) entry currently registered, grouped by brand — the read path a
    /// backend-exposed catalog endpoint (US3) projects into a response, guaranteed to never drift
    /// from what <see cref="Resolve"/>/<see cref="TryResolve"/> would accept since both read the
    /// same static dictionary.
    /// </summary>
    public static IReadOnlyList<PrinterCatalogBrandGroup> All =>
        Entries.Keys
            .GroupBy(key => key.Brand)
            .Select(group => new PrinterCatalogBrandGroup(
                group.Key,
                group.Select(key => key.ModelCode).ToList()))
            .ToList();
}

public sealed record PrinterCatalogBrandGroup(PrinterBrand Brand, IReadOnlyList<string> Models);
