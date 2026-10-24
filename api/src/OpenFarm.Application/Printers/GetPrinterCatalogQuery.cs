using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed record GetPrinterCatalogQuery;

public sealed record PrinterCatalogBrand(string Brand, IReadOnlyList<PrinterModel> Models);

public sealed record PrinterCatalogResult(IReadOnlyList<PrinterCatalogBrand> Brands);
