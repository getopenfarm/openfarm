using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Api.Endpoints.FeedUnits;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Production;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

public class ReloaderConfigurationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static Uri UnitsUrl(Guid printerId, bool all = false) =>
        new($"/api/v1/printers/{printerId}/feed-units{(all ? "?includeHidden=true" : "")}", UriKind.Relative);

    private async Task<(HttpClient Client, Guid PrinterId, FeedUnitResponse External)> SetupAsync()
    {
        var client = await AuthenticatedClientAsync(_client);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        await WaitForFeedUnitsAsync(client, printerId);
        var all = await client.GetFromJsonAsync<ListFeedUnitsResponse>(UnitsUrl(printerId, true));
        return (client, printerId, all!.FeedUnits.Single(u => u.Kind == "ExternalSpool"));
    }

    private static async Task<AddFeedUnitResponse> AddAsync(HttpClient client, Guid printerId, Guid? replacement = null)
    {
        var response = await client.PostAsJsonAsync(UnitsUrl(printerId), new
        {
            kind = "AutomaticFilamentReloader",
            name = $"Reloader {Guid.NewGuid():N}",
            slotCount = 2,
            replacesFeedUnitId = replacement,
            startingSlotPosition = 0,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddFeedUnitResponse>())!;
    }

    [Fact]
    public async Task Replacement_TransfersRegisteredExternalSpool_AndRoutesDetectionToOneSlot()
    {
        var (client, printerId, external) = await SetupAsync();
        var spoolId = await RegisterSpoolAsync(client, await CreateFilamentVariantAsync(client));
        (await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = external.FeedSlots[0].Id })).EnsureSuccessStatusCode();
        var reloader = await AddAsync(client, printerId, external.Id);
        var listed = await client.GetFromJsonAsync<ListFeedUnitsResponse>(UnitsUrl(printerId));
        listed!.FeedUnits.Should().NotContain(u => u.Id == external.Id);
        var unit = listed.FeedUnits.Single(u => u.Id == reloader.Id);
        unit.FeedingSlotId.Should().Be(reloader.FeedSlots[0].Id);
        unit.FeedingSelectionSource.Should().Be("Operator");
        unit.FeedSlots[0].CurrentSpool!.Id.Should().Be(spoolId);
        unit.FeedSlots[1].DetectedMaterialType.Should().BeNull();
        var printer = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printerId}", UriKind.Relative));
        unit.FeedSlots[0].DetectedMaterialType.Should().Be(printer!.FeedReadings.Single(r => r.Kind == FeedUnitKindCatalog.ExternalSpool).Slots.Single().MaterialType);
        var all = await client.GetFromJsonAsync<ListFeedUnitsResponse>(UnitsUrl(printerId, true));
        all!.FeedUnits.Single(u => u.Id == external.Id).FeedSlots[0].DetectedMaterialType.Should().BeNull();
    }

    [Fact]
    public async Task ConfigureExistingReloader_RejectsOccupiedTransfer_WithoutPartialChanges()
    {
        var (client, printerId, external) = await SetupAsync();
        var reloader = await AddAsync(client, printerId);
        var variant = await CreateFilamentVariantAsync(client);
        var original = await RegisterSpoolAsync(client, variant);
        var occupied = await RegisterSpoolAsync(client, variant);
        (await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{original}/load", UriKind.Relative), new { feedSlotId = external.FeedSlots[0].Id })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{occupied}/load", UriKind.Relative), new { feedSlotId = reloader.FeedSlots[0].Id })).EnsureSuccessStatusCode();
        var response = await client.PutAsJsonAsync(new Uri($"/api/v1/feed-units/{reloader.Id}/configuration", UriKind.Relative),
            new { replacesFeedUnitId = external.Id, feedingSlotId = reloader.FeedSlots[0].Id, transferExternalSpool = true });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var all = await client.GetFromJsonAsync<ListFeedUnitsResponse>(UnitsUrl(printerId, true));
        all!.FeedUnits.Single(u => u.Id == reloader.Id).ReplacesFeedUnitId.Should().BeNull();
        all.FeedUnits.Single(u => u.Id == external.Id).FeedSlots[0].CurrentSpool!.Id.Should().Be(original);
        response = await client.PutAsJsonAsync(new Uri($"/api/v1/feed-units/{reloader.Id}/configuration", UriKind.Relative),
            new { replacesFeedUnitId = external.Id, feedingSlotId = reloader.FeedSlots[0].Id, transferExternalSpool = false });
        response.EnsureSuccessStatusCode();
        var spools = await client.GetFromJsonAsync<ListSpoolsResponse>(new Uri("/api/v1/spools", UriKind.Relative));
        spools!.Spools.Single(s => s.Id == original).State.Should().Be("InStorage");
    }

    [Fact]
    public async Task Removal_ReleasesPort_AndRejectsStaleSlotMutations()
    {
        var (client, printerId, external) = await SetupAsync();
        var reloader = await AddAsync(client, printerId, external.Id);
        (await client.DeleteAsync(new Uri($"/api/v1/feed-units/{reloader.Id}", UriKind.Relative))).EnsureSuccessStatusCode();
        var all = await client.GetFromJsonAsync<ListFeedUnitsResponse>(UnitsUrl(printerId, true));
        all!.FeedUnits.Single(u => u.Id == external.Id).SupersededByFeedUnitId.Should().NotBe(reloader.Id);
        var next = await AddAsync(client, printerId, external.Id);
        next.Id.Should().NotBe(reloader.Id);
        var selection = await client.PutAsJsonAsync(new Uri($"/api/v1/feed-units/{reloader.Id}/feeding-slot", UriKind.Relative), new { feedingSlotId = reloader.FeedSlots[0].Id });
        selection.StatusCode.Should().Be((HttpStatusCode)422);
        var spoolId = await RegisterSpoolAsync(client, await CreateFilamentVariantAsync(client));
        var load = await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = reloader.FeedSlots[0].Id });
        load.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Replacement_RejectsAnotherPrintersPort_AndConcurrentDuplicateClaims()
    {
        var (client, printerId, external) = await SetupAsync();
        var otherPrinterId = await RegisterSimulatorPrinterAsync(client);
        var wrong = await client.PostAsJsonAsync(UnitsUrl(otherPrinterId), new
        {
            kind = "AutomaticFilamentReloader",
            name = "Wrong port",
            slotCount = 2,
            replacesFeedUnitId = external.Id,
        });
        wrong.StatusCode.Should().Be((HttpStatusCode)422);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => client.PostAsJsonAsync(UnitsUrl(printerId), new
        {
            kind = "AutomaticFilamentReloader",
            name = $"Concurrent {i}",
            slotCount = 2,
            replacesFeedUnitId = external.Id,
        })));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == (HttpStatusCode)422 || r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }
    [Fact]
    public async Task ProductionMaterials_UseOnlySelectedReloaderSlot()
    {
        var (client, printerId, _) = await SetupAsync();
        var reloader = await AddAsync(client, printerId);
        var black = await RegisterSpoolAsync(client, await CreateFilamentVariantAsync(client, colorHex: "#111111"));
        var white = await RegisterSpoolAsync(client, await CreateFilamentVariantAsync(client, colorHex: "#EEEEEE"));
        (await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{black}/load", UriKind.Relative), new { feedSlotId = reloader.FeedSlots[0].Id })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{white}/load", UriKind.Relative), new { feedSlotId = reloader.FeedSlots[1].Id })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>();
            var materials = await service.ResolveLoadedMaterialsAsync(await db.Printers.SingleAsync(p => p.Id == printerId), CancellationToken.None);
            materials.Should().HaveCount(1);
            materials[0].ColorHex.Should().Be("#111111");
        }
        (await client.PutAsJsonAsync(new Uri($"/api/v1/feed-units/{reloader.Id}/feeding-slot", UriKind.Relative), new { feedingSlotId = reloader.FeedSlots[1].Id })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<ProductionDispatchQueryService>();
            var materials = await service.ResolveLoadedMaterialsAsync(await db.Printers.SingleAsync(p => p.Id == printerId), CancellationToken.None);
            materials.Should().HaveCount(1);
            materials[0].ColorHex.Should().Be("#EEEEEE");
        }
    }

    [Fact]
    public async Task ConcurrentLoadAndRemoval_LeaveNoSpoolAssignedToArchivedUnit()
    {
        var (client, printerId, _) = await SetupAsync();
        var reloader = await AddAsync(client, printerId);
        var spoolId = await RegisterSpoolAsync(client, await CreateFilamentVariantAsync(client));
        var removal = client.DeleteAsync(new Uri($"/api/v1/feed-units/{reloader.Id}", UriKind.Relative));
        var load = client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spoolId}/load", UriKind.Relative), new { feedSlotId = reloader.FeedSlots[0].Id });
        (await removal).EnsureSuccessStatusCode();
        var loadResponse = await load;
        (loadResponse.IsSuccessStatusCode || loadResponse.StatusCode == (HttpStatusCode)422).Should().BeTrue();
        var spools = await client.GetFromJsonAsync<ListSpoolsResponse>(new Uri("/api/v1/spools", UriKind.Relative));
        spools!.Spools.Single(s => s.Id == spoolId).State.Should().Be("InStorage");
        spools.Spools.Single(s => s.Id == spoolId).FeedSlotId.Should().BeNull();
    }
}
