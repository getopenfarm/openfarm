using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public interface IPrinterCostRepository
{
    Task<IReadOnlySet<(string Brand, string ModelCode)>> ListFleetModelsAsync(CancellationToken ct);
    Task RemoveAsync(string brand, string modelCode, CancellationToken ct);
    Task<IReadOnlyList<PrinterModelCost>> ListAsync(CancellationToken ct);
    Task<PrinterModelCost> UpsertAsync(string brand, string modelCode, PrinterCostRates rates, CancellationToken ct);
}
