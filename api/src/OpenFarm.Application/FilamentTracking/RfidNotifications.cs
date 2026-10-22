using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// A tagged spool a printer reads that no active spool owns. Register prefills from it: the matched catalog colour, the tag's
/// weight and the grams the reading implies. When an untagged spool is loaded in that slot, Link ties the tag to it instead.
/// </summary>
public sealed record UnregisteredRfidSpool(
    Guid PrinterId, string PrinterName, Guid FeedSlotId, string SlotLabel, string RfidId,
    string? MaterialType, string? SubBrand, string? ColorHex, int? RemainPercent, int? RfidWeightGrams, decimal? RemainingGrams,
    VariantStock? Match, Guid? LoadedSpoolId, string? LoadedSpoolName);

/// <summary>
/// The RFID items of Action required, derived from what reachable printers read now, so they clear by themselves once the
/// tag is registered or linked, the tray changes, or the printer goes offline.
/// </summary>
public sealed class RfidNotifications(IPrinterRepository printers, IFeedUnitRepository feedUnits, ISpoolRepository spools)
{
    public async Task<IReadOnlyList<UnregisteredRfidSpool>> ListAsync(IReadOnlyList<VariantStock> colors, CancellationToken cancellationToken)
    {
        var items = new List<UnregisteredRfidSpool>();
        foreach (var printer in (await printers.ListAsync(cancellationToken)).Where(p => p.IsReachable))
        {
            var units = await feedUnits.GetByPrinterIdAsync(printer.Id, cancellationToken);
            var readings = RfidSpoolLinker.Readings(units, printer).Where(r => r.Tray.RfidId is not null).ToList();
            if (readings.Count == 0) continue;
            var owners = await spools.FindByRfidIdsAsync(
                readings.Select(r => r.Tray.RfidId!).Distinct().ToList(), cancellationToken, includeFinished: true);
            foreach (var (unit, slot, tray) in readings)
            {
                var owned = owners.Where(s => s.RfidId == tray.RfidId).ToList();
                if (owned.Any(s => s.State != SpoolState.Finished)) continue;
                // A finished spool's tag is usually that empty spool, still in the AMS: only a reading with filament on it is news.
                if (owned.Count > 0 && tray.RemainPercent is not (>= 1 and <= 100)) continue;
                var loaded = await spools.FindUsingFeedSlotAsync(slot.Id, cancellationToken);
                var linkable = loaded is { RfidId: null } ? loaded : null;
                items.Add(new(printer.Id, printer.Name, slot.Id, FeedUnitDisplay.SlotLabel(unit, slot, printer.Brand), tray.RfidId!,
                    tray.MaterialType, tray.SubBrand, tray.ColorHex, tray.RemainPercent is >= 1 and <= 100 ? tray.RemainPercent : null,
                    tray.RfidWeightGrams, tray.RfidWeightGrams is { } full ? SpoolReading.Grams(tray.RemainPercent, full) : null,
                    RfidVariantMatch.Find(colors, tray), linkable?.Id, linkable?.Label));
            }
        }

        return items;
    }
}
