# Phase 3 Quickstart: Filament Tracking

Supersedes the previous version — Feed Slots are now grouped under Feed Units, and automatic continuation
is driven by each Feed Unit's kind rather than a per-slot toggle. See `data-model.md` for entity shapes and
`contracts/filament-tracking-api.md` for exact request/response bodies.

## Prerequisites

```bash
cp .env.example infra/.env
cd infra && docker compose up   # postgres + api (applies AddFilamentTracking + AddExternalSpoolTelemetry) + web
curl -f http://localhost:8888/health/ready
```

Log in as the seeded operator and keep the JWT. Register the virtual/simulator printer (or a real Bambu
printer) and note its id — its connection loop starts automatically, populating its connector-derived Feed
Units within seconds.

## Scenario 1 — Build the catalog (User Story 1) — unchanged

```bash
curl -s -X POST http://localhost:8888/api/v1/vendors -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"name": "Bambu Lab"}' | tee vendor.json
VENDOR_ID=$(jq -r .id vendor.json)

curl -s -X POST http://localhost:8888/api/v1/filaments -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d "{\"vendorId\": \"$VENDOR_ID\", \"material\": \"PLA Basic\", \"materialType\": \"PLA\", \"variants\": [{\"colorName\": \"Black\", \"colorHex\": \"#18181b\"}]}" | tee filament.json
FILAMENT_VARIANT_ID=$(jq -r '.variants[0].id' filament.json)
```

## Scenario 2 — Connector-derived Feed Units (FR-005a/b)

```bash
curl -s http://localhost:8888/api/v1/printers/<printerId>/feed-units -H "Authorization: Bearer <token>" | tee feedunits.json | jq '.feedUnits[] | {kind, unitIndex, displayLabel}'
```

**Expected** (simulator, after a few seconds of connection): one `MultiMaterialUnit` (`"AMS 1"`) with four
child slots. **No `ExternalSpool` entry** — the simulator's AMS is active, so the External Spool position is
omitted from the response by default (`research.md` §12, FR-022/FR-023) — it still exists and syncs
underneath, just isn't surfaced until revealed or loaded onto. Confirm no `AutomaticFilamentReloader` unit
exists yet (none declared).

```bash
MMU_SLOT_1=$(jq -r '.feedUnits[] | select(.kind == "MultiMaterialUnit") | .feedSlots[0].id' feedunits.json)
MMU_SLOT_2=$(jq -r '.feedUnits[] | select(.kind == "MultiMaterialUnit") | .feedSlots[1].id' feedunits.json)
```

## Scenario 2b — Reveal, the loaded-spool safety net, and re-hide (FR-024/FR-025, unified add/remove action)

The External Spool position isn't in Scenario 2's default response at all. Reveal it via the same unified
"Add a Feed Unit" action used for an Automatic Filament Reloader (`research.md` §13) — no id needed, since
the handler finds the printer's own singleton `ExternalSpool` row itself:

```bash
curl -s -X POST http://localhost:8888/api/v1/printers/<printerId>/feed-units -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"kind": "ExternalSpool"}' | tee external_spool_unit.json
EXTERNAL_SPOOL_UNIT_ID=$(jq -r .id external_spool_unit.json)
EXTERNAL_SLOT_ID=$(jq -r '.feedSlots[0].id' external_spool_unit.json)
```

**Expected**: `200`, `isRevealed: true`, `feedSlots` included directly in the response. Re-`GET
.../feed-units` (default) and confirm the `ExternalSpool` unit now appears alongside the AMS unit.

Load a spool onto it, then use the unified "Remove a Feed Unit" action to re-hide it *while the spool is
still loaded* — demonstrating the safety net:

```bash
curl -s -X PUT http://localhost:8888/api/v1/spools/<some-spool-id>/load -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d "{\"feedSlotId\": \"$EXTERNAL_SLOT_ID\"}" | jq '.portSharingNote'

curl -i -X DELETE http://localhost:8888/api/v1/feed-units/$EXTERNAL_SPOOL_UNIT_ID -H "Authorization: Bearer <token>"

curl -s http://localhost:8888/api/v1/printers/<printerId>/feed-units -H "Authorization: Bearer <token>" \
  | jq '.feedUnits[] | select(.kind == "ExternalSpool")'
```

**Expected**: the load succeeds with a populated `portSharingNote`; the `DELETE` (re-hide) succeeds (`204`);
but the `ExternalSpool` unit **still appears** in the default listing — `isRevealed: false` again, but
FR-025's safety net keeps it visible because a spool is `Using` it. Unload that spool and re-`GET
.../feed-units` — now it disappears from the default listing, since nothing keeps it visible anymore.

Attempt `DELETE /feed-units/{id}` against the `MultiMaterialUnit` unit's own id:

**Expected**: `422` — remove/re-hide only applies to `AutomaticFilamentReloader`/`ExternalSpool`, never a
connector-derived `MultiMaterialUnit`.

## Scenario 3 — Register spools, load them (with RFID validation)

```bash
curl -s -X POST http://localhost:8888/api/v1/spools -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d "{\"filamentVariantId\": \"$FILAMENT_VARIANT_ID\", \"spoolWeightGrams\": 1000}" | tee spool1.json
SPOOL_1=$(jq -r .id spool1.json)

curl -s -X POST http://localhost:8888/api/v1/spools -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d "{\"filamentVariantId\": \"$FILAMENT_VARIANT_ID\", \"spoolWeightGrams\": 1000}" | tee spool2.json
SPOOL_2=$(jq -r .id spool2.json)

curl -s -X PUT http://localhost:8888/api/v1/spools/$SPOOL_1/load -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d "{\"feedSlotId\": \"$MMU_SLOT_1\"}" | jq '{state, feedSlotId, rfidMismatchWarning}'
```

**Expected**: `200`, `state: "Using"`. Load Spool #2 onto the AMS's *second* slot too — both spools are now
simultaneously `Using`, in different slots of the same Multi-Material Unit:

```bash
curl -s -X PUT http://localhost:8888/api/v1/spools/$SPOOL_2/load -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d "{\"feedSlotId\": \"$MMU_SLOT_2\"}"
```

## Scenario 4 — Multi-Material Unit auto-continuation, match required (User Story 4, Acceptance Scenarios 1–2)

With both Spool #1 (slot 1) and Spool #2 (slot 2) `Using` the same Filament/color, deplete Spool #1:

```bash
curl -s -X POST http://localhost:8888/api/v1/spools/$SPOOL_1/usage -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"gramsConsumed": 1000}' | jq '{state, autoContinuation}'
```

**Expected**: `state: "Finished"`, `autoContinuation.occurred: true`, `continuedSpoolId` equals `$SPOOL_2`,
`materialMismatch: false`. Confirm via `GET /spools`: Spool #2 is still shown `Using` its own slot — it was
never "reloaded," it just continues to be tracked exactly where it already was.

**No-match path** (Acceptance Scenario 2): repeat with a *different*-color Spool #3 loaded onto slot 2
instead of a matching one. Deplete slot 1's spool and confirm `autoContinuation.occurred: false` — the
system does not continue, this is treated as ordinary (no warning), not an error. Confirm the depleted
spool's state is `Finished` and nothing else changed.

## Scenario 5 — Add and use an Automatic Filament Reloader (User Story 4, Acceptance Scenarios 3–4)

```bash
curl -s -X POST http://localhost:8888/api/v1/printers/<printerId>/feed-units -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"kind": "AutomaticFilamentReloader", "name": "Splicer A", "slotCount": 2}' | tee afr.json
AFR_SLOT_1=$(jq -r '.feedSlots[0].id' afr.json)
AFR_SLOT_2=$(jq -r '.feedSlots[1].id' afr.json)
```

**Expected**: `201`, two `FeedSlot`s provisioned immediately (no telemetry involved — this is the one
manually-added kind, FR-005c) — via the same unified "Add a Feed Unit" action Scenario 2b used to reveal the
External Spool, just with `kind: "AutomaticFilamentReloader"` and the extra `name`/`slotCount` fields.

Load a spool into each position (any two spools — for the mismatch test below, use *different* colors):

```bash
curl -s -X PUT http://localhost:8888/api/v1/spools/<spool-a>/load -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d "{\"feedSlotId\": \"$AFR_SLOT_1\"}"
curl -s -X PUT http://localhost:8888/api/v1/spools/<spool-b>/load -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d "{\"feedSlotId\": \"$AFR_SLOT_2\"}"
```

Deplete `<spool-a>`:

```bash
curl -s -X POST http://localhost:8888/api/v1/spools/<spool-a>/usage -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"gramsConsumed": 1000}' | jq '.autoContinuation'
```

**Expected**: `occurred: true`, `continuedSpoolId` equals `<spool-b>`, **regardless of whether its color
matches** `<spool-a>`'s — an Automatic Filament Reloader never checks material (`Unconditional` policy). If
the colors differ, `materialMismatch: true` and an `ActivityLogEntry` (`FeedUnitMaterialMismatch`) exists,
but the continuation still completed (FR-011).

**No-slot-2-loaded path**: repeat with nothing loaded in slot 2. Deplete slot 1's spool and confirm
`autoContinuation.occurred: false`, but this time check the activity log for `FeedUnitNoQualifyingReload` —
unlike the Multi-Material Unit's no-match case, this *is* a warning (an AFR's entire purpose is unattended
continuation, so nothing to continue onto is a real gap, not an ordinary outcome).

## Scenario 5b — Add an Automatic Filament Reloader that supersedes the External Spool port (FR-022)

Register a *second* printer for this scenario (or re-hide/remove the one from Scenario 2b first) so its
External Spool starts fresh, hidden:

```bash
curl -s http://localhost:8888/api/v1/printers/<printerId2>/feed-units -H "Authorization: Bearer <token>" \
  | jq '.feedUnits[] | select(.kind == "ExternalSpool")'
```

**Expected**: no `ExternalSpool` entry (hidden, per Scenario 2's default behavior). Add an AFR declaring it
supersedes that printer's External Spool — the handler resolves the External Spool by looking it up on the
same printer, not by the caller supplying its id:

```bash
curl -s -X POST http://localhost:8888/api/v1/printers/<printerId2>/feed-units -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"kind": "AutomaticFilamentReloader", "name": "Splicer B", "slotCount": 2, "replacesFeedUnitId": "<external-spool-unit-id-for-printerId2>"}'
```

**Expected**: `201`, `replacesFeedUnitId` echoes the External Spool unit's id. Load a spool directly onto the
External Spool slot anyway:

```bash
curl -s -X PUT http://localhost:8888/api/v1/spools/<some-spool-id>/load -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d "{\"feedSlotId\": \"<external-spool-slot-id>\"}" | jq '.portSharingNote'
```

**Expected**: the load still succeeds (`200`) — `portSharingNote` is populated (e.g. `"This port may be
superseded by Splicer B"`), never a blocking error (FR-022); the External Spool unit now also appears in the
*default* listing per FR-025's safety net, since a spool is loaded on it.

## Scenario 5c — Detected-material gating is per-instance for every kind, not restricted by kind (FR-021, corrected)

Using the revealed External Spool unit from Scenario 2b (`external_spool_unit.json`) alongside the AMS unit:

```bash
curl -s http://localhost:8888/api/v1/printers/<printerId>/feed-units -H "Authorization: Bearer <token>" \
  | jq '.feedUnits[] | {kind, slots: [.feedSlots[].detectedMaterialType]}'
```

**Expected**: for the simulator/virtual printer (no RFID hardware simulated), every slot's
`detectedMaterialType` is `null` — for both `MultiMaterialUnit` and `ExternalSpool` alike, since neither
kind gates this by itself anymore (`research.md` §8, corrected after research disproved the earlier
"External Spool never carries RFID data" rule via Snapmaker U1's individually-RFID-capable single inputs).
`null` is expected either way — when nothing's loaded, or when the specific hardware/product simply doesn't
read tags at all — both skip FR-021's comparison identically; only a populated value ever triggers it,
regardless of which kind the slot belongs to.

## Scenario 5d — Multi-Material Unit auto-derives as superseding the External Spool port (FR-023, no declaration needed)

With the AMS unit from Scenario 2 still connected (no Automatic Filament Reloader added this time — use
the revealed External Spool unit from Scenario 2b, which was never given a `replacesFeedUnitId`):

```bash
curl -s http://localhost:8888/api/v1/printers/<printerId>/feed-units -H "Authorization: Bearer <token>" \
  | jq '.feedUnits[] | select(.kind == "ExternalSpool") | .supersededByFeedUnitId'
```

**Expected**: non-null, equal to the `MultiMaterialUnit` unit's own id — populated automatically, purely
because that unit is currently active, with no operator action taken (contrast with Scenario 5b, where the
same field was populated by an explicit `replacesFeedUnitId` declaration on a manually-added Automatic
Filament Reloader). This is the auto-derived case — there's no declared relationship behind it, but the
hide-by-default behavior it drives is identical either way (`research.md` §12): re-hide this unit (`DELETE
/feed-units/{id}`) and confirm it disappears from the default listing again, exactly like Scenario 5b's
would if re-hidden.

## Scenario 6 — Remove the Automatic Filament Reloader

```bash
curl -i -X DELETE http://localhost:8888/api/v1/feed-units/$(jq -r .id afr.json) -H "Authorization: Bearer <token>"
```

**Expected**: `204`. Re-`GET .../feed-units` and confirm the unit/slots still appear (not deleted) but with
`deactivatedAtUtc` set, and any spool still `Using` one of its slots is completely unaffected (`GET
/spools`). Attempt the same `DELETE` against the connector-derived AMS unit's id and confirm `422` —
connector-derived units can't be manually deactivated.

## Scenario 7 — Topology change on a connector-derived unit while in use — unchanged from the prior design

Same as before: a Multi-Material Unit's telemetry shrinking mid-use soft-deactivates its slots without
disturbing an already-`Using` spool's own state.

## Concurrency edge case — unchanged

Two near-simultaneous `unload` calls against the same spool: one `200`, one `409`.

## Activity log coverage check

Confirm `SpoolRfidMismatchWarning`, `FeedUnitAutoContinued`, `FeedUnitMaterialMismatch`, and
`FeedUnitNoQualifyingReload` entries all appear where triggered above, via the unmodified
`ActivityLogPostProcessor` pipeline.
