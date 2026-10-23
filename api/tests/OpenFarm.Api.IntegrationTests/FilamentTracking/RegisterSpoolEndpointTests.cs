using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class RegisterSpoolEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, RegisterSpoolRequest request) =>
        client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), request);

    private static RegisterSpoolLineRequest Line(
        Guid variantId, decimal weight = 1000, int quantity = 1, decimal? remaining = null, decimal? price = null, string? label = null) =>
        new() { FilamentVariantId = variantId, SpoolWeightGrams = weight, Quantity = quantity, RemainingWeightGrams = remaining, Price = price, Label = label };

    private static RegisterSpoolRequest Delivery(params RegisterSpoolLineRequest[] lines) => new() { Lines = lines };

    private static async Task<IReadOnlyList<SpoolResponse>> SpoolsOfAsync(HttpClient client, Guid variantId) =>
        (await client.GetFromJsonAsync<ListSpoolsResponse>($"/api/v1/spools?filamentVariantId={variantId}"))!.Spools;

    [Fact]
    public async Task Register_WithoutRemainingWeight_DefaultsToSpoolWeightAndCreatesInStorage()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, Delivery(Line(variantId)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var spool = (await response.Content.ReadFromJsonAsync<RegisterSpoolsResponse>())!.Spools.Single();
        spool.State.Should().Be("InStorage");
        spool.RemainingWeightGrams.Should().Be(1000);
        spool.FeedSlotId.Should().BeNull();
        spool.Price.Should().BeNull();
        spool.PricePerKg.Should().BeNull();
    }

    [Fact]
    public async Task Register_WithLowerRemainingWeight_CreatesPartiallyUsedSpool()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, Delivery(Line(variantId, remaining: 400)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var spool = (await response.Content.ReadFromJsonAsync<RegisterSpoolsResponse>())!.Spools.Single();
        spool.State.Should().Be("InStorage");
        spool.RemainingWeightGrams.Should().Be(400);
    }

    [Fact]
    public async Task Register_WithUnknownFilamentVariantId_Returns404()
    {
        var client = await AuthenticatedClientAsync(_client);

        var response = await RegisterAsync(client, Delivery(Line(Guid.NewGuid())));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The remaining-vs-spool-weight relationship is a request validator rule, not a thrown domain exception — so like every other
    /// validator-caught failure in this codebase, FastEndpoints' validation pre-processor returns 400, not 422 (422 is reserved for
    /// DomainExceptionHandler's mapped domain exceptions; see e.g. InvalidSpoolStateTransitionException).
    /// </summary>
    [Fact]
    public async Task Register_WithRemainingWeightAboveSpoolWeight_Returns400()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, Delivery(Line(variantId, weight: 500, remaining: 600)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithAQuantity_AddsThatManySpoolsWithTheSameDetails_AndLogsEach()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, new RegisterSpoolRequest
        {
            Lines = [Line(variantId, weight: 750, quantity: 4, remaining: 700, price: 18.5m, label: "Shelf B")],
            Supplier = "Filament Shop",
            OrderNumber = "PO-1042",
            ReceivedOn = new DateOnly(2026, 10, 20),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var spools = (await response.Content.ReadFromJsonAsync<RegisterSpoolsResponse>())!.Spools;
        spools.Should().HaveCount(4);
        spools.Select(s => s.Id).Distinct().Should().HaveCount(4);
        spools.Should().OnlyContain(s =>
            s.Label == "Shelf B" && s.SpoolWeightGrams == 750 && s.RemainingWeightGrams == 700 && s.Price == 18.5m
            && s.Supplier == "Filament Shop" && s.OrderNumber == "PO-1042" && s.ReceivedOn == new DateOnly(2026, 10, 20));
        spools[0].PricePerKg.Should().BeApproximately(24.6667m, 0.0001m);

        (await SpoolsOfAsync(client, variantId)).Should().HaveCount(4);
        foreach (var spool in spools)
        {
            var log = (await client.GetFromJsonAsync<ActivityLogPageResponse>($"/api/v1/activity-log?entityId={spool.Id}&pageSize=100"))!;
            log.Items.Should().ContainSingle(i => i.EventType == "SpoolRegistered");
        }
    }

    [Fact]
    public async Task Register_ADeliveryOfSeveralColors_AddsEachLine_WithTheSharedPurchaseDetails()
    {
        var client = await AuthenticatedClientAsync(_client);
        var black = await CreateFilamentVariantAsync(client);
        var white = await CreateFilamentVariantAsync(client, "White", "#ffffff");

        var response = await RegisterAsync(client, new RegisterSpoolRequest
        {
            Lines = [Line(black, quantity: 3, price: 20m), Line(white, weight: 500, quantity: 2)],
            Supplier = "Filament Shop",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<RegisterSpoolsResponse>())!.Spools.Should().HaveCount(5);
        var blacks = await SpoolsOfAsync(client, black);
        var whites = await SpoolsOfAsync(client, white);
        blacks.Should().HaveCount(3).And.OnlyContain(s => s.SpoolWeightGrams == 1000 && s.Price == 20m && s.Supplier == "Filament Shop");
        whites.Should().HaveCount(2).And.OnlyContain(s => s.SpoolWeightGrams == 500 && s.Price == null && s.Supplier == "Filament Shop");
    }

    [Fact]
    public async Task Register_AnUnknownColorOnAnyLine_AddsNothing()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, Delivery(Line(variantId, quantity: 3), Line(Guid.NewGuid())));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SpoolsOfAsync(client, variantId)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task Register_WithAQuantityOutOfRange_Returns400_AndAddsNothing(int quantity)
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, Delivery(Line(variantId, quantity: quantity)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SpoolsOfAsync(client, variantId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Register_MoreThanAThousandSpoolsInOneDelivery_Returns400_EvenWhenTheSumWouldOverflow()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var tooMany = await RegisterAsync(client, Delivery(Line(variantId, quantity: 400), Line(variantId, quantity: 400), Line(variantId, quantity: 400)));
        var overflow = await RegisterAsync(client, Delivery(Line(variantId, quantity: int.MaxValue), Line(variantId, quantity: int.MaxValue)));

        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        overflow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SpoolsOfAsync(client, variantId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Register_WithoutLines_OrWithMoreThan20_Returns400()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var none = await RegisterAsync(client, Delivery());
        var tooManyLines = await RegisterAsync(client, Delivery(Enumerable.Range(0, 21).Select(_ => Line(variantId)).ToArray()));

        none.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        tooManyLines.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_ZeroPriceIsKeptAsZero_NotAsUnknown()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, Delivery(Line(variantId, price: 0m)));

        var spool = (await response.Content.ReadFromJsonAsync<RegisterSpoolsResponse>())!.Spools.Single();
        spool.Price.Should().Be(0m);
        spool.PricePerKg.Should().Be(0m);
    }

    [Fact]
    public async Task Register_WithANegativePrice_Returns400()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, Delivery(Line(variantId, price: -1m)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithSupplierOrOrderNumberOver100Characters_Returns400()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var tooLong = new string('x', 101);

        var supplier = await RegisterAsync(client, new RegisterSpoolRequest { Lines = [Line(variantId)], Supplier = tooLong });
        var order = await RegisterAsync(client, new RegisterSpoolRequest { Lines = [Line(variantId)], OrderNumber = tooLong });

        supplier.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        order.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
