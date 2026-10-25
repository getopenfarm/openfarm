using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Diffs currently-stored connector-derived <see cref="FeedUnit"/>/<see cref="FeedSlot"/> rows
/// against a printer's live-reported topology — upserts new units/slots, follows the extruder each
/// slot feeds, soft-deactivates ones no longer reported, reactivates ones that reappear. A unit kind
/// the readings don't cover is left alone: its report hasn't arrived yet. Only persists when the diff
/// is non-empty (Constitution Principle V). Never touches
/// <see cref="FeedUnitKindCatalog.AutomaticFilamentReloader"/> rows (research.md §6).
/// </summary>
public sealed class FeedSlotTelemetrySyncHandler(IFeedUnitRepository feedUnitRepository) : IFeedSlotTelemetrySync
{
    // One transaction, so a reader never sees a half-synced topology (an External Spool before the Multi-Material Unit
    // that supersedes it), and concurrent syncs of one printer queue up instead of colliding on the unique unit index.
    public Task SyncAsync(Guid printerId, IReadOnlyList<FeedUnitReading> readings, CancellationToken cancellationToken) =>
        feedUnitRepository.ExecuteAtomicallyAsync(async () =>
        {
            await SyncCoreAsync(printerId, readings, cancellationToken);
            return true;
        }, cancellationToken, printerId);

    private async Task SyncCoreAsync(Guid printerId, IReadOnlyList<FeedUnitReading> readings, CancellationToken cancellationToken)
    {
        var reportedKinds = readings.Select(r => r.Kind).ToHashSet();
        var existingUnits = (await feedUnitRepository.GetByPrinterIdAsync(printerId, cancellationToken))
            .Where(u => u.Kind is FeedUnitKindCatalog.MultiMaterialUnit or FeedUnitKindCatalog.ExternalSpool && reportedKinds.Contains(u.Kind))
            .ToList();

        var hasChanges = false;

        foreach (var reading in readings)
        {
            var existing = existingUnits.FirstOrDefault(u => u.Kind == reading.Kind && u.UnitIndex == reading.UnitIndex);
            if (existing is null)
            {
                var unit = FeedUnit.CreateConnectorDerived(printerId, reading.Kind, reading.UnitIndex);
                foreach (var slot in reading.Slots)
                {
                    unit.AddFeedSlot(slot.SlotPosition, slot.ExtruderIndex);
                }

                await feedUnitRepository.AddAsync(unit, cancellationToken);
                continue;
            }

            hasChanges |= ReconcileSlots(existing, reading.Slots);
        }

        foreach (var existing in existingUnits.Where(u => !readings.Any(r => r.Kind == u.Kind && r.UnitIndex == u.UnitIndex)))
        {
            hasChanges |= DeactivateAll(existing);
        }

        if (hasChanges)
        {
            await feedUnitRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private bool ReconcileSlots(FeedUnit unit, IReadOnlyList<FeedSlotReading> reportedSlots)
    {
        var changed = false;

        foreach (var reported in reportedSlots)
        {
            var slot = unit.FeedSlots.FirstOrDefault(s => s.SlotPosition == reported.SlotPosition);
            if (slot is null)
            {
                // TrackNewFeedSlot, not plain graph fix-up: unit is already tracked/Unchanged, and a
                // new child discovered only via its navigation collection is misdetected as Modified
                // by EF Core (client-generated Guid key already non-default) — see IFeedUnitRepository.
                feedUnitRepository.TrackNewFeedSlot(unit.AddFeedSlot(reported.SlotPosition, reported.ExtruderIndex));
                changed = true;
                continue;
            }

            if (slot.DeactivatedAtUtc is not null)
            {
                slot.Reactivate();
                changed = true;
            }

            changed |= slot.AssignExtruder(reported.ExtruderIndex);
        }

        foreach (var slot in unit.FeedSlots.Where(s => s.DeactivatedAtUtc is null && reportedSlots.All(r => r.SlotPosition != s.SlotPosition)))
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
