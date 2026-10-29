using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Settings;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Settings;

public sealed class GeneralSettingsEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new LoginCommand("admin", "admin-openfarm"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    [Fact]
    public async Task CurrencyDefaultsToUsdAndGeneralSettingsRequireAuthentication()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(new Uri("/api/v1/settings/general", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var client = await ClientAsync();
        var settings = await client.GetFromJsonAsync<GeneralSettingsResponse>("/api/v1/settings/general");
        settings!.CurrencyCode.Should().Be("USD");
        settings.SupportedCurrencies.Should().Contain(["USD", "MYR", "EUR", "JPY"]);
    }

    [Fact]
    public async Task GeneralTariffAndLegacyEndpointShareStorageAndCurrencyDoesNotConvertAmounts()
    {
        using var client = await ClientAsync();
        var uri = new Uri("/api/v1/settings/general", UriKind.Relative);
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/models", UriKind.Relative), new
        {
            brand = "Bambu",
            modelCode = "P1S",
            printerPurchasePrice = 1234.56m
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(uri, new { currencyCode = "MYR", electricityPricePerKwh = 0.5m })).EnsureSuccessStatusCode();
        var saved = await client.GetFromJsonAsync<GeneralSettingsResponse>(uri);
        saved!.CurrencyCode.Should().Be("MYR");
        saved.ElectricityPricePerKwh.Should().Be(0.5m);
        var costs = await client.GetFromJsonAsync<PrinterCostsResponse>("/api/v1/settings/printer-costs");
        costs!.ElectricityPricePerKwh.Should().Be(0.5m);
        costs.Models.Single(m => m.Brand == "Bambu" && m.ModelCode == "P1S").PrinterPurchasePrice.Should().Be(1234.56m);
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/electricity", UriKind.Relative), new { electricityPricePerKwh = 0m })).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<GeneralSettingsResponse>(uri))!.ElectricityPricePerKwh.Should().Be(0m);
        (await client.PutAsJsonAsync(uri, new { currencyCode = "USD", electricityPricePerKwh = (decimal?)null })).EnsureSuccessStatusCode();
        var cleared = await client.GetFromJsonAsync<GeneralSettingsResponse>(uri);
        cleared!.ElectricityPricePerKwh.Should().BeNull();
    }

    [Fact]
    public async Task InvalidSettingsDoNotPartiallyUpdateCurrencyOrTariff()
    {
        using var client = await ClientAsync();
        var uri = new Uri("/api/v1/settings/general", UriKind.Relative);
        (await client.PutAsJsonAsync(uri, new { currencyCode = "USD", electricityPricePerKwh = 0.25m })).EnsureSuccessStatusCode();
        foreach (var invalid in new object[] {
            new { currencyCode = "XYZ", electricityPricePerKwh = 1m },
            new { currencyCode = "MYR", electricityPricePerKwh = -1m },
            new { currencyCode = "", electricityPricePerKwh = 1m },
            new { electricityPricePerKwh = 1m },
            new { currencyCode = (string?)null, electricityPricePerKwh = 1m }
        })
            (await client.PutAsJsonAsync(uri, invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var saved = await client.GetFromJsonAsync<GeneralSettingsResponse>(uri);
        saved!.CurrencyCode.Should().Be("USD");
        saved.ElectricityPricePerKwh.Should().Be(0.25m);
    }
}
