# Contract: Printer Build Plate Selection HTTP Surface

Extends two existing endpoints from `005-printer-fleet-refinements`/`009-process-profiles` — no new
routes are added. Both are FastEndpoints classes under `OpenFarm.Api`, JWT-authenticated,
permission-gated, activity-logged, and surface failures as RFC 7807 `ProblemDetails`, matching every
other endpoint in this API surface.

---

## PATCH `/api/v1/printers/{id}`

**Same endpoint 009-process-profiles already extended for machine/process profile** — extended in
place again with one more field, rather than a new `/build-plate` sub-route, so selecting/changing/
clearing a printer's build plate stays part of the single "submit full current state" edit call the
frontend already makes (`EditPrinterEndpoint.cs`).

**Permission**: unchanged (`printers:edit-connection`).

**Request body** — new field:

```jsonc
{
  "id": "3af1...",
  "name": "Printer 1",
  "machineProfileId": "b3f...",
  "processProfileId": "a91...",
  "buildPlateType": "TexturedPei"        // new: string enum, or null to clear
}
```

| Field | Type | Notes |
|---|---|---|
| `buildPlateType` | `"Cool"` \| `"Engineering"` \| `"HighTemp"` \| `"TexturedPei"` \| `"TexturedCool"` \| `"Supertack"` \| `null`, optional | Applied exactly as sent — `null` clears the selection, an omitted/undefined field on the wire is treated the same as `null` by the existing request-DTO default. **Not gated on `machineProfileId` being set** — unlike `processProfileId`, which requires `machineProfileId` first, `buildPlateType` can be set on a printer with no machine profile linked at all (FR-002 Acceptance Scenario #2). |

**200 Response**: `PrinterSummaryResponse` — **unchanged shape** (this endpoint's response today does
not echo `machineProfileId`/`processProfileId` either; the printer's full detail, including the new
build-plate fields, is read back via `GET /api/v1/printers/{id}`, not this endpoint's response).

**New failure mode**:

| Status | Condition | Body |
|---|---|---|
| 422 | `buildPlateType` is a valid enum value but not in the printer's own registered model's compatible set (`PrinterModelCatalog`) | `{ "title": "Build plate incompatible with printer model", "detail": "'Cool' is not a compatible build plate for Bambu A1.", "status": 422 }` |

A syntactically invalid `buildPlateType` string (not one of the six enum values) is rejected at model
binding / FluentValidation, the same way any other malformed enum field already is elsewhere in this
API — no new validator class needed beyond what FluentValidation's enum binding already provides.

**Activity log**: two new granular event types, written by the handler exactly like
`PrinterMachineProfileLinked/Unlinked` — only when the value actually changed:

- `PrinterBuildPlateSelected` — `buildPlateType` went from `null`/a different value to a non-null value
- `PrinterBuildPlateCleared` — `buildPlateType` went from a non-null value to `null`

(The base `PrinterEdited` event the endpoint's `IActivityLoggable` implementation already writes on
every successful call still fires regardless, unchanged.)

---

## GET `/api/v1/printers/{id}`

**Same endpoint** (`GetPrinterDetailEndpoint` → `GetPrinterDetailQuery`) — extended in place with two
new response fields.

**Permission**: unchanged.

**200 Response** — new fields on `PrinterDetailResponse`:

```jsonc
{
  "id": "3af1...",
  "name": "Printer 1",
  "brand": "Bambu",
  "modelCode": "A1",
  "machineProfileId": "b3f...",
  "machineProfileName": "Bambu Lab A1 0.4 nozzle",
  "processProfileId": "a91...",
  "processProfileName": "0.20mm Standard",
  "buildPlateType": "TexturedPei",                          // new, nullable
  "compatibleBuildPlateTypes": ["TexturedPei", "HighTemp"]    // new, always populated
}
```

| Field | Type | Notes |
|---|---|---|
| `buildPlateType` | string enum \| `null` | The printer's current selection; `null` for any printer that hasn't had one selected yet (every printer, until an operator acts). |
| `compatibleBuildPlateTypes` | string enum array | **Always non-empty for a registered printer** — derived purely from the printer's own registered `brand`/`modelCode` via `PrinterModelCatalog`, independent of `machineProfileId`/`machineProfileName` above. Present even when `machineProfileId` is `null` (FR-002 Acceptance Scenario #2) — this is what lets the frontend render build-plate choices immediately on a freshly registered printer, before any machine profile has ever been linked. |

No new endpoint is needed to "browse build plate choices" the way `GET /api/v1/profiles/machine`
exists for machine profiles — the fixed, ≤6-item, printer-specific choice list rides along on the
printer detail response the frontend already fetches to render the page.

---

## Slicing (no new HTTP surface)

The slicing endpoints (`POST /api/v1/parts/{id}/slice` and the estimate-only equivalent) are
**unchanged at the contract level** — `buildPlateType`/build-plate resolution is entirely internal to
`ResolveSlicingProfileSelection` (see `research.md` R3–R4, `data-model.md`). The only externally
visible change is the **content** of the existing failure responses when a printer's build plate is
unconfigured or the resolved filament preset has no temperature for the selected plate — both already
flow through the existing `SlicingProfileUnconfiguredException → 422` mapping already wired in
`DomainExceptionHandler`, just with a message that now also names the missing build plate:

```jsonc
{ "title": "Slicing profile unconfigured", "detail": "Printer 'Printer 1' has no build plate selected.", "status": 422 }
```

```jsonc
{ "title": "Slicing profile unconfigured", "detail": "Filament profile 'Bambu PLA Basic @BBL X1C' has no bed-temperature value for build plate 'Engineering Plate'.", "status": 422 }
```
