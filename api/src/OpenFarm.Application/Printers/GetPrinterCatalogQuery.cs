namespace OpenFarm.Application.Printers;

public sealed record GetPrinterCatalogQuery;

public sealed record PrinterCatalogBrand(string Brand, IReadOnlyList<string> Models);

public sealed record PrinterCatalogResult(IReadOnlyList<PrinterCatalogBrand> Brands);
