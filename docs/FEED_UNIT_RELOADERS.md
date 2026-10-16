# Reloader feed paths and slots

A reloader represents a single feed path with independently registered spool positions. The Add dialog defaults to two slots and replacing an available external spool port. Configure existing units to choose their replacement and physical feeding slot. Names and slot counts remain fixed after creation.

When configuring a replacement, move the external port's registered spool to the selected slot (the default), or return it to `InStorage`. Occupied destinations are rejected with 409. Configuration hides the external port; it can still be explicitly revealed. Only one active reloader may replace a port on the same printer.

`FeedUnit.FeedingSlotId` identifies the selected slot; `FeedingSelectionSource` is `Operator`, `Estimated`, or null when unset. Selection can represent unregistered filament. Printer activity is separate: a reachable, active external port supplies the selected slot's live activity and detection, labeled as printer-port detection. Standby slots receive no port detection. The raw connector snapshot remains attached to the external port, and a revealed replaced port does not duplicate the reloader's detection.

Recorded depletion finishes the selected spool and selects a loaded, active sibling with positive remaining weight as estimated continuation. Two-slot units can continue in either direction. Material mismatch retains the existing warning. Standby depletion does not change selection; no available sibling and unloading the feeding spool clear it. Manual usage and production consumption share this resolver. Consumption remains explicitly allocated by spool; estimates do not measure physical switches or automatically split grams across slots. Production material readiness considers only the selected reloader slot.

Remove atomically returns registered spools to `InStorage`, preserves remaining weight and usage history, deactivates slots, clears selection, and sets `ArchivedAtUtc`. Archived reloaders disappear from ordinary listings/loading pickers and no longer supersede the external port. Removing again is safe. Re-adding creates new rows; archived names may be reused.

## API

- `GET /api/v1/printers/{printerId}/feed-units`: optional `includeHidden` and `includeArchived` query flags, both false by default. Configuration requests hidden ports without including archived units.
- `POST /api/v1/printers/{printerId}/feed-units`: existing reloader fields plus optional `startingSlotPosition` (zero-based) and `transferExternalSpool` (default true). The UI always supplies the starting slot; older clients omitting it create an unset selection. Slot count is 1–100.
- `PUT /api/v1/feed-units/{id}/configuration`: `{ replacesFeedUnitId, feedingSlotId, transferExternalSpool }`. Null replacement means an independent path; null selection means unknown. This is a full configuration update.
- `PUT /api/v1/feed-units/{id}/feeding-channel`: `{ feedingSlotId }`, nullable to clear selection. Records operator selection without moving inventory.
- Feed-unit responses add `archivedAtUtc`, `feedingSlotId`, and `feedingSelectionSource`. Creation responses return selection state.
- Printer detail and `PrinterTelemetryUpdated` add `externalSpoolTray`. Live readings update the detail cache; only changes in external-port presence or AMS topology trigger topology refetches.
- New mutations use `feed-units:configure` permission and activity logging; `FeedUnitsChanged` invalidates feed-unit and spool caches after persistence.

Feed-path creation, configuration, selection, loading, and removal use database transactions and a per-printer advisory lock. Replacement claims have a partial unique index; feed units also use PostgreSQL `xmin` concurrency checks.

## Migration

`ConfigureReloaderChannels` adds selection and archive columns. Fully deactivated legacy reloaders become archived, and their loaded spools return to inventory. Active units retain unknown selection until configured. If legacy data contains duplicate replacement claims, the earliest-created active unit retains the claim; other units retain their inventory but become independent paths. No rows or usage history are deleted.

This document supersedes the original spec 006 rules that kept removed reloaders visible and assigned no telemetry to their slots.
