using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.Notifications;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Api.Endpoints.Vendors;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI. Stock per colour on GET /api/v1/filaments and low-stock items.</summary>
public class FilamentStockTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static async Task<FilamentResponse> CreateFilamentAsync(
        HttpClient client, decimal? pricePerKg, decimal? lowStockGrams, params CreateFilamentVariantRequest[] variants)
    {
        var vendorResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = $"Vendor {Guid.NewGuid():N}" });
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>();
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), new CreateFilamentRequest
        {
            VendorId = vendor!.Id,
            Material = "PLA Basic",
            MaterialType = "PLA",
            PricePerKg = pricePerKg,
            LowStockGrams = lowStockGrams,
            Variants = variants,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FilamentResponse>())!;
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, Guid variantId, decimal remaining = 1000, decimal? price = null)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), new RegisterSpoolRequest
        {
            Lines = [new RegisterSpoolLineRequest { FilamentVariantId = variantId, SpoolWeightGrams = 1000, RemainingWeightGrams = remaining, Price = price }],
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RegisterSpoolsResponse>())!.Spools.Single().Id;
    }

    private static async Task<IReadOnlyList<VariantStockResponse>> StockAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListFilamentsResponse>(new Uri("/api/v1/filaments", UriKind.Relative)))!.Stock;

    private static async Task<List<LowStockNotificationResponse>> LowStockAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListNotificationsResponse>(new Uri("/api/v1/notifications", UriKind.Relative)))!.LowStock;

    [Fact]
    public async Task List_ReportsEachColorsStock_ValuingEverySpoolAtItsOwnPrice()
    {
        var client = await AuthenticatedClientAsync(_client);
        var filament = await CreateFilamentAsync(client, pricePerKg: 20, lowStockGrams: 1500,
            new CreateFilamentVariantRequest { ColorName = "Black", ColorHex = "#000000" },
            new CreateFilamentVariantRequest { ColorName = "Red", ColorHex = "#ff0000" },
            new CreateFilamentVariantRequest { ColorName = "Blue", ColorHex = "#0000ff" },
            new CreateFilamentVariantRequest { ColorName = "White", ColorHex = "#ffffff", PricePerKg = null, LowStockGrams = 0 });
        var black = filament.Variants.Single(v => v.ColorName == "Black").Id;
        var red = filament.Variants.Single(v => v.ColorName == "Red").Id;
        var blue = filament.Variants.Single(v => v.ColorName == "Blue").Id;
        var white = filament.Variants.Single(v => v.ColorName == "White").Id;

        await RegisterAsync(client, black, price: 30);
        var used = await RegisterAsync(client, black);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        await client.PutAsJsonAsync(
            new Uri($"/api/v1/spools/{used}/load", UriKind.Relative), new { feedSlotId = feedUnits.FeedUnits[0].FeedSlots[0].Id });
        (await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{used}/usage", UriKind.Relative), new { gramsConsumed = 100 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var finished = await RegisterAsync(client, red, remaining: 400);
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{finished}/unload", UriKind.Relative), new { action = "MarkFinished" });
        await RegisterAsync(client, white, remaining: 10);

        var stock = (await StockAsync(client)).ToDictionary(s => s.FilamentVariantId);

        var blackStock = stock[black];
        blackStock.OnHandGrams.Should().Be(1900);
        blackStock.ActiveSpoolCount.Should().Be(2);
        blackStock.StockValue.Should().Be(48); // 1 kg at the spool's 30/kg + 0.9 kg at the filament's 20/kg
        blackStock.UnpricedSpoolCount.Should().Be(0);
        blackStock.UsedLast30DaysGrams.Should().Be(100);
        blackStock.ThresholdGrams.Should().Be(1500);
        blackStock.IsLow.Should().BeFalse();

        // Stocked once, all finished: the filament's default applies and it is out.
        stock[red].OnHandGrams.Should().Be(0);
        stock[red].ActiveSpoolCount.Should().Be(0);
        stock[red].IsLow.Should().BeTrue();

        // Never stocked: the default does not cover it.
        stock[blue].ThresholdGrams.Should().BeNull();
        stock[blue].IsLow.Should().BeFalse();

        // Its own threshold of 0 silences the default.
        stock[white].ThresholdGrams.Should().Be(0);
        stock[white].IsLow.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnpricedSpool_IsCountedInsteadOfValuedAtZero()
    {
        var client = await AuthenticatedClientAsync(_client);
        var filament = await CreateFilamentAsync(client, pricePerKg: null, lowStockGrams: null,
            new CreateFilamentVariantRequest { ColorName = "Grey", ColorHex = "#888888" });
        var grey = filament.Variants.Single().Id;
        await RegisterAsync(client, grey, price: 25);
        await RegisterAsync(client, grey, remaining: 500);

        var stock = (await StockAsync(client)).Single(s => s.FilamentVariantId == grey);

        stock.StockValue.Should().Be(25);
        stock.UnpricedSpoolCount.Should().Be(1);
    }

    [Fact]
    public async Task Notifications_ListALowColor_UntilItIsRestocked()
    {
        var client = await AuthenticatedClientAsync(_client);
        var filament = await CreateFilamentAsync(client, pricePerKg: null, lowStockGrams: null,
            new CreateFilamentVariantRequest { ColorName = "Orange", ColorHex = "#ff8800", LowStockGrams = 500 });
        var orange = filament.Variants.Single().Id;
        var spool = await RegisterAsync(client, orange, remaining: 400);

        var low = (await LowStockAsync(client)).Single(item => item.FilamentVariantId == orange);
        low.FilamentId.Should().Be(filament.Id);
        low.ColorName.Should().Be("Orange");
        low.OnHandGrams.Should().Be(400);
        low.ThresholdGrams.Should().Be(500);

        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spool}", UriKind.Relative), new { remainingWeightGrams = 600 });

        (await LowStockAsync(client)).Should().NotContain(item => item.FilamentVariantId == orange);
    }

    [Fact]
    public async Task Edit_KeepsTheThresholdWhenOmitted_AndClearsItOnNull()
    {
        var client = await AuthenticatedClientAsync(_client);
        var filament = await CreateFilamentAsync(client, pricePerKg: null, lowStockGrams: 1000,
            new CreateFilamentVariantRequest { ColorName = "Black", ColorHex = "#000000", LowStockGrams = 250 });
        var variantId = filament.Variants.Single().Id;
        var uri = new Uri($"/api/v1/filaments/{filament.Id}", UriKind.Relative);

        var kept = await client.PutAsJsonAsync(uri, new
        {
            vendorId = filament.VendorId,
            material = filament.Material,
            materialType = filament.MaterialType,
            newVariants = Array.Empty<object>(),
            profileAssignments = Array.Empty<object>(),
            variantPrices = Array.Empty<object>(),
        });
        var keptBody = await kept.Content.ReadFromJsonAsync<FilamentResponse>();
        keptBody!.LowStockGrams.Should().Be(1000);
        keptBody.Variants.Single().LowStockGrams.Should().Be(250);

        var cleared = await client.PutAsJsonAsync(uri, new
        {
            vendorId = filament.VendorId,
            material = filament.Material,
            materialType = filament.MaterialType,
            newVariants = Array.Empty<object>(),
            profileAssignments = Array.Empty<object>(),
            variantPrices = new[] { new { variantId, pricePerKg = (decimal?)null, lowStockGrams = (decimal?)null } },
            lowStockGrams = (decimal?)null,
        });
        var clearedBody = await cleared.Content.ReadFromJsonAsync<FilamentResponse>();
        clearedBody!.LowStockGrams.Should().BeNull();
        clearedBody.Variants.Single().LowStockGrams.Should().BeNull();
    }

    private static async Task<FilamentResponse> CreateWithDaysAsync(HttpClient client, int? lowStockDays, int? colorDays)
    {
        var vendorResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = $"Vendor {Guid.NewGuid():N}" });
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>();
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), new CreateFilamentRequest
        {
            VendorId = vendor!.Id,
            Material = "PLA Basic",
            MaterialType = "PLA",
            LowStockDays = lowStockDays,
            Variants = [new CreateFilamentVariantRequest { ColorName = "Black", ColorHex = "#000000", LowStockDays = colorDays }],
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FilamentResponse>())!;
    }

    [Fact]
    public async Task DaysLeft_FollowsTheLast30DaysPace_AndAColorLowByDaysAloneIsListedInTheBell()
    {
        var client = await AuthenticatedClientAsync(_client);
        var filament = await CreateWithDaysAsync(client, lowStockDays: 30, colorDays: null);
        var black = filament.Variants.Single().Id;
        var spool = await RegisterAsync(client, black);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        await client.PutAsJsonAsync(
            new Uri($"/api/v1/spools/{spool}/load", UriKind.Relative), new { feedSlotId = feedUnits.FeedUnits[0].FeedSlots[0].Id });
        (await client.PostAsJsonAsync(new Uri($"/api/v1/spools/{spool}/usage", UriKind.Relative), new { gramsConsumed = 600 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stock = (await StockAsync(client)).Single(s => s.FilamentVariantId == black);
        var notifications = await client.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative));

        // 400 g left after 600 g in 30 days: 20 days, under the filament's 30.
        stock.DaysLeft.Should().Be(20);
        stock.ThresholdDays.Should().Be(30);
        stock.ThresholdGrams.Should().BeNull();
        stock.IsLow.Should().BeTrue();
        notifications.StatusCode.Should().Be(HttpStatusCode.OK);
        var low = (await notifications.Content.ReadFromJsonAsync<ListNotificationsResponse>())!.LowStock
            .Single(item => item.FilamentVariantId == black);
        low.ThresholdGrams.Should().BeNull();
        low.DaysLeft.Should().Be(20);
        low.ThresholdDays.Should().Be(30);
    }

    [Fact]
    public async Task Edit_KeepsTheDaysThresholdWhenOmitted_AndAColorListedWithoutDaysLosesItsOverride()
    {
        var client = await AuthenticatedClientAsync(_client);
        var filament = await CreateWithDaysAsync(client, lowStockDays: 14, colorDays: 7);
        var variantId = filament.Variants.Single().Id;
        var uri = new Uri($"/api/v1/filaments/{filament.Id}", UriKind.Relative);
        object Edit(object variantPrices, bool clearDefault) => clearDefault
            ? new
            {
                vendorId = filament.VendorId,
                material = filament.Material,
                materialType = filament.MaterialType,
                newVariants = Array.Empty<object>(),
                profileAssignments = Array.Empty<object>(),
                variantPrices,
                lowStockDays = (int?)null,
            }
            : new
            {
                vendorId = filament.VendorId,
                material = filament.Material,
                materialType = filament.MaterialType,
                newVariants = Array.Empty<object>(),
                profileAssignments = Array.Empty<object>(),
                variantPrices,
            };

        (await client.PutAsJsonAsync(uri, Edit(Array.Empty<object>(), clearDefault: false))).EnsureSuccessStatusCode();
        var listed = (await client.GetFromJsonAsync<ListFilamentsResponse>(new Uri("/api/v1/filaments", UriKind.Relative)))!
            .Filaments.Single(f => f.Id == filament.Id);
        var cleared = await client.PutAsJsonAsync(
            uri, Edit(new[] { new { variantId, pricePerKg = (decimal?)null, lowStockGrams = (decimal?)null } }, clearDefault: true));

        listed.LowStockDays.Should().Be(14);
        listed.Variants.Single().LowStockDays.Should().Be(7);
        var clearedBody = (await cleared.Content.ReadFromJsonAsync<FilamentResponse>())!;
        clearedBody.LowStockDays.Should().BeNull();
        clearedBody.Variants.Single().LowStockDays.Should().BeNull();
    }

    [Fact]
    public async Task ADaysThresholdOverAYear_IsRefused()
    {
        var client = await AuthenticatedClientAsync(_client);
        var vendorResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = $"Vendor {Guid.NewGuid():N}" });
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>();

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), new CreateFilamentRequest
        {
            VendorId = vendor!.Id,
            Material = "PLA",
            MaterialType = "PLA",
            LowStockDays = 366,
            Variants = [new CreateFilamentVariantRequest { ColorName = "Black", ColorHex = "#000000" }],
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ANegativeThreshold_IsRefused()
    {
        var client = await AuthenticatedClientAsync(_client);
        var vendorResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = $"Vendor {Guid.NewGuid():N}" });
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>();

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), new CreateFilamentRequest
        {
            VendorId = vendor!.Id,
            Material = "PLA",
            MaterialType = "PLA",
            LowStockGrams = -1,
            Variants = [new CreateFilamentVariantRequest { ColorName = "Black", ColorHex = "#000000" }],
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
