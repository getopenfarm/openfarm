using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.FeedUnits;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class FeedUnitsListingTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetFeedUnits_OnSingleInletSimulatorPrinter_OmitsExternalSpoolByDefault()
    {
        var client = await AuthenticatedClientAsync(_client);
        var printerId = await RegisterSimulatorPrinterAsync(client);

        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);

        feedUnits.FeedUnits.Should().Contain(u => u.Kind == "MultiMaterialUnit");
        feedUnits.FeedUnits.Should().NotContain(u => u.Kind == "ExternalSpool");
    }

    /// <summary>
    /// FR-023/FR-025: the External Spool position is hidden by default (superseded by the active
    /// simulator AMS), but once a spool is loaded directly onto it (always allowed — FR-022 never
    /// hard-blocks a direct load), the safety net keeps it visible. The External Spool's FeedSlot id
    /// is fetched via the repository directly (not the HTTP API) — the hide-by-default filter is
    /// exactly what makes it otherwise undiscoverable before US4's reveal action exists.
    /// </summary>
    [Fact]
    public async Task GetFeedUnits_AfterLoadingASpoolDirectlyOntoTheExternalSpool_IncludesItViaTheSafetyNet()
    {
        var client = await AuthenticatedClientAsync(_client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        await WaitForFeedUnitsAsync(client, printerId);

        using var scope = factory.Services.CreateScope();
        var feedUnitRepository = scope.ServiceProvider.GetRequiredService<IFeedUnitRepository>();
        var externalSpoolSlotId = (await WaitForExternalSpoolSlotAsync(feedUnitRepository, printerId)).Id;

        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        var loadResponse = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = externalSpoolSlotId });
        loadResponse.EnsureSuccessStatusCode();

        var feedUnitsResponse = await client.GetAsync(new Uri($"/api/v1/printers/{printerId}/feed-units", UriKind.Relative));
        var body = await feedUnitsResponse.Content.ReadFromJsonAsync<ListFeedUnitsResponse>();

        body!.FeedUnits.Should().Contain(u => u.Kind == "ExternalSpool" && u.FeedSlots.Any(s => s.Id == externalSpoolSlotId));
    }

    private static async Task<FeedSlot> WaitForExternalSpoolSlotAsync(IFeedUnitRepository repository, Guid printerId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cts.IsCancellationRequested)
        {
            var units = await repository.GetByPrinterIdAsync(printerId, CancellationToken.None);
            var externalSpool = units.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.ExternalSpool);
            if (externalSpool is not null && externalSpool.FeedSlots.Count > 0)
            {
                return externalSpool.FeedSlots[0];
            }

            await Task.Delay(200, CancellationToken.None);
        }

        throw new TimeoutException("External Spool feed slot did not appear in time.");
    }
}
