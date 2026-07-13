# Phase 1 Data Model: Fleet Management + Live Bambu Integration

Entities and validation rules below implement spec.md's Key Entities section, informed by the
protocol findings in `research.md` (particularly §2 state mapping, §3 AMS schema, and §4 camera
protocol).

## `Printer` (new table)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Finbuckle tenant column; global query filter applies as on every other tenant-scoped table. |
| `Name` | `string` | Operator-assigned label. |
| `Brand` | `string` | `"Bambu"` only this phase (FR-012 anticipates future brands, see `PrinterModel` below). |
| `ModelCode` | `string` | Validated against the `PrinterModel` catalog (see below), **not** free text — e.g. `X1C`, `P1S`, `A1MINI`. Registration/edit is rejected with a clear error if `(Brand, ModelCode)` doesn't resolve to a known catalog entry. |
| `IpAddress` | `string` | Not unique — duplicate IPs across printers are explicitly permitted (FR-001b; DHCP reuse is common). |
| `SerialNumber` | `string` | **Unique per `OrganizationId`** (FR-001b) — enforced via a composite unique index `(OrganizationId, SerialNumber)`. This is also the natural key used to look up the printer's `ConnectionSecret` (see below), so it must never change after creation. |
| `Status` | `PrinterStatus` enum | `Idle` \| `Printing` \| `AwaitingClear`. See transition rules below. |
| `IsReachable` | `bool` | Network/connector reachability — **independent of `Status`**, per spec's Key Entities ("Network reachability is tracked as a separate piece of state, not a value of this status"). Defaults `false` until the connector's first successful message. |
| `LastMessageUtc` | `DateTimeOffset?` | Updated on every successfully parsed MQTT message; drives the ~30s unreachable sweep (research.md §6). Null until first message ever received. |
| `Telemetry` | inline columns (see below) | Point-in-time only, per spec's Key Entities — not historical, overwritten on every update, no separate append-only table. |
| `AmsUnits` | `jsonb` | List of AMS units and their trays — see AMS shape below. Null/empty for printers with no AMS attached. |
| `CreatedAtUtc` | `DateTimeOffset` | |

## `PrinterModel` (value object + compile-time catalog — not a table)

Resolves `(Brand, ModelCode)` into typed, protocol-relevant static facts, replacing a bare free-text
`Model` string. This is deliberately **not** a full extracted `printer_profiles` table (that stays
Phase 4's job, per spec Assumptions and `docs/FEATURE_PLAN.md` — no real duplication across brands
exists yet to justify it, and the constitution's "Build for Expansion, Not Speculation" principle
argues against pulling that forward speculatively). It's a small in-code lookup living in
`OpenFarm.Domain.Printers.PrinterModelCatalog`, resolved at read/connect time, not persisted
redundantly on `Printer`.

**Important scoping note**: AMS support is deliberately **excluded** from this catalog. It is not a
static per-model fact — `pybambu/models.py`'s `supports_feature()` computes `Features.AMS` as
`len(self.ams.data) != 0` (line 194-195), i.e. whether *this specific* printer currently has an AMS
unit physically attached, not something the model name predicts (an A1 may or may not have an AMS
Lite attached; a P1S may or may not have an AMS). This confirms the existing `AmsUnits` JSONB design
above is already correct — AMS presence is inferred live from telemetry, never from the model catalog.

| Field | Type | Notes |
|---|---|---|
| `Brand` | enum | `Bambu` only this phase; Phase 16 (Expanded live printer integration) adds further brands as new catalog entries, not a rewrite of this type. |
| `ModelCode` | `string` | Matches Bambu's own `Printers` `StrEnum` (`pybambu/const.py` lines 12-26). |
| `CameraTransport` | enum: `None` \| `Rtsp` \| `BinaryImage` | Static per-model fact, mutually exclusive — verified directly against `pybambu/models.py`'s `supports_feature()` (lines 172-175). |

Catalog contents for this phase's scope (X1/P1/A1-series only, per spec Assumptions — Bambu's own
`Printers` enum also lists `A2L`, `P2S`, `H2C/H2D/H2DPRO/H2S`, `X2D`, which exist upstream but are out
of scope here):

| `ModelCode` | `CameraTransport` |
|---|---|
| `X1` | `Rtsp` |
| `X1C` | `Rtsp` |
| `X1E` | `Rtsp` |
| `P1P` | `BinaryImage` |
| `P1S` | `BinaryImage` |
| `A1` | `BinaryImage` |
| `A1MINI` | `BinaryImage` |

`BambuCameraRelay` (and eventually any other brand's connector) selects RTSP-vs-binary-image handling
by resolving `PrinterModel.CameraTransport` from `(Brand, ModelCode)` rather than hardcoding per-printer
logic or trusting an operator-set flag — see `research.md` §4 for the camera-protocol implementation
detail this catalog feeds into.

### Inline telemetry columns (nullable — absent until first data arrives)

| Field | Type | Source |
|---|---|---|
| `NozzleTempC` | `decimal?` | `print` payload nozzle temperature field. |
| `BedTempC` | `decimal?` | `print` payload bed temperature field. |
| `PrintProgressPercent` | `int?` | `print` payload progress field. |
| `CurrentStageLabel` | `string?` | Optional human-readable sub-stage (from `stg_cur` → `CURRENT_STAGE_IDS`, research.md §2) — display-only, not part of the `PrinterStatus` state machine. `stg_cur == 0` while `print_type == "idle"` is corrected to the idle sentinel `255` before lookup (firmware boot quirk, research.md §2). |

### AMS shape (`AmsUnits` JSONB)

```jsonc
[
  {
    "unitIndex": 0,
    "trays": [
      { "slotIndex": 0, "empty": false, "materialType": "PLA", "colorHex": "1A1A1AFF", "subBrand": "Bambu PLA Basic", "remainPercent": 62, "active": true },
      { "slotIndex": 1, "empty": true,  "materialType": null,  "colorHex": null,        "subBrand": null,             "remainPercent": null, "active": false },
      // ... up to 4 trays per unit for a standard AMS; a single-entry list for an external/single-spool holder (research.md §3)
    ]
  }
]
```

A printer with no AMS attached has `AmsUnits = null` or `[]`.

### Validation rules

- `SerialNumber` required, unique per `OrganizationId` (FR-001b) — violating this returns a clear
  error identifying the conflicting printer, not a generic constraint-violation message.
- `IpAddress` required, valid IPv4/hostname format, **not** required to be unique.
- `Name` required, non-empty.
- `(Brand, ModelCode)` must resolve against the `PrinterModelCatalog` — an unrecognized combination is
  rejected with a clear error (422) at registration/edit time, not silently accepted as free text.
- A printer cannot be created or have its connection details edited without first passing the
  verify-before-save gate (FR-001a/FR-002, research.md §7) — enforced at the `Application` command
  layer, not just the frontend form.

## `PrinterStatus` (enum, not a table)

```
Idle | Printing | AwaitingClear
```

### Transition rules

| Operation | Allowed from | Allowed to | Trigger |
|---|---|---|---|
| `TransitionTo` (strict, manual) | `AwaitingClear` | `Idle` | Operator "Mark ready" action (spec User Story 4). This is the **only** manual transition this phase supports — FR-011 excludes any manual `Idle → Printing` trigger (no print dispatch yet). Any other manual transition attempt throws `InvalidPrinterStatusTransitionException`. |
| `ObserveStatus` (lenient, connector-reported) | any | any, per research.md §2's `gcode_state` mapping | Every connector-reported `gcode_state` change, regardless of the strict-transition table — reconciles to reported reality rather than rejecting (FR-004). Always logged via `IActivityLogWriter` with `ActorType.System` when triggered from `PrinterConnectionBackgroundService` (non-HTTP context — see plan.md's Security & Multi-Tenancy decision), or via the standard `ActivityLogPostProcessor` when triggered from an HTTP-originated observation path. |

`IsReachable` transitions are **not** part of this state machine — they're set directly by
`PrinterConnectionBackgroundService`'s reachability sweep (research.md §6) and never go through
`TransitionTo`/`ObserveStatus`.

## `ConnectionSecret` (existing table, reused — no schema change)

`api/src/OpenFarm.Domain/ConnectionSecrets/ConnectionSecret.cs` already exists from Phase 0, keyed by
`(OrganizationId, PrinterSerialNumber)` with `EncryptedAccessCode` (`IDataProtector`-encrypted). Phase
1 reuses it via a **natural-key lookup** — `IPrinterRepository`/the connector resolves a printer's
credential by `(printer.OrganizationId, printer.SerialNumber)` rather than adding a
`Printers.ConnectionSecretId` foreign key column. This is possible because `SerialNumber` is already
guaranteed unique per organization (FR-001b, enforced above), so the natural key is sufficient and the
Phase 0 `ConnectionSecrets` table/migration needs zero changes. Editing a printer's connection details
(FR-002) writes a new `ConnectionSecret` row (or updates the existing one) keyed by the same
`(OrganizationId, SerialNumber)` pair — access codes are never shown back in full once saved, per
Phase 0's existing secret-handling convention.

## `Live Telemetry` (not a separate entity — see inline columns above)

Confirmed non-historical per spec's Key Entities ("Reflects current state; not a historical record").
Modeled as nullable columns directly on `Printer`, overwritten on every parsed MQTT `print` payload —
no separate `PrinterTelemetry` table, no retention policy needed (there's nothing to retain).

## `Activity History Entry` (existing table, reused — no schema change)

Every `Printer` status transition (manual `TransitionTo` or connector-reported `ObserveStatus`) is
recorded via the existing `ActivityLogEntries` table and `IActivityLogWriter`/`ActivityLogPostProcessor`
pipeline from Phase 0 — no new table. `EntityType` = `"Printer"`, `EntityId` = the printer's `Id`,
`EventType` distinguishes manual (`"PrinterMarkedReady"`) from connector-reported
(`"PrinterStatusObserved"`) transitions so the activity feed can render them distinctly per FR-010.
