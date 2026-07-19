using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Diffs currently-stored connector-derived <see cref="FeedUnit"/>/<see cref="FeedSlot"/> rows
/// against a printer's live-reported topology — upserts new units/slots, soft-deactivates ones no
/// longer reported, reactivates ones that reappear. Only persists when the diff is non-empty
/// (Constitution Principle V). Never touches <see cref="FeedUnitKindCatalog.AutomaticFilamentReloader"/>
/// rows (research.md §6).
/// </summary>
public sealed class FeedSlotTelemetrySyncHandler(IFeedUnitRepository feedUnitRepository) : IFeedSlotTelemetrySync
{
    public async Task SyncAsync(
        Guid printerId,
        Guid organizationId,
        string brand,
        IReadOnlyList<AmsUnit> amsUnits,
        AmsTray? externalSpoolTray,
        CancellationToken cancellationToken)
    {
        var existingUnits = (await feedUnitRepository.GetByPrinterIdAsync(printerId, cancellationToken))
            .Where(u => u.Kind is FeedUnitKindCatalog.MultiMaterialUnit or FeedUnitKindCatalog.ExternalSpool)
            .ToList();

        var hasChanges = false;

        foreach (var amsUnit in amsUnits)
        {
            var existing = existingUnits.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit && u.UnitIndex == amsUnit.UnitIndex);
            if (existing is null)
            {
                var unit = FeedUnit.CreateConnectorDerived(organizationId, printerId, FeedUnitKindCatalog.MultiMaterialUnit, amsUnit.UnitIndex);
                foreach (var tray in amsUnit.Trays)
                {
                    unit.AddFeedSlot(tray.SlotIndex);
                }

                await feedUnitRepository.AddAsync(unit, cancellationToken);
                continue;
            }

            hasChanges |= ReconcileSlots(existing, amsUnit.Trays.Select(t => t.SlotIndex));
        }

        foreach (var existing in existingUnits.Where(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit && amsUnits.All(a => a.UnitIndex != u.UnitIndex)))
        {
            hasChanges |= DeactivateAll(existing);
        }

        var externalSpoolUnit = existingUnits.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.ExternalSpool);
        if (externalSpoolTray is not null)
        {
            if (externalSpoolUnit is null)
            {
                var unit = FeedUnit.CreateConnectorDerived(organizationId, printerId, FeedUnitKindCatalog.ExternalSpool, unitIndex: 0);
                unit.AddFeedSlot(0);
                await feedUnitRepository.AddAsync(unit, cancellationToken);
            }
            else
            {
                hasChanges |= ReconcileSlots(externalSpoolUnit, [0]);
            }
        }
        else if (externalSpoolUnit is not null)
        {
            hasChanges |= DeactivateAll(externalSpoolUnit);
        }

        if (hasChanges)
        {
            await feedUnitRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private bool ReconcileSlots(FeedUnit unit, IEnumerable<int> reportedSlotPositions)
    {
        var reported = reportedSlotPositions.ToHashSet();
        var changed = false;

        foreach (var position in reported)
        {
            var slot = unit.FeedSlots.FirstOrDefault(s => s.SlotPosition == position);
            if (slot is null)
            {
                // TrackNewFeedSlot, not plain graph fix-up: unit is already tracked/Unchanged, and a
                // new child discovered only via its navigation collection is misdetected as Modified
                // by EF Core (client-generated Guid key already non-default) — see IFeedUnitRepository.
                feedUnitRepository.TrackNewFeedSlot(unit.AddFeedSlot(position));
                changed = true;
            }
            else if (slot.DeactivatedAtUtc is not null)
            {
                slot.Reactivate();
                changed = true;
            }
        }

        foreach (var slot in unit.FeedSlots.Where(s => s.DeactivatedAtUtc is null && !reported.Contains(s.SlotPosition)))
        {
            slot.Deactivate();
            changed = true;
        }

        return changed;
    }

    private static bool DeactivateAll(FeedUnit unit)
    {
        var changed = false;
        foreach (var slot in unit.FeedSlots.Where(s => s.DeactivatedAtUtc is null))
        {
            slot.Deactivate();
            changed = true;
        }

        return changed;
    }
}
