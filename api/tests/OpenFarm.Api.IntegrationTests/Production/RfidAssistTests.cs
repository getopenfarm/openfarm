using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Persistence;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.Production;

// Test-after: the RFID spool linker, driven directly with AMS readings, and registering or linking a tag.
public class RfidAssistTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static string NewTag() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    /// <summary>A printer with a four-slot AMS; returns the AMS slot ids in slot order.</summary>
    private async Task<(Guid PrinterId, Guid[] Slots)> AmsPrinterAsync()
    {
        var printer = await PrinterAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var ams = FeedUnit.CreateConnectorDerived(printer.Id, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var slots = Enumerable.Range(0, 4).Select(ams.AddFeedSlot).ToArray();
        db.FeedUnits.Add(ams);
        await db.SaveChangesAsync();
        return (printer.Id, slots.Select(s => s.Id).ToArray());
    }

    /// <summary>Records what the AMS reads in each listed slot (null: empty), then runs the linker as a telemetry flush would.</summary>
    private async Task ReadAsync(Guid printerId, params (int Slot, string? Tag)[] trays)
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var printer = await db.Printers.SingleAsync(p => p.Id == printerId);
            printer.RecordTelemetry(null, null, null, null, null, null,
                [new AmsUnit(0, trays.Select(t => new AmsTray(t.Slot, t.Tag is null, t.Tag is null ? null : "PLA",
                    t.Tag is null ? null : "000000FF", null, 90, false, t.Tag, t.Tag is null ? null : 1000)).ToList())], null);
            await db.SaveChangesAsync();
        }

        using var linkScope = Factory.Services.CreateScope();
        var reloaded = await linkScope.ServiceProvider.GetRequiredService<IPrinterRepository>().GetByIdAsync(printerId, default);
        await linkScope.ServiceProvider.GetRequiredService<RfidSpoolLinker>().LinkAsync(reloaded!, default);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, Guid variantId, string? tag) =>
        await client.PostAsJsonAsync("/api/v1/spools", new { filamentVariantId = variantId, spoolWeightGrams = 1000, rfidId = tag });

    private static async Task<Guid> RegisteredAsync(HttpClient client, Guid variantId, string? tag = null)
    {
        var response = await RegisterAsync(client, variantId, tag);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RegisterSpoolsResponse>())!.Spools.Single().Id;
    }

    private static async Task<SpoolResponse> SpoolAsync(HttpClient client, Guid spoolId) =>
        (await client.GetFromJsonAsync<SpoolResponse>($"/api/v1/spools/{spoolId}"))!;

    [Fact]
    public async Task ARegisteredTagRead_LoadsItsSpool_AndReturnsTheSlotsSpoolToStorage()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);
        var (printerId, slots) = await AmsPrinterAsync();
        var untagged = await RegisteredAsync(client, variant);
        await client.PutAsJsonAsync($"/api/v1/spools/{untagged}/load", new { feedSlotId = slots[0] });
        var tag = NewTag();
        var tagged = await RegisteredAsync(client, variant, tag);

        await ReadAsync(printerId, (0, tag));

        (await SpoolAsync(client, tagged)).FeedSlotId.Should().Be(slots[0]);
        (await SpoolAsync(client, untagged)).State.Should().Be("InStorage");
        (await ActivityEventTypesAsync(client, tagged)).Should().Contain("SpoolLoadedFromRfid");
    }

    [Fact]
    public async Task AManualUnload_IsNotUndoneWhileTheTagIsStillRead_ButReinsertingLoadsItAgain()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);
        var (printerId, slots) = await AmsPrinterAsync();
        var tag = NewTag();
        var spool = await RegisteredAsync(client, variant, tag);
        await ReadAsync(printerId, (0, tag));
        await client.PutAsJsonAsync($"/api/v1/spools/{spool}/unload", new { action = "ReturnToStorage" });

        await ReadAsync(printerId, (0, tag));
        (await SpoolAsync(client, spool)).State.Should().Be("InStorage");

        await ReadAsync(printerId, (0, null));
        await ReadAsync(printerId, (0, tag));
        (await SpoolAsync(client, spool)).FeedSlotId.Should().Be(slots[0]);
    }

    [Fact]
    public async Task ATagReadInAnotherSlot_MovesTheSpool_AndOneReadInTwoSlotsLoadsNeither()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);
        var (printerId, slots) = await AmsPrinterAsync();
        var tag = NewTag();
        var spool = await RegisteredAsync(client, variant, tag);
        await ReadAsync(printerId, (0, tag));

        await ReadAsync(printerId, (0, null), (1, tag));
        (await SpoolAsync(client, spool)).FeedSlotId.Should().Be(slots[1]);

        var other = NewTag();
        var twice = await RegisteredAsync(client, variant, other);
        await ReadAsync(printerId, (2, other), (3, other));
        (await SpoolAsync(client, twice)).State.Should().Be("InStorage");
    }

    [Fact]
    public async Task AFinishedSpoolsTag_LoadsNothing_AndCanBeRegisteredAgain_ButThenTheOldSpoolCannotBeRestored()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);
        var (printerId, _) = await AmsPrinterAsync();
        var tag = NewTag();
        var finished = await RegisteredAsync(client, variant, tag);
        await client.PutAsJsonAsync($"/api/v1/spools/{finished}/unload", new { action = "MarkFinished" });

        await ReadAsync(printerId, (0, tag));
        (await SpoolAsync(client, finished)).State.Should().Be("Finished");

        var refill = await RegisteredAsync(client, variant, tag);
        refill.Should().NotBe(finished);
        (await client.PutAsJsonAsync($"/api/v1/spools/{finished}/restore", new { })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ATagCanBelongToOneActiveSpoolOnly_WhetherRegisteredOrLinked()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);
        var tag = NewTag();
        await RegisteredAsync(client, variant, tag);

        (await RegisterAsync(client, variant, tag)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var untagged = await RegisteredAsync(client, variant);
        (await client.PutAsJsonAsync($"/api/v1/spools/{untagged}/rfid", new { rfidId = tag })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var fresh = NewTag();
        (await client.PutAsJsonAsync($"/api/v1/spools/{untagged}/rfid", new { rfidId = fresh })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/v1/spools/{untagged}/rfid", new { rfidId = NewTag() })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ATaggedSpoolIsAddedOneAtATime()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/spools",
            new { filamentVariantId = variant, spoolWeightGrams = 1000, quantity = 2, rfidId = NewTag() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
