using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

/// <summary>A multi-colour print the printer started: tools pair with trays by material and colour, whatever trays OpenFarm saw.</summary>
public class ExternalPrintMultiColourTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task ATwoColourPrintJoinedMidPrint_DeductsEachToolFromTheTrayHoldingItsColour()
    {
        await ClientAsync();
        var printer = await PrinterAsync();
        Guid[] spools;
        Guid printId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var variantId = (await db.Spools.FirstAsync()).FilamentVariantId;
            var unit = FeedUnit.CreateConnectorDerived(printer.Id, FeedUnitKindCatalog.MultiMaterialUnit, 0);
            var slots = Enumerable.Range(0, 3).Select(position => unit.AddFeedSlot(position)).ToList();
            db.FeedUnits.Add(unit);
            var loaded = slots.Select(s => { var spool = Spool.Create(variantId, null, 1000, 1000); spool.LoadOntoFeedSlot(s.Id); return spool; }).ToArray();
            db.Spools.AddRange(loaded);
            spools = loaded.Select(s => s.Id).ToArray();

            var live = await db.Printers.SingleAsync(p => p.Id == printer.Id);
            // Red, white and black PETG in trays 0-2, as the printer reports them (RRGGBBAA).
            live.RecordTelemetry(null, null, null, null, null, null,
                [new FeedUnitReading(FeedUnitKindCatalog.MultiMaterialUnit, 0, [new FeedSlotReading(0, false, "PETG", "F72323FF", null, null, false),
                    new FeedSlotReading(1, false, "PETG", "FFFFFFFF", null, null, false), new FeedSlotReading(2, false, "PETG", "000000FF", null, null, false)])]);
            // Black (tool 0) printed before OpenFarm joined: only red's tray was ever seen.
            var print = ExternalPrint.Start(printer.Id, "riser",
                [new ExternalFilamentSpec(0, "PETG", "#000000", 7m, null), new ExternalFilamentSpec(1, "PETG", "#FB001C", 24m, null)]);
            print.ObserveSlot(new FeedSlotRef(FeedUnitKindCatalog.MultiMaterialUnit, 0, 0), null);
            await scope.ServiceProvider.GetRequiredService<IExternalPrintRepository>().AddAsync(print, default);
            await db.SaveChangesAsync();
            printId = print.Id;
        }

        await ReplayAsync(printer.Id, "FINISH");

        using var check = Factory.Services.CreateScope();
        var verify = check.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var lines = await verify.PrintFilaments.Where(f => f.PrintId == printId).OrderBy(f => f.ToolIndex).ToListAsync();
        lines.Select(l => l.SpoolId).Should().Equal(spools[2], spools[0]);
        lines.Should().OnlyContain(l => l.SkippedReason == null);
        (await verify.Spools.SingleAsync(s => s.Id == spools[2])).RemainingWeightGrams.Should().Be(993m);
        (await verify.Spools.SingleAsync(s => s.Id == spools[0])).RemainingWeightGrams.Should().Be(976m);
    }
}
