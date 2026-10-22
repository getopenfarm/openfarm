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

    [Fact]
    public async Task Register_WithoutRemainingWeight_DefaultsToSpoolWeightAndCreatesInStorage()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, new RegisterSpoolRequest
        {
            FilamentVariantId = variantId,
            SpoolWeightGrams = 1000,
        });

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

        var response = await RegisterAsync(client, new RegisterSpoolRequest
        {
            FilamentVariantId = variantId,
            SpoolWeightGrams = 1000,
            RemainingWeightGrams = 400,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var spool = (await response.Content.ReadFromJsonAsync<RegisterSpoolsResponse>())!.Spools.Single();
        spool.State.Should().Be("InStorage");
        spool.RemainingWeightGrams.Should().Be(400);
    }

    [Fact]
    public async Task Register_WithUnknownFilamentVariantId_Returns404()
    {
        var client = await AuthenticatedClientAsync(_client);

        var response = await RegisterAsync(client, new RegisterSpoolRequest
        {
            FilamentVariantId = Guid.NewGuid(),
            SpoolWeightGrams = 1000,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The remaining-vs-spool-weight relationship is enforced by RegisterSpoolCommandValidator (a
    /// FluentValidation rule), not a thrown domain exception — so like every other validator-caught
    /// failure in this codebase, FastEndpoints' validation pre-processor returns 400, not 422 (422 is
    /// reserved for DomainExceptionHandler's mapped domain exceptions; see e.g.
    /// InvalidSpoolStateTransitionException).
    /// </summary>
    [Fact]
    public async Task Register_WithRemainingWeightAboveSpoolWeight_Returns400()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, new RegisterSpoolRequest
        {
            FilamentVariantId = variantId,
            SpoolWeightGrams = 500,
            RemainingWeightGrams = 600,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithAQuantity_AddsThatManySpoolsWithTheSameDetails_AndLogsEach()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, new RegisterSpoolRequest
        {
            FilamentVariantId = variantId,
            Label = "Shelf B",
            SpoolWeightGrams = 750,
            RemainingWeightGrams = 700,
            Quantity = 4,
            Price = 18.5m,
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

        var listed = (await client.GetFromJsonAsync<ListSpoolsResponse>($"/api/v1/spools?filamentVariantId={variantId}"))!;
        listed.Spools.Should().HaveCount(4);
        foreach (var spool in spools)
        {
            var log = (await client.GetFromJsonAsync<ActivityLogPageResponse>($"/api/v1/activity-log?entityId={spool.Id}&pageSize=100"))!;
            log.Items.Should().ContainSingle(i => i.EventType == "SpoolRegistered");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Register_WithAQuantityOutOfRange_Returns400_AndAddsNothing(int quantity)
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, new RegisterSpoolRequest { FilamentVariantId = variantId, SpoolWeightGrams = 1000, Quantity = quantity });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<ListSpoolsResponse>($"/api/v1/spools?filamentVariantId={variantId}"))!.Spools.Should().BeEmpty();
    }

    [Fact]
    public async Task Register_ZeroPriceIsKeptAsZero_NotAsUnknown()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, new RegisterSpoolRequest { FilamentVariantId = variantId, SpoolWeightGrams = 1000, Price = 0m });

        var spool = (await response.Content.ReadFromJsonAsync<RegisterSpoolsResponse>())!.Spools.Single();
        spool.Price.Should().Be(0m);
        spool.PricePerKg.Should().Be(0m);
    }

    [Fact]
    public async Task Register_WithANegativePrice_Returns400()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await RegisterAsync(client, new RegisterSpoolRequest { FilamentVariantId = variantId, SpoolWeightGrams = 1000, Price = -1m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithSupplierOrOrderNumberOver100Characters_Returns400()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);
        var tooLong = new string('x', 101);

        var supplier = await RegisterAsync(client, new RegisterSpoolRequest { FilamentVariantId = variantId, SpoolWeightGrams = 1000, Supplier = tooLong });
        var order = await RegisterAsync(client, new RegisterSpoolRequest { FilamentVariantId = variantId, SpoolWeightGrams = 1000, OrderNumber = tooLong });

        supplier.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        order.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
