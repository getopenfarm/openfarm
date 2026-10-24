using OpenFarm.Application.Common;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>
/// Reads the existing in-code <see cref="PrinterModelCatalog"/> — no persistence, no
/// <c>Infrastructure</c>/EF Core involvement (research.md §4). Guaranteed to never drift from what
/// <c>RegisterPrinterCommandValidator</c>/<see cref="Printer.Create"/> would accept (FR-008), since
/// both read the same static dictionary.
/// </summary>
public sealed class GetPrinterCatalogQueryHandler : IQueryHandler<GetPrinterCatalogQuery, PrinterCatalogResult>
{
    public Task<PrinterCatalogResult> HandleAsync(GetPrinterCatalogQuery query, CancellationToken cancellationToken)
    {
        var brands = PrinterModelCatalog.Models
            .GroupBy(model => model.Brand)
            .Select(group => new PrinterCatalogBrand(group.Key.ToString(), group.ToList()))
            .ToList();

        return Task.FromResult(new PrinterCatalogResult(brands));
    }
}
