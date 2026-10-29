using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Assemblies;

public sealed record AssemblyMachineCostContribution(Guid PartId, string? Brand, string? ModelCode,
    decimal? Hours, decimal? PrinterDepreciation, decimal? Electricity);

public sealed class AssemblyMachineCosts(IPartRepository parts, IPrinterCostRepository costs, IFarmSettingsRepository settings)
{
    public async Task<IReadOnlyList<AssemblyMachineCostContribution>> CalculateAsync(IReadOnlyList<ExplodedPartQuantity> quantities, CancellationToken ct)
    {
        var models = (await costs.ListAsync(ct)).ToDictionary(x => (x.Brand, x.ModelCode));
        var tariff = (await settings.GetAsync(ct)).ElectricityPricePerKwh;
        var rows = new List<AssemblyMachineCostContribution>();
        foreach (var row in quantities)
        {
            var part = await parts.GetByIdAsync(row.PartId, ct);
            var model = PrinterModelCatalog.TryResolveSlicedModel(part?.Estimation?.MachineModel);
            decimal? hours = part?.EstimatedPrintTimeMinutes is { } minutes
                ? row.Quantity / Math.Max(1, part.UnitsPerPrint) * minutes / 60m : null;
            var rate = model is null ? null : models.GetValueOrDefault((model.Brand.ToString(), model.ModelCode))?.Rates;
            rows.Add(new(row.PartId, model?.Brand.ToString(), model?.ModelCode, hours,
                hours * rate?.DepreciationPerHour, hours * rate?.ElectricityPerHour(tariff)));
        }
        return rows;
    }
}
