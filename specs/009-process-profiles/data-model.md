# Phase 1 Data Model: Process Profiles — System & User Profiles for Machine, Process, and Filament

Extends `003-slicer-profile-import`'s three profile tables (adds columns, no new profile tables — research.md
§3), adds a printer↔process link parallel to the existing printer↔machine link (§4), removes the
organization-level default-machine/default-process/default-filament-per-material tables and fields
entirely, and adds no new tables for System Profiles themselves — they are rows in the same tables as
User-imported profiles, distinguished by `Origin`.

## New enum

### `ProfileOrigin`

```
System | User
```

Persisted as `text` via `HasConversion<string>()`, matching `ProfileSourceFormat`'s existing convention.
`System` rows are ingestion-only (never created/edited via any endpoint); `User` rows are exactly today's
import flow, unchanged.

## Changed entities

### `MachineProfile` (changed)

| Field | Change | Notes |
|---|---|---|
| `Origin : ProfileOrigin` | **new**, required | `User` for every pre-existing row (migration default); ingestion creates `System` rows |
| `VendorName : string?` | **new** | vendor index's own `name` (e.g. `"Bambulab"`); `null` for `User` rows |
| `ExternalId : string?` | **new** | vendor `setting_id` for a selectable leaf; `null` for abstract-base System rows and all `User` rows — research.md §1 idempotency key |
| `IsCurrentlyOffered : bool` | **new**, default `true` | `false` once a re-ingestion no longer contains this `ExternalId`/`(VendorName, Name)` — research.md §1; irrelevant for `User` rows (always `true`) |
| `IsSelectable : bool` | **new**, default `true` | from vendor `instantiation`; `true` for every `User` row (unchanged import behavior) |
| `VendorMachineModelName : string?` | **new** | System machine rows only: the leaf preset's own `printer_model` field, linking to `PrinterModelCatalog` via the new `VendorMachineModelName` catalog field (research.md §0) |
| `IncludedTemplateNames : IReadOnlyList<string>?` | **new**, `jsonb` | System rows only, verbatim from the vendor leaf's own `include` array — populated for Bambu-Studio-sourced rows (universal on its machine presets), `null` for OrcaSlicer-sourced rows (format doesn't use it) and all `User` rows — research.md §0a |

`DefaultProcessProfileId`/`SetDefaultProcessProfile`/`ClearDefaultProcessProfile` are **removed** —
superseded by `Printer.ProcessProfileId` (below). Everything else (`BaseProfileId`/`BaseProfileName`
self-resolution, `RawDataJson`, `ResolveBaseProfile`, `ReplaceRawData`) is unchanged; `ReplaceRawData`
remains callable only through the existing raw-editor path, which stays scoped to `User` rows (a `System`
row is never a valid target — enforced in the command handler, not the entity, consistent with how the
entity itself carries no origin-aware guard clauses elsewhere).

### `ProcessProfile` (changed)

Same five new fields as `MachineProfile` (`Origin`, `VendorName`, `ExternalId`, `IsCurrentlyOffered`,
`IsSelectable`), plus:

| Field | Change | Notes |
|---|---|---|
| `CompatibleMachineNames : IReadOnlyList<string>?` | **new**, `jsonb` | System rows only: verbatim `compatible_printers` array (machine **preset names**, not model names) from vendor data — research.md §0 point 2 |

No raw-editor path exists for `ProcessProfile` after this feature (FR-011) — `ReplaceRawData` stays on the
type (ingestion re-runs use it to refresh a `System` row's JSON on upsert, research.md §1) but no endpoint
calls it for a `User`-initiated edit, and no import endpoint creates `Origin=User` rows anymore.

### `FilamentProfile` (changed)

Same five fields as `MachineProfile`/`ProcessProfile`, plus:

| Field | Change | Notes |
|---|---|---|
| `CompatibleMachineNames : IReadOnlyList<string>?` | **new**, `jsonb` | same shape/source as `ProcessProfile`'s |
| `MaterialType : string?` | **new** | System rows only: the *resolved* (chain-merged, research.md §0 point 5) `filament_type`, e.g. `"PLA"`/`"PETG"`/`"ABS"` — cached at ingestion time, not re-derived per read |
| `IncludedTemplateNames : IReadOnlyList<string>?` | **new**, `jsonb` | same shape/source as `MachineProfile`'s (research.md §0a) — populated for the 68% of Bambu-Studio-sourced filament rows that use `include`; include-target fragments (e.g. `fdm_filament_template_direct_bowden`) are themselves ordinary ingested `FilamentProfile` rows (`type:"filament"`, `instantiation:"false"`), just reached via this field rather than `BaseProfileId` |

### `Printer` (changed)

| Field | Change | Notes |
|---|---|---|
| `ProcessProfileId : Guid?` | **new** | parallel to existing `MachineProfileId`; set via new `LinkProcessProfile(Guid)`/cleared via `UnlinkProcessProfile()`, mirroring `LinkMachineProfile`/`UnlinkMachineProfile` exactly |

Compatibility gating (machine must be selected first; process must be compatible with the selected
machine's `Name` per `CompatibleMachineNames`) is enforced in the `Application`-layer command handler that
calls `LinkProcessProfile`, not in the `Printer` entity itself — consistent with how `Printer.Create`
already delegates its own model-validity check out to `PrinterModelCatalog.Resolve` rather than encoding
catalog knowledge in the entity.

### `PrinterModel` / `PrinterModelCatalog` (changed)

| Field | Change | Notes |
|---|---|---|
| `VendorMachineModelName : string?` | **new** on `PrinterModel` | hand-authored per entry (research.md §0 table); `null` only for the non-vendor `System/Simulation` entry |

`PrinterModelCatalog.Resolve`/`TryResolve`/`All` are unchanged in signature; ingestion scoping (FR-001)
reads `PrinterModelCatalog.All` and matches vendor `machine_model_list[].name` values against each entry's
`VendorMachineModelName`.

### `PartFilamentRequirement` (unchanged shape, extended usage)

`NarrowFilamentVariantId`/`NarrowFilamentProfileId` already exist on this entity and are already fully
wired end-to-end through `ConfirmPartProcessingJobCommand`/`Handler`/`Validator` and `GetPartQuery` (the
one-time narrowing offered during the part-processing confirm flow). What's **missing** — and what FR-012
actually adds — is a **standalone** narrow/change/clear action reachable from the part detail page at any
time after confirm, not only during the initial confirm step. This needs a new command
(`UpdatePartFilamentRequirementNarrowingCommand`) + endpoint, following the same
`NarrowFilamentVariantId`/`NarrowFilamentProfileId` mutual-exclusivity validation
`ConfirmPartProcessingJobCommandValidator` already enforces, and a new frontend picker component on
`PartDetailPage` (there is currently no such control there — `PartFilamentRequirementsList.tsx` only wires
narrowing during the confirm-review flow).

## Removed entities / fields

| Removed | Was | Replaced by |
|---|---|---|
| `Organization.DefaultMachineProfileId` + `SetDefaultMachineProfile`/`ClearDefaultMachineProfile` | org-level default machine profile | `Printer.MachineProfileId` per-printer (already existed; now the *only* source) |
| `MachineProfile.DefaultProcessProfileId` + `SetDefaultProcessProfile`/`ClearDefaultProcessProfile` | machine-profile→process pairing | `Printer.ProcessProfileId` per-printer |
| `OrganizationFilamentDefault` (whole entity/table) | org-level default filament per material | §2's automatic representative-System-filament resolution (no persisted row at all) |
| `IOrganizationFilamentDefaultRepository` | — | deleted, no replacement (resolution is a query over `FilamentProfile`, not a lookup table) |

## Persistence delta — migration `AddSystemProfilesAndProcessLink`

Hand-authored, one migration for the whole cohesive delta (mirrors 008's `AddSlicerVersionTracking`
precedent of one migration per cohesive schema change):

```
MachineProfiles   ADD COLUMN "Origin"                  text NOT NULL DEFAULT 'User'
MachineProfiles   ADD COLUMN "VendorName"               text NULL
MachineProfiles   ADD COLUMN "ExternalId"                text NULL
MachineProfiles   ADD COLUMN "IsCurrentlyOffered"        boolean NOT NULL DEFAULT true
MachineProfiles   ADD COLUMN "IsSelectable"              boolean NOT NULL DEFAULT true
MachineProfiles   ADD COLUMN "VendorMachineModelName"    text NULL
MachineProfiles   ADD COLUMN "IncludedTemplateNames"     jsonb NULL
MachineProfiles   DROP COLUMN "DefaultProcessProfileId"

ProcessProfiles   ADD COLUMN "Origin"                  text NOT NULL DEFAULT 'User'
ProcessProfiles   ADD COLUMN "VendorName"               text NULL
ProcessProfiles   ADD COLUMN "ExternalId"                text NULL
ProcessProfiles   ADD COLUMN "IsCurrentlyOffered"        boolean NOT NULL DEFAULT true
ProcessProfiles   ADD COLUMN "IsSelectable"              boolean NOT NULL DEFAULT true
ProcessProfiles   ADD COLUMN "CompatibleMachineNames"    jsonb NULL

FilamentProfiles  ADD COLUMN "Origin"                  text NOT NULL DEFAULT 'User'
FilamentProfiles  ADD COLUMN "VendorName"                text NULL
FilamentProfiles  ADD COLUMN "ExternalId"                text NULL
FilamentProfiles  ADD COLUMN "IsCurrentlyOffered"        boolean NOT NULL DEFAULT true
FilamentProfiles  ADD COLUMN "IsSelectable"              boolean NOT NULL DEFAULT true
FilamentProfiles  ADD COLUMN "CompatibleMachineNames"    jsonb NULL
FilamentProfiles  ADD COLUMN "MaterialType"              text NULL
FilamentProfiles  ADD COLUMN "IncludedTemplateNames"     jsonb NULL

Printers          ADD COLUMN "ProcessProfileId"          uuid NULL  (FK -> ProcessProfiles, ON DELETE SET NULL)

Organizations     DROP COLUMN "DefaultMachineProfileId"

DROP TABLE "OrganizationFilamentDefaults"
```

- Unique partial index per table: `UNIQUE (OrganizationId, VendorName, ExternalId) WHERE Origin = 'System'
  AND ExternalId IS NOT NULL` (leaf idempotency key) and `UNIQUE (OrganizationId, VendorName, Name) WHERE
  Origin = 'System' AND ExternalId IS NULL` (abstract-base idempotency key) — research.md §1. No
  uniqueness constraint changes for `User` rows (FR-009 in feature 003 already permits duplicate names).
- `Down` reverses every step above (re-adds `OrganizationFilamentDefaults`, restores the dropped columns as
  nullable with no data — this is a forward-only feature by nature, same as 008's `SlicerVersionUsed`
  columns having "no backfill" for historical rows).
- `Printers.ProcessProfileId` FK: `ON DELETE SET NULL`, matching `MachineProfileId`'s existing behavior
  (no delete endpoint exists for profiles yet, defensive default per `MachineProfileConfiguration`'s own
  comment).
- Tenant scoping: unchanged pattern, `IsMultiTenant()` stays applied to all three profile tables and to
  `Printers`.

## Configuration delta (not database)

- **Removed**: any configuration surface backing the interim `OrganizationFilamentDefault` settings page —
  none exists beyond the DB-backed rows themselves (no appsettings knob to remove).
- **Added**: none — ingestion runs as part of the existing `tools/provision-slicers` step (dev/build-time,
  no new runtime configuration) plus a startup or on-demand ingestion pass reading the same
  `SlicerBundleOptions:RootDirectory` `BundledSlicerScanner` already reads (no new root path config).

## Lifecycle & state notes

- **Ingestion** (new step, alongside binary provisioning — research.md §0 "Locating resources/profiles"):
  recursively find each vendor index JSON in the bundled slicer's leaf, keep only vendors whose
  `machine_model_list` overlaps `PrinterModelCatalog.All` (via `VendorMachineModelName`), then for
  machine/process/filament in turn: fetch every listed preset **filtered to the fetched file's own `type`
  matching the expected kind** (research.md §0a point 3 — a vendor's `machine_list`/`filament_list` can
  contain untyped or mismatched-type include-target fragments, confirmed on Bambu Studio's data; list
  membership alone is not sufficient), upsert by the research.md §1 key (capturing `IncludedTemplateNames`
  verbatim where present), resolve each row's `BaseProfileId` by name **within the same ingested batch**
  (mirrors `ConfirmProfileImportCommandHandler`'s existing two-pass import: pass 1 creates/updates rows,
  pass 2 resolves `BaseProfileId`/`FilamentProfile.MaterialType` now that every sibling row exists to
  resolve against), then mark any previously-`System` row *not* seen in this run as `IsCurrentlyOffered =
  false`. Idempotent and safe to re-run (Acceptance Scenario 5).
- **Selection** (printer page): `LinkMachineProfile` unchanged; `LinkProcessProfile` gated on
  machine-selected-first + compatibility (§ above). Both logged via the existing activity-log
  post-processor (FR-016), same as today's `LinkMachineProfile`.
- **Narrowing** (part detail page): new standalone command, logged the same way existing part mutations
  are (FR-016).
- **Slicing resolution**: `ResolveSlicingProfileSelection`'s successor takes a target (printer or none) —
  research.md §5 — and produces the same `ResolvedSlicingProfiles(MachineSettingsJson, ProcessSettingsJson,
  FilamentSettingsJson)` shape `CliSlicerEngine`/`PrusaSliceFamilyInvocation` already consume, so nothing
  downstream of it changes. Its machine/filament resolution additionally folds in `IncludedTemplateNames`
  after the existing ancestor-chain merge (research.md §0a/§5) — without this, every Bambu-Studio-sourced
  slice would silently omit `machine_start_gcode`/`change_filament_gcode`/etc. from the JSON handed to the
  CLI via `--load-settings`/`--load-filaments`.
