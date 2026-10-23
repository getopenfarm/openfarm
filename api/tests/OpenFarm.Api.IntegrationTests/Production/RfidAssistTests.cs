using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Notifications;
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

    /// <summary>What an AMS slot reads: a tagged PLA Matte spool, nearly black, 90% left; or empty when <paramref name="tag"/> is null.</summary>
    private static AmsTray TrayOf(int slot, string? tag, int remain = 90) => tag is null
        ? new AmsTray(slot, true, null, null, null, null, false)
        : new AmsTray(slot, false, "PLA", "080808FF", "PLA Matte", remain, false, tag, 1000);

    /// <summary>Records what the AMS reads in each listed slot (null: empty), then runs the linker as a telemetry flush would.</summary>
    private Task ReadAsync(Guid printerId, params (int Slot, string? Tag)[] trays) =>
        ReadTraysAsync(printerId, trays.Select(t => TrayOf(t.Slot, t.Tag)).ToArray());

    private async Task ReadTraysAsync(Guid printerId, params AmsTray[] trays)
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var printer = await db.Printers.SingleAsync(p => p.Id == printerId);
            printer.RecordTelemetry(null, null, null, null, null, null, [new AmsUnit(0, trays)], null);
            await db.SaveChangesAsync();
        }

        using var linkScope = Factory.Services.CreateScope();
        var reloaded = await linkScope.ServiceProvider.GetRequiredService<IPrinterRepository>().GetByIdAsync(printerId, default);
        await linkScope.ServiceProvider.GetRequiredService<RfidSpoolLinker>().LinkAsync(reloaded!, default);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, Guid variantId, string? tag) =>
        await client.PostAsJsonAsync("/api/v1/spools", new { lines = new[] { new { filamentVariantId = variantId, spoolWeightGrams = 1000 } }, rfidId = tag });

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

        var twoOfThem = await client.PostAsJsonAsync("/api/v1/spools",
            new { lines = new[] { new { filamentVariantId = variant, spoolWeightGrams = 1000, quantity = 2 } }, rfidId = NewTag() });
        var twoLines = await client.PostAsJsonAsync("/api/v1/spools",
            new
            {
                lines = new[] { new { filamentVariantId = variant, spoolWeightGrams = 1000, quantity = 1 }, new { filamentVariantId = variant, spoolWeightGrams = 1000, quantity = 1 } },
                rfidId = NewTag(),
            });

        twoOfThem.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        twoLines.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<ListSpoolsResponse>($"/api/v1/spools?filamentVariantId={variant}"))!.Spools.Should().BeEmpty();
    }

    private static async Task<List<UnregisteredRfidSpoolResponse>> UnregisteredAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListNotificationsResponse>("/api/v1/notifications"))!.RfidSpools;

    [Fact]
    public async Task AnUnregisteredTag_IsListed_WithItsLikelyColorAndTheGramsTheReadingImplies()
    {
        var client = await ClientAsync();
        var charcoal = await CreateFilamentVariantAsync(client, "Charcoal", "#000000");
        await RenameFilamentAsync(charcoal, "PLA Matte");
        var (printerId, slots) = await AmsPrinterAsync();
        var tag = NewTag();

        await ReadAsync(printerId, (0, tag));

        var item = (await UnregisteredAsync(client)).Single(i => i.RfidId == tag);
        item.PrinterId.Should().Be(printerId);
        item.FeedSlotId.Should().Be(slots[0]);
        item.SlotLabel.Should().Be("Multi-Material Unit 1 · Slot 1"); // a simulator, not a Bambu, so no "AMS"
        item.RemainPercent.Should().Be(90);
        item.RfidWeightGrams.Should().Be(1000);
        item.RemainingGrams.Should().Be(900);
        item.FilamentVariantId.Should().Be(charcoal);
        item.VariantName.Should().Be("PLA Matte Charcoal");
        item.LoadedSpoolId.Should().BeNull();

        await RegisteredAsync(client, charcoal, tag);
        (await UnregisteredAsync(client)).Should().NotContain(i => i.RfidId == tag);
    }

    [Fact]
    public async Task AnUntaggedSpoolInTheSlot_IsOfferedForLinking()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);
        var (printerId, slots) = await AmsPrinterAsync();
        var untagged = await RegisteredAsync(client, variant);
        await client.PutAsJsonAsync($"/api/v1/spools/{untagged}/load", new { feedSlotId = slots[1] });
        var tag = NewTag();

        await ReadAsync(printerId, (1, tag));

        (await UnregisteredAsync(client)).Single(i => i.RfidId == tag).LoadedSpoolId.Should().Be(untagged);
    }

    [Fact]
    public async Task AnOfflinePrinter_ListsNothing()
    {
        var client = await ClientAsync();
        var (printerId, _) = await AmsPrinterAsync();
        var tag = NewTag();
        await ReadAsync(printerId, (0, tag));
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Printers.SingleAsync(p => p.Id == printerId)).MarkUnreachable();
            await db.SaveChangesAsync();
        }

        (await UnregisteredAsync(client)).Should().NotContain(i => i.RfidId == tag);
    }

    [Fact]
    public async Task AFinishedSpoolsTag_IsNews_OnlyWithFilamentOnIt()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);
        var (printerId, _) = await AmsPrinterAsync();
        var tag = NewTag();
        var spool = await RegisteredAsync(client, variant, tag);
        await client.PutAsJsonAsync($"/api/v1/spools/{spool}/unload", new { action = "MarkFinished" });

        await ReadTraysAsync(printerId, TrayOf(0, tag, remain: 0));
        (await UnregisteredAsync(client)).Should().NotContain(i => i.RfidId == tag);

        await ReadTraysAsync(printerId, TrayOf(0, tag, remain: 100));
        (await UnregisteredAsync(client)).Should().Contain(i => i.RfidId == tag);
    }

    private async Task RenameFilamentAsync(Guid variantId, string material)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var variant = await db.FilamentVariants.SingleAsync(v => v.Id == variantId);
        var filament = await db.Filaments.SingleAsync(f => f.Id == variant.FilamentId);
        filament.UpdateDetails(filament.VendorId, material, filament.MaterialType);
        await db.SaveChangesAsync();
    }

    private static async Task<List<RfidWeightDriftResponse>> DriftAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListNotificationsResponse>("/api/v1/notifications"))!.RfidDrift;

    [Fact]
    public async Task AReadingWellOffTheRecordedWeight_IsListed_AndCorrectingItWritesAnAmsReadingEntry()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);
        var (printerId, slots) = await AmsPrinterAsync();
        var tag = NewTag();
        var spool = await RegisteredAsync(client, variant, tag);

        await ReadTraysAsync(printerId, TrayOf(0, tag, remain: 42));

        var drift = (await DriftAsync(client)).Single(d => d.SpoolId == spool);
        drift.FeedSlotId.Should().Be(slots[0]);
        drift.InventoryGrams.Should().Be(1000);
        drift.ReadGrams.Should().Be(420);
        (drift.Material, drift.ColorName).Should().Be(("PLA Basic", "Black"));

        (await client.PutAsJsonAsync($"/api/v1/spools/{spool}", new { remainingWeightGrams = 420, reason = "AMS reading" }))
            .EnsureSuccessStatusCode();
        (await DriftAsync(client)).Should().NotContain(d => d.SpoolId == spool);
        var latest = (await client.GetFromJsonAsync<SpoolHistoryResponse>($"/api/v1/spools/{spool}/history"))!.Entries[0];
        latest.Source.Should().Be("Correction");
        latest.Reason.Should().Be("AMS reading");
        latest.RemainingAfterGrams.Should().Be(420);
    }

    [Fact]
    public async Task WhileThePrinterPrints_DriftIsNotListed()
    {
        var client = await ClientAsync();
        var variant = await CreateFilamentVariantAsync(client);
        var (printerId, _) = await AmsPrinterAsync();
        var tag = NewTag();
        var spool = await RegisteredAsync(client, variant, tag);
        await ReadTraysAsync(printerId, TrayOf(0, tag, remain: 42));
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Printers.SingleAsync(p => p.Id == printerId)).ObserveStatus("RUNNING");
            await db.SaveChangesAsync();
        }

        (await DriftAsync(client)).Should().NotContain(d => d.SpoolId == spool);
    }
}
