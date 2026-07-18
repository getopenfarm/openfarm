using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Spools;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class RegisterSpoolEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Register_WithoutRemainingWeight_DefaultsToSpoolWeightAndCreatesInStorage()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), new RegisterSpoolRequest
        {
            FilamentVariantId = variantId,
            SpoolWeightGrams = 1000,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var spool = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        spool!.State.Should().Be("InStorage");
        spool.RemainingWeightGrams.Should().Be(1000);
        spool.FeedSlotId.Should().BeNull();
    }

    [Fact]
    public async Task Register_WithLowerRemainingWeight_CreatesPartiallyUsedSpool()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), new RegisterSpoolRequest
        {
            FilamentVariantId = variantId,
            SpoolWeightGrams = 1000,
            RemainingWeightGrams = 400,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var spool = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        spool!.State.Should().Be("InStorage");
        spool.RemainingWeightGrams.Should().Be(400);
    }

    [Fact]
    public async Task Register_WithUnknownFilamentVariantId_Returns404()
    {
        var client = await AuthenticatedClientAsync(_client);

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), new RegisterSpoolRequest
        {
            FilamentVariantId = Guid.NewGuid(),
            SpoolWeightGrams = 1000,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Register_WithRemainingWeightAboveSpoolWeight_Returns422()
    {
        var client = await AuthenticatedClientAsync(_client);
        var variantId = await CreateFilamentVariantAsync(client);

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), new RegisterSpoolRequest
        {
            FilamentVariantId = variantId,
            SpoolWeightGrams = 500,
            RemainingWeightGrams = 600,
        });

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }
}
