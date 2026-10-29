using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed class GeneralSettingsRequest
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal? ElectricityPricePerKwh { get; set; }
}
public sealed class GeneralSettingsResponse
{
    public string CurrencyCode { get; set; } = "USD";
    public decimal? ElectricityPricePerKwh { get; set; }
    public string[] SupportedCurrencies { get; set; } = FarmSettings.SupportedCurrencies;
}
public sealed class GeneralSettingsValidator : AbstractValidator<GeneralSettingsRequest>
{
    public GeneralSettingsValidator()
    {
        RuleFor(x => x.CurrencyCode).Must(x => FarmSettings.SupportedCurrencies.Contains(x)).WithMessage("Select a supported currency.");
        RuleFor(x => x.ElectricityPricePerKwh).GreaterThanOrEqualTo(0m);
    }
}
[Permission("settings:general:read")]
public sealed class GetGeneralSettingsEndpoint(IFarmSettingsRepository settings) : EndpointWithoutRequest<GeneralSettingsResponse>
{
    public override void Configure() { Get("/api/v1/settings/general"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var farm = await settings.GetAsync(ct);
        Response = new() { CurrencyCode = farm.CurrencyCode, ElectricityPricePerKwh = farm.ElectricityPricePerKwh };
    }
}
[Permission("settings:general:update")]
public sealed class UpdateGeneralSettingsEndpoint(IFarmSettingsRepository settings) : Endpoint<GeneralSettingsRequest, GeneralSettingsResponse>
{
    public override void Configure() { Put("/api/v1/settings/general"); Policies("Permission"); }
    public override async Task HandleAsync(GeneralSettingsRequest req, CancellationToken ct)
    {
        var farm = await settings.GetAsync(ct);
        farm.SetCurrency(req.CurrencyCode);
        farm.SetElectricityPrice(req.ElectricityPricePerKwh);
        await settings.SaveChangesAsync(ct);
        Response = new() { CurrencyCode = farm.CurrencyCode, ElectricityPricePerKwh = farm.ElectricityPricePerKwh };
    }
}
