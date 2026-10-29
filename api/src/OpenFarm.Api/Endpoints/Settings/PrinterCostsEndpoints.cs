using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed class PrinterModelCostResponse
{
    public string Brand { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public decimal? PrinterPurchasePrice { get; set; }
    public decimal? PrinterUpfrontCost { get; set; }
    public decimal? LifetimeServiceCost { get; set; }
    public decimal? LifetimePrintingHours { get; set; }
    public decimal? AveragePrintingWatts { get; set; }
    public bool CanRemove { get; set; }
    public decimal? DepreciationPerHour { get; set; }
    public decimal? ElectricityPerHour { get; set; }

    public static PrinterModelCostResponse From(PrinterModelCost model, decimal? tariff, bool canRemove) => new()
    {
        CanRemove = canRemove,
        Brand = model.Brand,
        ModelCode = model.ModelCode,
        PrinterPurchasePrice = model.PrinterPurchasePrice,
        PrinterUpfrontCost = model.PrinterUpfrontCost,
        LifetimeServiceCost = model.LifetimeServiceCost,
        LifetimePrintingHours = model.LifetimePrintingHours,
        AveragePrintingWatts = model.AveragePrintingWatts,
        DepreciationPerHour = model.Rates.DepreciationPerHour,
        ElectricityPerHour = model.Rates.ElectricityPerHour(tariff)
    };
}

public sealed class PrinterCostsResponse
{
    public decimal? ElectricityPricePerKwh { get; set; }
    public List<PrinterModelCostResponse> Models { get; set; } = [];
}

[Permission("settings:printer-costs:read")]
public sealed class GetPrinterCostsEndpoint(IPrinterCostRepository costs, IFarmSettingsRepository settings)
    : EndpointWithoutRequest<PrinterCostsResponse>
{
    public override void Configure() { Get("/api/v1/settings/printer-costs"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var tariff = (await settings.GetAsync(ct)).ElectricityPricePerKwh;
        var fleetModels = await costs.ListFleetModelsAsync(ct);
        Response = new()
        {
            ElectricityPricePerKwh = tariff,
            Models = (await costs.ListAsync(ct)).Select(x => PrinterModelCostResponse.From(x, tariff, !fleetModels.Contains((x.Brand, x.ModelCode)))).ToList()
        };
    }
}

public sealed class UpdateElectricityPriceRequest
{
    public decimal? ElectricityPricePerKwh { get; set; }
}

public sealed class UpdateElectricityPriceValidator : AbstractValidator<UpdateElectricityPriceRequest>
{
    public UpdateElectricityPriceValidator() => RuleFor(x => x.ElectricityPricePerKwh).GreaterThanOrEqualTo(0m);
}

[Permission("settings:printer-costs:update")]
public sealed class UpdateElectricityPriceEndpoint(IFarmSettingsRepository settings)
    : Endpoint<UpdateElectricityPriceRequest, UpdateElectricityPriceRequest>
{
    public override void Configure() { Put("/api/v1/settings/printer-costs/electricity"); Policies("Permission"); }
    public override async Task HandleAsync(UpdateElectricityPriceRequest req, CancellationToken ct)
    {
        var farm = await settings.GetAsync(ct);
        farm.SetElectricityPrice(req.ElectricityPricePerKwh);
        await settings.SaveChangesAsync(ct);
        Response = req;
    }
}

public sealed class UpdatePrinterModelCostRequest
{
    public string Brand { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public decimal? PrinterPurchasePrice { get; set; }
    public decimal? PrinterUpfrontCost { get; set; }
    public decimal? LifetimeServiceCost { get; set; }
    public decimal? LifetimePrintingHours { get; set; }
    public decimal? AveragePrintingWatts { get; set; }
}

public sealed class UpdatePrinterModelCostValidator : AbstractValidator<UpdatePrinterModelCostRequest>
{
    public UpdatePrinterModelCostValidator()
    {
        RuleFor(x => x).Must(x => PrinterModelCatalog.Find(x.Brand, x.ModelCode) is not null)
            .WithMessage("Select a supported printer model.");
        RuleFor(x => x.PrinterPurchasePrice).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.PrinterUpfrontCost).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.LifetimeServiceCost).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.LifetimePrintingHours).GreaterThan(0m);
        RuleFor(x => x.AveragePrintingWatts).GreaterThanOrEqualTo(0m);
    }
}

[Permission("settings:printer-costs:update")]
public sealed class UpdatePrinterModelCostEndpoint(IPrinterCostRepository costs, IFarmSettingsRepository settings)
    : Endpoint<UpdatePrinterModelCostRequest, PrinterModelCostResponse>
{
    public override void Configure() { Put("/api/v1/settings/printer-costs/models"); Policies("Permission"); }
    public override async Task HandleAsync(UpdatePrinterModelCostRequest req, CancellationToken ct)
    {
        var rates = new PrinterCostRates(req.PrinterPurchasePrice, req.PrinterUpfrontCost,
            req.LifetimeServiceCost, req.LifetimePrintingHours, req.AveragePrintingWatts);
        var model = await costs.UpsertAsync(req.Brand, req.ModelCode, rates, ct);
        Response = PrinterModelCostResponse.From(model, (await settings.GetAsync(ct)).ElectricityPricePerKwh,
            !(await costs.ListFleetModelsAsync(ct)).Contains((model.Brand, model.ModelCode)));
    }
}

public sealed class RemovePrinterModelCostRequest
{
    public string Brand { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
}

[Permission("settings:printer-costs:update")]
public sealed class RemovePrinterModelCostEndpoint(IPrinterCostRepository costs) : Endpoint<RemovePrinterModelCostRequest>
{
    public override void Configure()
    {
        Delete("/api/v1/settings/printer-costs/models/{brand}/{modelCode}");
        Policies("Permission");
    }

    public override async Task HandleAsync(RemovePrinterModelCostRequest req, CancellationToken ct)
    {
        await costs.RemoveAsync(req.Brand, req.ModelCode, ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
