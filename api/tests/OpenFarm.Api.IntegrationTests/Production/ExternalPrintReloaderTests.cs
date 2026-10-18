using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

/// <summary>A reloader that replaces the external port feeds tray 254, so a print on that tray draws from the reloader's feeding spool.</summary>
public class ExternalPrintReloaderTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private const int ExternalTray = 254;

    /// <summary>The printer's one spool moves from the external slot to a reloader that replaces it; no feeding slot is chosen yet.</summary>
    private async Task<(Guid PrinterId, Guid SpoolId, Guid ReloaderSlotId, Guid ReloaderId)> PrinterWithReloaderAsync()
    {
        var printer = await PrinterAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var external = await db.FeedUnits.Include(u => u.FeedSlots).SingleAsync(u => u.PrinterId == printer.Id && u.Kind == FeedUnitKindCatalog.ExternalSpool);
        var spool = await db.Spools.SingleAsync(s => s.FeedSlotId == external.FeedSlots[0].Id);
        spool.ReturnToStorage();
        var reloader = FeedUnit.CreateAutomaticFilamentReloader(printer.Id, 0, "AFR", external.Id);
        var slot = reloader.AddFeedSlot(0);
        db.FeedUnits.Add(reloader);
        await db.SaveChangesAsync();
        spool.LoadOntoFeedSlot(slot.Id);
        await db.SaveChangesAsync();
        return (printer.Id, spool.Id, slot.Id, reloader.Id);
    }

    private async Task<Guid> StartExternalAsync(Guid printerId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var printer = await db.Printers.SingleAsync(p => p.Id == printerId);
        var print = ExternalPrint.Start(printerId, "benchy", [new ExternalFilamentSpec(0, "PLA", "000000", 20m, null)]);
        var repository = scope.ServiceProvider.GetRequiredService<IExternalPrintRepository>();
        await repository.AddAsync(print, default);
        await repository.SaveChangesAsync(default);
        return print.Id;
    }

    private async Task ObserveTelemetryAsync(Guid printerId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var printer = await db.Printers.SingleAsync(p => p.Id == printerId);
        printer.ObserveReportedState("RUNNING", DateTimeOffset.UtcNow);
        printer.RecordTelemetry(null, null, 50, null, null, "benchy", [], null, 50, 100, ExternalTray, "benchy.gcode.3mf");
        await scope.ServiceProvider.GetRequiredService<ExternalPrintSync>().ObserveTelemetryAsync(printer, default);
    }

    [Fact]
    public async Task APrintOnTheExternalTray_DeductsFromTheSpoolTheReloaderIsFeeding()
    {
        await ClientAsync();
        var (printerId, spoolId, slotId, reloaderId) = await PrinterWithReloaderAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.FeedUnits.Include(u => u.FeedSlots).SingleAsync(u => u.Id == reloaderId)).SelectFeedingSlot(slotId);
            await db.SaveChangesAsync();
        }

        var printId = await StartExternalAsync(printerId);
        await ObserveTelemetryAsync(printerId);
        await ReplayAsync(printerId, "FINISH");

        using var check = Factory.Services.CreateScope();
        var verify = check.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        (await verify.Spools.SingleAsync(s => s.Id == spoolId)).RemainingWeightGrams.Should().Be(980m);
        (await verify.PrintFilaments.SingleAsync(f => f.PrintId == printId)).SkippedReason.Should().BeNull();
    }

    [Fact]
    public async Task ATrayFirstSeenBeforeAFeedingSlotWasChosen_TakesTheSpoolOnceItIs()
    {
        await ClientAsync();
        var (printerId, spoolId, slotId, reloaderId) = await PrinterWithReloaderAsync();
        var printId = await StartExternalAsync(printerId);

        await ObserveTelemetryAsync(printerId);
        using (var scope = Factory.Services.CreateScope())
        {
            var open = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().ExternalPrints.SingleAsync(p => p.Id == printId);
            // No feeding slot is chosen yet, so the spool is unknown.
            open.ObservedSpoolIds.Should().Equal(Guid.Empty);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.FeedUnits.Include(u => u.FeedSlots).SingleAsync(u => u.Id == reloaderId)).SelectFeedingSlot(slotId);
            await db.SaveChangesAsync();
        }

        await ObserveTelemetryAsync(printerId);
        await ReplayAsync(printerId, "FINISH");

        using var check = Factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Spools.SingleAsync(s => s.Id == spoolId))
            .RemainingWeightGrams.Should().Be(980m);
    }
}
