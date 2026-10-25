using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Reports;
using OpenFarm.Application.Production;
using OpenFarm.Application.Reports;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

public class StatsReportTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static string Range(DateTimeOffset from, DateTimeOffset to, string zone = "UTC") =>
        $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&timeZone={Uri.EscapeDataString(zone)}";

    [Fact]
    public async Task Stats_CountTheEndedPrints_TheirFailureCodes_AndMatchTheMaterialsReport()
    {
        var client = await ClientAsync();
        var completed = await ExternalPrintAsync(grams: 20);
        await ReplayAsync(completed, "FINISH");
        var failed = await ExternalPrintAsync(grams: 40);
        await ChangePrinterAsync(failed, p =>
        {
            p.RecordTelemetry(null, null, 50, null, null, null, []);
            p.ObserveAlerts(null, [new PrinterAlert("PrintError", "0300_8004", "Error", "Filament runout")]);
        });
        await ReplayAsync(failed, "FAILED");
        await ExternalPrintAsync(grams: 10); // still printing: not a print yet, and no hours of its own

        var today = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var range = Range(today, today.AddDays(1));
        var stats = (await client.GetFromJsonAsync<StatsReport>($"/api/v1/reports/stats?{range}"))!;
        var materials = (await client.GetFromJsonAsync<MaterialsReportResponse>(
            $"/api/v1/reports/materials?from={Uri.EscapeDataString(today.ToString("O"))}&to={Uri.EscapeDataString(today.AddDays(1).ToString("O"))}"))!;

        stats.Current.Should().BeEquivalentTo(new { Prints = 2, Completed = 1, Failed = 1, Cancelled = 0, UsedGrams = 20m, WasteGrams = 20m });
        stats.Current.UsedGrams.Should().Be(materials.Total.UsedGrams);
        stats.Current.WasteGrams.Should().Be(materials.Total.WasteGrams);
        stats.Days.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Completed = 1, Failed = 1, UsedGrams = 20m, WasteGrams = 20m });
        stats.FailureCodes.Should().ContainSingle().Which.Should().Be(new FailureCode("0300_8004", "Filament runout", 1));
        stats.Printers.Select(p => p.PrinterId).Should().Contain([completed, failed]);
        stats.Current.Activity.Observed.Should().BeGreaterThan(0, "every printer's time is tracked since it was added");
        stats.Previous.Prints.Should().Be(0);
    }

    [Fact]
    public async Task ARangeOverTheDstChange_HasOneEntryPerLocalDay()
    {
        var client = await ClientAsync();

        // Oct 24 and Oct 25 in Berlin; the clocks go back on Oct 25.
        var stats = await client.GetFromJsonAsync<StatsReport>(
            $"/api/v1/reports/stats?{Range(new(2026, 10, 23, 22, 0, 0, TimeSpan.Zero), new(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), "Europe/Berlin")}");

        stats!.Days.Select(d => d.Day).Should().Equal(new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25));
    }

    [Theory]
    [InlineData("W. Europe Standard Time")]
    [InlineData("Not/AZone")]
    public async Task AZoneTheDatabaseCantUse_IsRejected(string zone)
    {
        var client = await ClientAsync();
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);

        var response = await client.GetAsync($"/api/v1/reports/stats?{Range(today, today.AddDays(1), zone)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnEmptyOrOverlongRange_IsRejected()
    {
        var client = await ClientAsync();
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);

        (await client.GetAsync($"/api/v1/reports/stats?{Range(today, today)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/v1/reports/stats?{Range(today.AddDays(-400), today)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>A printer-started print of <paramref name="grams"/> on a fresh printer, drawing from its loaded spool; returns the printer.</summary>
    private async Task<Guid> ExternalPrintAsync(decimal grams)
    {
        var printer = await PrinterAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var slotIds = await db.FeedUnits.Where(u => u.PrinterId == printer.Id).SelectMany(u => u.FeedSlots).Select(s => s.Id).ToListAsync();
        var spool = await db.Spools.SingleAsync(s => s.FeedSlotId != null && slotIds.Contains(s.FeedSlotId.Value));
        var print = ExternalPrint.Start(printer.Id, "benchy", [new ExternalFilamentSpec(0, "PLA", "000000", grams, null)]);
        print.ObserveSlot(new FeedSlotRef(FeedUnitKindCatalog.MultiMaterialUnit, 0, 0), spool.Id);
        var repository = scope.ServiceProvider.GetRequiredService<IExternalPrintRepository>();
        await repository.AddAsync(print, default);
        await repository.SaveChangesAsync(default);
        return printer.Id;
    }

    private async Task ChangePrinterAsync(Guid printerId, Action<Printer> change)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        change(await db.Printers.SingleAsync(p => p.Id == printerId));
        await db.SaveChangesAsync();
    }
}
