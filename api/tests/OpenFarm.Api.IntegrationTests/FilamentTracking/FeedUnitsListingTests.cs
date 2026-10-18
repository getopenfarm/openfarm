using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.FeedUnits;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Infrastructure.Persistence;
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

        var externalSpoolSlotId = (await WaitForExternalSpoolSlotAsync(factory, printerId)).Id;

        var variantId = await CreateFilamentVariantAsync(client);
        var spoolId = await RegisterSpoolAsync(client, variantId);
        var loadResponse = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = externalSpoolSlotId });
        loadResponse.EnsureSuccessStatusCode();

        var feedUnitsResponse = await client.GetAsync(new Uri($"/api/v1/printers/{printerId}/feed-units", UriKind.Relative));
        var body = await feedUnitsResponse.Content.ReadFromJsonAsync<ListFeedUnitsResponse>();

        body!.FeedUnits.Should().Contain(u => u.Kind == "ExternalSpool" && u.FeedSlots.Any(s => s.Id == externalSpoolSlotId));
    }

    /// <summary>
    /// Queries via a raw DI scope rather than the HTTP pipeline. A fresh scope/DbContext per poll
    /// avoids reusing a change tracker against rows the background telemetry sync is concurrently
    /// inserting via its own DbContext.
    /// </summary>
    private static async Task<FeedSlot> WaitForExternalSpoolSlotAsync(CustomWebApplicationFactory factory, Guid printerId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cts.IsCancellationRequested)
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var units = await dbContext.FeedUnits
                .AsNoTracking()
                .Include(u => u.FeedSlots)
                .Where(u => u.PrinterId == printerId)
                .ToListAsync(CancellationToken.None);
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
