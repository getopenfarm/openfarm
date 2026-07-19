# Phase 3 Data Model: Filament Tracking

Supersedes the previous version of this file in full — `FeedSlot` split into `FeedUnit` (physical
accessory) + `FeedSlot` (position within it), per `research.md` §1–§4. Seven new tables in
`FilamentTracking` (`Vendors`, `Filaments`, `FilamentVariants`, `FeedUnits`, `FeedSlots`, `Spools`,
`SpoolUsageRecords`) plus one small, separate addition to the existing `Printers` table
(`ExternalSpoolTray`, unchanged from the prior design).

## `Vendor` (new table) — unchanged

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Finbuckle tenant column. |
| `Name` | `string` | Required, non-empty. Unique per organization. |
| `CreatedAtUtc` | `DateTimeOffset` | |

No delete endpoint. Managed via a create + rename UI embedded in the Filament feature page
(`research.md` of the prior planning pass, §9 — unaffected by this pass's changes).

## `Filament` (new table) — unchanged

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Tenant column. |
| `VendorId` | `Guid` (FK → `Vendors.Id`) | Required. `ON DELETE RESTRICT`. |
| `Material` | `string` | e.g. `"PLA Basic"`. Required, non-empty. |
| `MaterialType` | `string` | e.g. `"PLA"` — free text. |
| `FilamentProfileId` | `Guid?` | Optional loose reference to `OpenFarm.Domain.Profiles.FilamentProfile` (FR-013). Bare `Guid?`, no FK. |
| `CreatedAtUtc` | `DateTimeOffset` | |

## `FilamentVariant` (new table) — unchanged

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `FilamentId` | `Guid` (FK → `Filaments.Id`) | `ON DELETE CASCADE`. |
| `ColorName` | `string` | Required, non-empty. |
| `ColorHex` | `string` | Required. |
| `CreatedAtUtc` | `DateTimeOffset` | |

Unique per `(FilamentId, ColorName)`.

## `FeedUnit` (new table — the physical accessory)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Tenant column. |
| `PrinterId` | `Guid` | Bare FK, no navigation into `OpenFarm.Domain.Printers` — validated at the `Application` layer via the existing `IPrinterRepository`. |
| `Kind` | `string` | Open, extensible tag — `"MultiMaterialUnit"`, `"ExternalSpool"`, `"AutomaticFilamentReloader"` known today. Validated/resolved against `FeedUnitKindCatalog` (`research.md` §2), not a Postgres enum. |
| `UnitIndex` | `int` | Which physical unit of this `Kind` on the printer (matches `AmsUnit.UnitIndex` for `MultiMaterialUnit`; a fixed `0` for the singleton `ExternalSpool`; operator-assigned, incrementing per declared unit, for `AutomaticFilamentReloader`). |
| `Name` | `string?` | Operator-given display name — required and meaningful only for `AutomaticFilamentReloader` (there's no brand terminology to fall back on, `research.md` §5); `null` for connector-derived kinds, which compute their display label instead (see "Derived display fields"). |
| `ReplacesFeedUnitId` | `Guid?` (self-FK → `FeedUnits.Id`) | Only ever set on an `AutomaticFilamentReloader` row, referencing an `ExternalSpool`-kind `FeedUnit` on the same printer — an operator's optional declaration that this AFR physically supersedes that port (`research.md` §9). `ON DELETE SET NULL`, no enforcement beyond the target's `Kind` at write time, never referenced by the reload-policy algorithm. `null` for every other row. |
| `IsRevealed` | `bool` | Default `false`. Meaningful only on an `ExternalSpool`-kind row — an operator flips this on via `AddFeedUnitCommand` (`Kind = ExternalSpool`, the same unified action used to add an Automatic Filament Reloader, `research.md` §13) to keep it visible in `GetFeedUnitsQuery` despite being superseded, e.g. when a bypass/multi-way connector makes it genuinely usable alongside whatever supersedes it, and back off via `RemoveFeedUnitCommand`. Ignored for every other `Kind` (a non-`ExternalSpool` unit is never hidden in the first place). |
| `CreatedAtUtc` | `DateTimeOffset` | |

Unique per `(PrinterId, Kind, UnitIndex)`. No manual create/rename/delete for `Kind ∈ {MultiMaterialUnit,
ExternalSpool}` — written exclusively by `IFeedSlotTelemetrySync`. `Kind = AutomaticFilamentReloader` is the
one exception: added via `AddFeedUnitCommand` (`Kind = AutomaticFilamentReloader`, operator-supplied `Name` +
slot count, which immediately provisions that many child `FeedSlot`s) and removed via
`RemoveFeedUnitCommand` (`research.md` §13, unified with revealing/re-hiding an `ExternalSpool` position) —
never touched by the telemetry sync.

## `FeedSlot` (new table — a position within a `FeedUnit`)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Tenant column. |
| `FeedUnitId` | `Guid` (FK → `FeedUnits.Id`) | `ON DELETE RESTRICT` — a `FeedUnit` is never hard-deleted (only its slots deactivate), so this never actually fires; defensive default. |
| `SlotPosition` | `int` | Position within the unit (matches `AmsTray.SlotIndex` for `MultiMaterialUnit`; fixed `0` for `ExternalSpool`'s singleton slot; `0..N-1` as declared at creation for `AutomaticFilamentReloader`). |
| `DeactivatedAtUtc` | `DateTimeOffset?` | Set by the telemetry sync when a connector-derived slot is no longer reported; cleared automatically if it reappears. For an `AutomaticFilamentReloader`'s slots, set when the operator deactivates the whole unit (`research.md` §7) — never by any automatic process. |
| `CreatedAtUtc` | `DateTimeOffset` | |

Unique per `(FeedUnitId, SlotPosition)`. No `Kind` here — lives on the parent `FeedUnit` only
(`research.md` §3). No `IsAutomaticFilamentReloaderEnabled`, no `StandbySpoolId` — both removed entirely;
auto-continuation is determined by the parent `FeedUnit.Kind`'s `ReloadPolicy` at swap time, not stored
per-slot config (`research.md` §4).

The "currently active spool" for a `FeedSlot` is still not a column — derived by querying `Spools` where
`FeedSlotId` matches and `State = Using` (unchanged, still backed by the partial unique index below).

## `Spool` (new table — unchanged shape)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Tenant column. |
| `FilamentVariantId` | `Guid` (FK → `FilamentVariants.Id`) | Required. `ON DELETE RESTRICT`. |
| `Label` | `string?` | Optional operator-chosen identifier. |
| `SpoolWeightGrams` | `decimal` | `> 0` at creation. Immutable after. |
| `RemainingWeightGrams` | `decimal` | Defaults to `SpoolWeightGrams`; `≤ SpoolWeightGrams` and `> 0` at creation only. Thereafter the live, incrementally-updated estimate. |
| `HasUsageOverrun` | `bool` | Default `false`. |
| `State` | enum: `InStorage` \| `Using` \| `Finished` | Postgres enum + `Domain`-enforced `SpoolStateTransitions`. |
| `FeedSlotId` | `Guid?` (FK → `FeedSlots.Id`) | Set only while `State = Using`. `ON DELETE RESTRICT`. |
| `CreatedAtUtc` | `DateTimeOffset` | |
| *(xmin)* | Postgres system column | `UseXminAsConcurrencyToken()`. |

### `SpoolUsageRecord` (new table — unchanged)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Tenant column. |
| `SpoolId` | `Guid` (FK → `Spools.Id`) | `ON DELETE CASCADE`. |
| `GramsConsumed` | `decimal` | `> 0`. |
| `RecordedAtUtc` | `DateTimeOffset` | |
| `JobId` | `Guid?` | Reserved for Phase 8. |
| `Source` | enum: `Job` \| `Manual` | |

## `Printer.ExternalSpoolTray` (new column on the existing `Printers` table — unchanged, separate migration)

| Field | Type | Notes |
|---|---|---|
| `ExternalSpoolTray` | `AmsTray?` (`jsonb`, reused type) | Parsed from `print.vt_tray` (`research.md` §6). Folds under `Printer.RecordTelemetry`'s existing delta rule, same as `AmsUnits`. |

Only change to `Printers`. Migration: `AddExternalSpoolTelemetry`, separate from `AddFilamentTracking`.

## Reload policy resolution (`FeedUnitKindCatalog`, `research.md` §2/§4)

```
Kind                       -> ReloadPolicy
"MultiMaterialUnit"        -> MatchRequired
"AutomaticFilamentReloader"-> Unconditional
"ExternalSpool"            -> None
<anything unrecognized>    -> None (default; sync never fails on an unknown kind)
```

## Derived display fields — computed at read time, not persisted columns

- **`FeedUnit`/`FeedSlot` display label**: for `MultiMaterialUnit`, computed from `(Printer.Brand, Kind,
  UnitIndex, SlotPosition)` via `FeedUnitKindCatalog`'s brand-terminology resolution (e.g. `"AMS 1 · Slot
  2"`). For `ExternalSpool`, always `"External Spool"` (no unit/slot suffix — it's always a singleton). For
  `AutomaticFilamentReloader`, the operator's own `FeedUnit.Name` plus `SlotPosition` (e.g. `"Splicer A ·
  Slot 2"`). Never a stored column (`research.md` §5).
- **`FeedSlot` detected-material fields** (FR-021, any `Kind` with live telemetry backing it): for
  `MultiMaterialUnit`, read from the owning `Printer`'s current `AmsUnits[UnitIndex].Trays[SlotPosition]`;
  for `ExternalSpool`, from `Printer.ExternalSpoolTray`. Both at the moment of the load call — never cached
  on `FeedSlot` itself. No longer restricted to `MultiMaterialUnit` (`research.md` §8).
- Every joined/denormalized `Spool`/`Filament` response field from the prior design (`vendorName`,
  `colorName`, `colorHex`, `material`, `feedSlotLabel`) is unchanged in spirit, now sourced through the
  `FeedUnit`/`FeedSlot` join instead of a flat `FeedSlot`.

## Validation rules

- `Vendor.Name`, `Filament.Material`, `FilamentVariant.ColorName`/`ColorHex` — required, non-empty.
- `Spool.SpoolWeightGrams > 0`, `Spool.RemainingWeightGrams > 0`, `RemainingWeightGrams ≤ SpoolWeightGrams`
  — at creation only.
- `RegisterSpoolCommand` accepts only a `FilamentVariantId` — no free-text fields.
- `SpoolUsageRecord.GramsConsumed > 0`.
- Loading a spool (`LoadSpoolOntoFeedSlotCommand`) requires: the spool `InStorage`; the target `FeedSlot`
  not `DeactivatedAtUtc`-set; and no other spool currently `Using` that `FeedSlot`.
- `AddFeedUnitCommand` (`research.md` §13), `Kind = AutomaticFilamentReloader`: `Name` required non-empty,
  unique per printer; slot count `≥ 1` (validated as a positive integer — a 1-slot AFR is legal, even though
  it could never itself have a sibling to continue onto; `ReloadPolicy.Unconditional` simply never finds a
  qualifying sibling for it, the same "no qualifying target" outcome as any other unit with nothing loaded
  elsewhere). `ReplacesFeedUnitId`, when provided, must reference an existing `FeedUnit` on the same
  `PrinterId` whose `Kind = ExternalSpool` — any other target (a `MultiMaterialUnit`, a different printer's
  unit, or another `AutomaticFilamentReloader`) is rejected (422).
- `AddFeedUnitCommand`, `Kind = ExternalSpool`: no `Name`/slot count accepted — 404 if the printer has no
  `ExternalSpool`-kind `FeedUnit` at all (FR-026, nothing to reveal); otherwise sets `IsRevealed = true`,
  idempotently (no error if already `true`).
- `RemoveFeedUnitCommand`, keyed by the target `FeedUnit.Id`: if `Kind = AutomaticFilamentReloader`,
  deactivates it (unchanged); if `Kind = ExternalSpool`, sets `IsRevealed = false`, idempotently.
- **`FeedUnit`/`FeedSlot` write ownership**: `Kind ∈ {MultiMaterialUnit, ExternalSpool}` rows are written
  exclusively by `IFeedSlotTelemetrySync`, with one narrow exception — `AddFeedUnitCommand`/
  `RemoveFeedUnitCommand` toggle `IsRevealed` directly on an `ExternalSpool` row (`research.md` §12/§13);
  every other field on that row stays sync-owned. `Kind = AutomaticFilamentReloader` rows are written
  exclusively by the same two commands, dispatching on `Kind` (`research.md` §13) — no other code path
  writes to any `FeedUnit`/`FeedSlot` row.

## State machine (`SpoolStateTransitions`) — unchanged

```
InStorage --LoadOntoFeedSlot(feedSlotId)--> Using
Using     --ReturnToStorage()--------------> InStorage   (keeps RemainingWeightGrams)
Using     --MarkFinished()-----------------> Finished    (manual, FR-008; or automatic via reload policy, FR-009)
Finished  -- terminal --
```

## Auto-continuation (FR-009–011a) — kind-driven, no stored config

Cross-aggregate orchestration in `Application/FilamentTracking/SpoolUsageRecordedHandler`, invoked whenever
a usage record drives a `Using` spool's `RemainingWeightGrams` to exactly `0`:

1. Load the depleted spool's `FeedSlot` and its parent `FeedUnit`; resolve `ReloadPolicy` via
   `FeedUnitKindCatalog.Resolve(feedUnit.Kind)`.
2. Mark the depleted spool `Finished` (its `FeedSlotId` may stay as a historical breadcrumb — no correctness
   issue, since the partial unique index only scopes to `State = Using` rows).
3. If `ReloadPolicy = None` → stop; no further action (manual path, FR-008).
4. If `ReloadPolicy = MatchRequired` → query sibling `FeedSlot`s (`FeedUnitId` matches, `Id` ≠ the depleted
   one) for a `Using` spool whose `FilamentVariantId` equals the depleted spool's; lowest `SlotPosition`
   wins ties. None found → log `FeedUnitNoQualifyingReload` (informational — this is an ordinary, expected
   outcome of the match requirement, not a fault) and stop. Found → that spool **does not move** (it's
   already `Using` its own slot) — no state transition on it at all; just publish
   `FeedUnitAutoContinued` (`{feedUnitId, finishedSpoolId, continuedSpoolId, continuedFeedSlotId}`) and log
   `ActivityLogEntry` (`EventType: "FeedUnitAutoContinued"`).
5. If `ReloadPolicy = Unconditional` → query sibling `FeedSlot`s for the lowest-`SlotPosition` `Using` spool
   (no variant filter). None found → log `FeedUnitNoQualifyingReload` (this time a genuine warning — an
   Automatic Filament Reloader's whole purpose is unattended continuation, so nothing to continue onto is a
   real gap, edge case) and stop. Found → same as step 4 (no state transition, just the swap event); if its
   `FilamentVariantId` differs from the depleted spool's, additionally log
   `ActivityLogEntry` (`EventType: "FeedUnitMaterialMismatch"`) — FR-011, warn but never block.

Note the swap never calls `Spool.LoadOntoFeedSlot` on the continuation target — unlike the earlier design
(which modeled a `StandbySpoolId` moving into the depleted slot), the continuation target was already
`Using` its own sibling slot the whole time; there is nothing to load. A caller recording further usage
after a swap (today's interim endpoint; eventually Phase 8) must target the *continued* spool's id, which
`GetFeedUnitsQuery`'s per-slot `currentSpool` field makes discoverable.

## Detected-material load-time validation (FR-021) — no `Kind` restriction, gated purely on live data presence

**Corrected** (supersedes the earlier version of this section, which categorically excluded `ExternalSpool`
— disproven by real hardware, `research.md` §8: Snapmaker U1 reads RFID on individually-addressable,
single-slot inputs structurally identical to an `ExternalSpool` unit). `LoadSpoolOntoFeedSlotCommandHandler`,
after the state/occupancy preconditions, reads the owning `Printer`'s current telemetry for the target
`FeedSlot`'s position — `AmsUnits[UnitIndex].Trays[SlotPosition]` for a `MultiMaterialUnit` slot,
`ExternalSpoolTray` for an `ExternalSpool` slot — regardless of `Kind`. Empty (no data detected — either
nothing loaded, or this specific hardware/product never reads tags at all) → skip, no warning. Otherwise
compare `MaterialType`/`ColorHex`/`SubBrand` against the selected spool's `FilamentVariant`/`Filament`/
`Vendor` — mismatch sets a non-blocking `rfidMismatchWarning: true` on the response and logs
`ActivityLogEntry` (`EventType: "SpoolRfidMismatchWarning"`). Only `AutomaticFilamentReloader` slots never
trigger this — not because of their `Kind` per se, but because they genuinely have no telemetry backing
them at all (manually declared, `research.md` §7).

## Port sharing: hide-by-default, `IsRevealed` escape hatch, loaded-spool safety net (FR-022–FR-025)

`GetFeedUnitsQuery` computes an `ExternalSpool` unit's `supersededByFeedUnitId` from **either** source, in
priority order (`research.md` §9/§11):
1. **Declared** (FR-022): some `AutomaticFilamentReloader`-kind `FeedUnit` on the same printer has
   `ReplacesFeedUnitId` pointing at this `ExternalSpool` unit.
2. **Auto-derived** (FR-023/FR-023a), only checked if (1) found nothing and the printer's model has a single
   filament inlet (`PrinterModelCatalog.Resolve(printer.Brand, printer.ModelCode).FilamentInletCount == 1`):
   the same printer currently has at least one non-deactivated `MultiMaterialUnit`-kind `FeedUnit` — a
   Multi-Material Unit's presence is directly observable from live telemetry, so no operator declaration or
   stored field is needed for this case. Never checked against other `MultiMaterialUnit` rows themselves —
   multiple Multi-Material Units on the same printer never supersede each other. On a multi-inlet printer
   model (`FilamentInletCount > 1`, e.g. Bambu H2D/H2C/X2D) this auto-derivation is skipped entirely — an
   External Spool position there is only ever superseded via (1), never by Multi-Material Unit presence
   alone (`research.md` §11).

**Filtering (`research.md` §12, corrected from an earlier advisory-only design)**: `GetFeedUnitsQuery` omits
an `ExternalSpool` unit from its response entirely when *all three* hold: (a) `supersededByFeedUnitId` is
non-null (from either source above); (b) `IsRevealed = false`; (c) no `Spool` is currently `Using` any of
its `FeedSlot`s. An actively-tracked spool keeps the unit visible unconditionally, regardless of (a)/(b).
`MultiMaterialUnit` and `AutomaticFilamentReloader` units are never filtered by this logic at all — it
applies exclusively to `ExternalSpool`.

**Discoverability, resolved via the unified add/remove action, not a query parameter**
(`research.md` §13): revealing an `ExternalSpool` position no longer requires the client to already know its
id — `AddFeedUnitCommand` with `Kind = ExternalSpool` finds the printer's own (singleton) `ExternalSpool`
row itself and sets `IsRevealed = true`. `RemoveFeedUnitCommand` (keyed by the target `FeedUnit.Id`, since
by this point the operator already knows it — it's now visible) sets it back to `false`. This is the one
narrow exception to FR-005a's "operators MUST NOT manually create/rename/delete a connector-derived Feed
Unit" — neither call creates, renames, or deletes the row; they only toggle its visibility.

`LoadSpoolOntoFeedSlotCommandHandler`, when the target `FeedSlot` belongs to an `ExternalSpool`-kind
`FeedUnit` with a non-null `supersededByFeedUnitId` (from either source above — regardless of whether the
unit is currently hidden or revealed), completes the load regardless and adds a non-blocking
`portSharingNote` (e.g. `"This port may be superseded by Splicer A"` or
`"This printer also has an active AMS 1 attached"`) — informational only, never a validation failure.

## Not modeled as persisted entities

- **`StandbySpoolId` / per-slot AFR flag** — removed entirely; replaced by kind-driven `ReloadPolicy`.
- **"Job" requirement for the reload mismatch check** — still approximated from the depleted spool's own
  `FilamentVariantId`; no `Job` entity exists yet.
- **Dashboard warning widget** — mismatch/no-qualifying-reload/RFID-mismatch signals are `ActivityLogEntry`
  rows; the literal dashboard presentation is Phase 10's scope.
- **Predefined Spool Weight size list** — frontend-only constant.
- **`FeedUnit`/`FeedSlot`'s physical relationship to each other** (e.g. an `AutomaticFilamentReloader`
  wired through an `ExternalSpool` port) — deliberately not modeled (`research.md` §7).
- **Toolhead/nozzle routing** (which nozzle a `FeedUnit` feeds, relevant to multi-toolhead printers like
  Bambu's H2D/H2C) — confirmed via research that the existing `UnitIndex` generalization already covers
  reporting however many units a printer has; routing itself is deferred to a later phase's job-execution
  concern (`research.md` §9a).
- **A dedicated `web/src/features/vendors/` nav feature** — unchanged from the prior design.

## EF Core configuration notes

- Every tenant-scoped table (`Vendors`, `Filaments`, `FeedUnits`, `FeedSlots`, `Spools`,
  `SpoolUsageRecords`) gets `builder.IsMultiTenant()`. `FilamentVariants` reached through `Filaments`.
- `Spool.State` uses `.HasConversion<string>()`. `FeedUnit.Kind` is a plain `string` column — no conversion
  needed, no Postgres enum type created for it (`research.md` §2).
- `Spool` gets `builder.UseXminAsConcurrencyToken()`.
- Partial unique index: `Spools (FeedSlotId) WHERE "State" = 'Using'`.
- `Vendors`: unique on `(OrganizationId, Name)`. `FeedUnits`: unique on `(PrinterId, "Kind", "UnitIndex")`.
  `FeedSlots`: unique on `(FeedUnitId, SlotPosition)`. `FilamentVariants`: unique on `(FilamentId,
  ColorName)`. `FeedUnits.ReplacesFeedUnitId` is a nullable self-referencing FK with `ON DELETE SET NULL` —
  no uniqueness constraint (an `ExternalSpool` unit isn't expected to be referenced by more than one AFR in
  practice, but nothing enforces it at the DB level, `research.md` §9). `FeedUnits.IsRevealed` is a plain
  `bool NOT NULL DEFAULT FALSE` column — no index needed, it's read alongside every other `FeedUnit` field
  already fetched by `GetFeedUnitsQuery`, never queried/filtered on independently.
- `Printer.ExternalSpoolTray` uses the identical `jsonb` + `ValueComparer` pattern already applied to
  `AmsUnits`.
- Two migrations in this feature's PR: `AddFilamentTracking` (the seven new tables) and
  `AddExternalSpoolTelemetry` (the one new `Printers` column).
