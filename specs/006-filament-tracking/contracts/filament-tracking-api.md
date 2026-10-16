# Contract: Filament Tracking REPR endpoints (`OpenFarm.Api`)

> **2026-10-06 reloader amendment:** [Reloader feed paths and slots](../../../docs/FEED_UNIT_RELOADERS.md) is authoritative for replacement configuration, selected/estimated feeding slots, telemetry projection, and removal. Removed reloaders are archived and omitted by default; loaded spools return to inventory with weights/history preserved. Active replacement claims are unique, editable, and released on removal. The selected replacement slot receives external-port telemetry; standby slots do not. Manual and production consumption share depletion continuation, and production readiness uses the selected slot. These rules supersede conflicting original behavior below.


Supersedes the previous version of this file — Feed Slot endpoints now reflect the `FeedUnit`/`FeedSlot`
split and the removal of the per-slot AFR configuration endpoint; two corrections from real-hardware
research (`research.md` §8/§11): detected-material validation no longer excludes `ExternalSpool` by `Kind`,
and the Multi-Material Unit ↔ External Spool relationship is auto-derived, not declared; a superseded
`ExternalSpool` is omitted from `GET /feed-units` entirely by default (`research.md` §12); and, most
recently, declaring/removing an Automatic Filament Reloader and revealing/re-hiding a superseded
`ExternalSpool` are unified into one `POST`/`DELETE /feed-units` pair (`research.md` §13), replacing what
were three separate endpoints in earlier drafts of this contract. REPR pattern, one FastEndpoints class per
endpoint, `[Permission("...")]` per Phase 0's placeholder-policy precedent, all errors as `ProblemDetails`.

## Vendors — unchanged

### `POST /api/v1/vendors`
`[Permission("vendors:create")]`

**Request**: `{ "name": "Bambu Lab" }`
**Response 201**: `{ "id": "guid", "name": "Bambu Lab" }`
**Response 409** — name already exists.

### `GET /api/v1/vendors`
`[Permission("vendors:list")]`

**Response 200**: `{ "vendors": [ { "id": "guid", "name": "Bambu Lab" } ] }`

## Filaments — unchanged

### `POST /api/v1/filaments`
`[Permission("filaments:create")]`

**Request**: `{ "vendorId": "guid", "material": "PLA Basic", "materialType": "PLA", "filamentProfileId": null, "variants": [ { "colorName": "Black", "colorHex": "#18181b" } ] }`
**Response 201**: full filament shape with `variants[]`.
**Response 404** — `vendorId` doesn't resolve.

### `POST /api/v1/filaments/{id}/variants`
`[Permission("filaments:add-variant")]`

**Request**: `{ "colorName": "White", "colorHex": "#f4f4f5" }`
**Response 201**: `{ "id": "guid", "colorName": "White", "colorHex": "#f4f4f5" }`
**Response 409** — duplicate `(FilamentId, ColorName)`.

### `GET /api/v1/filaments`
`[Permission("filaments:list")]`

**Response 200**: `{ "filaments": [ { "id", "vendorId", "vendorName", "material", "materialType", "filamentProfileId", "variants": [...] } ] }`

## Feed Units / Feed Slots — redesigned around the `FeedUnit`/`FeedSlot` split

### `GET /api/v1/printers/{printerId}/feed-units`
`[Permission("feed-units:list")]` — replaces the earlier `GET .../feed-slots`; now returns the two-level
structure directly, since the frontend's "load spool" picker and printer-detail card both need to group
slots by their physical unit.

**Response 200** — typical single-AMS printer, no bypass connector in use (`ExternalSpool` omitted — see below):
```jsonc
{
  "feedUnits": [
    {
      "id": "guid", "kind": "MultiMaterialUnit", "unitIndex": 0,
      "displayLabel": "AMS 1",
      "feedSlots": [
        {
          "id": "guid", "slotPosition": 0, "displayLabel": "AMS 1 · Slot 1",
          "deactivatedAtUtc": null,
          "currentSpool": { "id": "guid", "filamentVariantId": "guid", "remainingWeightGrams": 820 },
          "detectedMaterialType": "PLA", "detectedColorHex": "#18181b", "detectedSubBrand": "Bambu PLA Basic"
        },
        {
          "id": "guid", "slotPosition": 1, "displayLabel": "AMS 1 · Slot 2",
          "deactivatedAtUtc": null, "currentSpool": null,
          "detectedMaterialType": null, "detectedColorHex": null, "detectedSubBrand": null
        }
      ]
    }
  ]
}
```
**No `ExternalSpool` entry here** — it exists in the database (still synced from telemetry as always), but
`GetFeedUnitsQuery` omits it from the response because it's superseded by the active `MultiMaterialUnit`,
not revealed, and nothing is loaded on it (`research.md` §12, FR-022/FR-023). If the operator reveals it
(`POST /feed-units`, `Kind: "ExternalSpool"`, below) — e.g. because they're using a bypass connector — or a
spool is currently `Using` it, it appears in the array like any other unit:
```jsonc
{
  "id": "guid", "kind": "ExternalSpool", "unitIndex": 0,
  "displayLabel": "External Spool",
  "isRevealed": true,
  "supersededByFeedUnitId": "guid-of-the-ams-unit-or-afr",
  "feedSlots": [
    { "id": "guid", "slotPosition": 0, "displayLabel": "External Spool", "deactivatedAtUtc": null, "currentSpool": null, "detectedMaterialType": null, "detectedColorHex": null, "detectedSubBrand": null }
  ]
}
```
An `AutomaticFilamentReloader` unit is never omitted — only `ExternalSpool` is subject to this filtering:
```jsonc
{
  "id": "guid", "kind": "AutomaticFilamentReloader", "unitIndex": 0,
  "displayLabel": "Splicer A",
  "replacesFeedUnitId": "guid-or-null",
  "feedSlots": [
    { "id": "guid", "slotPosition": 0, "displayLabel": "Splicer A · Slot 1", "deactivatedAtUtc": null, "currentSpool": { "id": "guid", "filamentVariantId": "guid", "remainingWeightGrams": 900 }, "detectedMaterialType": null, "detectedColorHex": null, "detectedSubBrand": null },
    { "id": "guid", "slotPosition": 1, "displayLabel": "Splicer A · Slot 2", "deactivatedAtUtc": null, "currentSpool": null, "detectedMaterialType": null, "detectedColorHex": null, "detectedSubBrand": null }
  ]
}
```

`detectedMaterialType`/`detectedColorHex`/`detectedSubBrand` are populated for **any** kind whose specific
hardware currently reports detected data (`research.md` §8, corrected — no longer restricted to
`MultiMaterialUnit`: real hardware, e.g. Snapmaker U1's individually-RFID-capable single inputs, confirmed
a plain `ExternalSpool`-shaped unit can carry this data too) — `null` whenever nothing's loaded *or* the
specific hardware simply doesn't read tags, indistinguishable either way. Always `null` for
`AutomaticFilamentReloader` (no telemetry backing it at all). Units/slots with `deactivatedAtUtc` set are
included, not hidden — deactivation and this omission are two different, independent things.
`supersededByFeedUnitId` on an `ExternalSpool` unit is populated from either of two sources (`research.md`
§9/§11, priority order): an `AutomaticFilamentReloader` declaring `replacesFeedUnitId` pointing at it
(FR-022, explicit), or — absent that, and only on a single-filament-inlet printer model — the same printer
currently having an active `MultiMaterialUnit` (FR-023, auto-derived purely from live topology, no stored
field; never checked against other `MultiMaterialUnit` rows, which never supersede each other). On a
multi-inlet printer model (`PrinterModel.FilamentInletCount > 1`, e.g. Bambu H2D/H2C/X2D) the second source
never applies — an `ExternalSpool` there is only ever superseded via an explicit `replacesFeedUnitId`
declaration (FR-023a). `isRevealed` reflects whether the operator has revealed this position (FR-024) and is
the reason a superseded unit is shown despite that.

**Response 200 with an empty array** — expected for a printer that has never connected and has no manually
added Automatic Filament Reloader.

**Response 404** — `printerId` doesn't resolve.

### `POST /api/v1/printers/{printerId}/feed-units`
`[Permission("feed-units:add")]` — "Add a Feed Unit," the single unified action covering both declaring a
brand-new Automatic Filament Reloader and revealing an already-detected-but-hidden External Spool position
(`research.md` §13, replacing what were two separately-named mechanisms in earlier drafts of this contract).

**Request, `kind: "AutomaticFilamentReloader"`**:
```jsonc
{ "kind": "AutomaticFilamentReloader", "name": "Splicer A", "slotCount": 2, "replacesFeedUnitId": "guid-or-null" }
```
`replacesFeedUnitId` (FR-022, `research.md` §9): optional — set when this AFR physically supersedes the
printer's External Spool port (a full-replacement multi-way splitter, no bypass) rather than sitting
alongside it via a Y-connector. Must reference an existing `ExternalSpool`-kind unit on this same printer.

**Response 201** (new rows created):
```jsonc
{
  "id": "guid", "kind": "AutomaticFilamentReloader", "unitIndex": 0, "displayLabel": "Splicer A",
  "replacesFeedUnitId": "guid-or-null",
  "feedSlots": [
    { "id": "guid", "slotPosition": 0, "displayLabel": "Splicer A · Slot 1" },
    { "id": "guid", "slotPosition": 1, "displayLabel": "Splicer A · Slot 2" }
  ]
}
```
**Response 409** — an Automatic Filament Reloader named `"Splicer A"` already exists for this printer.
**Response 422** — `slotCount < 1`, or `replacesFeedUnitId` doesn't reference an `ExternalSpool`-kind unit
on this printer.

**Request, `kind: "ExternalSpool"`**:
```jsonc
{ "kind": "ExternalSpool" }
```
No `name`/`slotCount` — the row and its one slot already exist, synced from telemetry; this just reveals it
(FR-024). No id needed either — the handler finds the printer's own `ExternalSpool` unit itself, resolving
`research.md` §12's original discoverability gap without a separate query parameter.

**Response 200** (existing row revealed, not created — idempotent if already revealed):
```jsonc
{
  "id": "guid", "kind": "ExternalSpool", "unitIndex": 0, "displayLabel": "External Spool",
  "isRevealed": true,
  "feedSlots": [ { "id": "guid", "slotPosition": 0, "displayLabel": "External Spool" } ]
}
```
Same shape as the Automatic Filament Reloader response above, including `feedSlots` — so a caller has
everything needed to immediately load a spool onto it without a follow-up `GET`.
**Response 404** — `printerId` doesn't resolve, **or** the printer has never actually reported an
`ExternalSpool` at all (FR-026 — nothing exists to reveal).

### `DELETE /api/v1/feed-units/{id}`
`[Permission("feed-units:remove")]` — "Remove a Feed Unit," the counterpart unified action: dispatches on
the target row's own `Kind` (`research.md` §13).

- `Kind = AutomaticFilamentReloader` → deactivates the unit and all its slots (`DeactivatedAtUtc` set); any
  `Spool` currently `Using` one of its slots keeps its own state/history undisturbed (spec.md's edge case).
- `Kind = ExternalSpool` → sets `IsRevealed = false` again (idempotent if already hidden) — still subject to
  FR-025's safety net, so it doesn't vanish from the listing if a spool is currently `Using` it.
- `Kind ∈ {MultiMaterialUnit}` → not a valid target at all (connector-derived, never removable — FR-005a).

**Response 204** — either branch above.
**Response 404** — doesn't resolve.
**Response 422** — the target `FeedUnit`'s `Kind = MultiMaterialUnit`.

There is deliberately no `PUT .../automatic-filament-reloader` "configure AFR" endpoint — no per-slot flag
or standby assignment exists to configure (`research.md` §4). Loading a spool onto any slot (any `Kind`)
uses the same `PUT /spools/{id}/load` below.

## Spools

### `POST /api/v1/spools` — unchanged
`[Permission("spools:register")]`

**Request**: `{ "filamentVariantId": "guid", "label": "Shelf B-3", "spoolWeightGrams": 1000, "remainingWeightGrams": 1000 }`
**Response 201**: spool shape (`state: "InStorage"`, `feedSlotId: null`).
**Response 404** — `filamentVariantId` doesn't resolve.
**Response 422** — weight validation failures.

### `GET /api/v1/spools` — unchanged shape, `feedSlotLabel` now sourced through the `FeedUnit` join
`[Permission("spools:list")]`

**Query params**: `state`, `filamentId`, `filamentVariantId`, `vendorId`.

**Response 200**: `{ "spools": [ { "id", "label", "filamentVariantId", "colorName", "colorHex", "material", "vendorName", "spoolWeightGrams", "remainingWeightGrams", "state", "feedSlotId", "feedSlotLabel", "hasUsageOverrun" } ] }`

### `PUT /api/v1/spools/{id}/load`
FR-005, FR-021, FR-022, FR-023 — detected-material validation fires for any Feed Slot kind with actual
detected data (no longer restricted to `MultiMaterialUnit`); a `portSharingNote` appears when loading onto
an `ExternalSpool` slot that's either declared-superseded by an AFR or auto-detected as secondary to an
active Multi-Material Unit.

`[Permission("spools:load")]`

**Request**: `{ "feedSlotId": "guid" }`
**Response 200**: spool shape + `{ "rfidMismatchWarning": false, "portSharingNote": null }` — e.g.
`"portSharingNote": "This port may be superseded by Splicer A"` when applicable; both fields are advisory,
never block the load.
**Response 404** — spool or feed slot doesn't resolve.
**Response 409** — spool isn't `InStorage`; the feed slot is deactivated; another spool already `Using` it;
or a concurrent conflicting change lost the race.

### `PUT /api/v1/spools/{id}/unload` — unchanged
`[Permission("spools:unload")]`

**Request**: `{ "action": "ReturnToStorage" }` or `{ "action": "MarkFinished" }`
**Response 200**: spool shape, `feedSlotId: null`.
**Response 409** — spool isn't `Using`, or a concurrent conflicting change lost the race.

### `PUT /api/v1/spools/{id}/remaining-weight` — unchanged
`[Permission("spools:override-weight")]`

**Request**: `{ "remainingWeightGrams": 150 }`
**Response 200**: spool shape, `hasUsageOverrun` reset to `false`.
**Response 422** — negative/non-numeric, or greater than `spoolWeightGrams`.

### `POST /api/v1/spools/{id}/usage`
`[Permission("spools:record-usage")]` — response shape now reflects the kind-driven auto-continuation
(`data-model.md`'s algorithm), not a standby swap.

**Request**: `{ "gramsConsumed": 45.5, "jobId": null }`

**Response 200**:
```jsonc
{
  "id": "guid", "state": "Finished", "remainingWeightGrams": 0,
  "autoContinuation": {
    "occurred": true,
    "continuedSpoolId": "guid", "continuedFeedSlotId": "guid",
    "materialMismatch": false
  }
}
```
`autoContinuation.occurred: false` (with the rest of that object omitted/null) when the spool depleted but
no qualifying continuation target existed, or the `FeedUnit`'s policy is `None`. Note `continuedSpoolId`
never receives a *new* load call as part of this response — it was already `Using` its own sibling slot
(`data-model.md`'s auto-continuation algorithm, step 4/5); this field only tells the caller which spool to
target for the *next* usage recording.

**Response 409** — spool isn't currently `Using`.

## SignalR (`/hubs/farm`, existing hub — no new hub)

- `SpoolStateChanged`: `{ spoolId, state, remainingWeightGrams, feedSlotId }` — unchanged.
- `FeedUnitAutoContinued`: `{ feedUnitId, finishedSpoolId, continuedSpoolId, continuedFeedSlotId,
  materialMismatch: bool }` — replaces `FeedSlotAutomaticFilamentReloaderSwapped`; fired for both
  `MatchRequired` and `Unconditional` continuations.
- `FeedUnitsChanged`: `{ printerId }` — replaces `FeedSlotsChanged`; published whenever
  `IFeedSlotTelemetrySync` creates/deactivates a connector-derived unit/slot, or `POST`/`DELETE
  /feed-units` adds/removes an Automatic Filament Reloader or reveals/re-hides an `ExternalSpool` — any of
  these can change what the next `GET .../feed-units` call returns, so the
  printer-detail page's Feed Units card should refetch on this event regardless of which triggered it.
