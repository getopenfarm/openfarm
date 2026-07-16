# Phase 2 Data Model: Profiles (Machine + Filament, Imported from Slicer Bundles)

Entities and validation rules implementing spec.md's Key Entities section, informed by
`research.md` §1–§3, §8, and §9. Two new tables (`MachineProfiles`, `FilamentProfiles`) — no changes to any
Phase 0/1 table.

## `MachineProfile` (new table)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Finbuckle tenant column; global query filter applies as on every other tenant-scoped table. |
| `Name` | `string` | The preset's own name from the source bundle, e.g. `"Bambu Lab X1 Carbon 0.4 nozzle"`. Not globally unique — duplicate names across imports are permitted (FR-009 flags, never blocks). Real column: needed for FR-009 duplicate lookups and for resolving other profiles' `BaseProfileName` against it. |
| `SourceFormat` | enum: `OrcaSlicer` \| `BambuStudio` | Which slicer produced the bundle this preset came from. Real column, not derivable from `RawDataJson`: which parser branch handled the upload is provenance about the *import*, not necessarily a field the preset JSON reliably self-declares. |
| `BaseProfileName` | `string?` | The raw `"inherits"`-equivalent reference from the source preset, stored verbatim. Null when the preset declares no base. **Never re-derived or cleared** once set, even if `BaseProfileId` never resolves (FR-005). Real column: drives the resolution algorithm (`research.md` §3). |
| `BaseProfileId` | `Guid?` (self-FK → `MachineProfiles.Id`) | Populated only when `BaseProfileName` resolves against another `MachineProfile` row in the same organization — either imported in the same confirm call or an earlier import (`research.md` §3). `ON DELETE SET NULL` — this feature has no delete endpoint yet (out of scope, see spec's Functional Requirements — none of FR-001–015 mention deletion), but the constraint is defined defensively for whenever one is added. |
| `RawDataJson` | `string` (`jsonb`) | The preset's full original JSON, exactly as extracted from the bundle — this is what US4's JSON editor reads and writes (FR-011/012), and the **only** source for every descriptive field below (see "Derived display fields"). Never reshaped or re-keyed on import. |
| `SourceFileName` | `string` | The uploaded bundle's original file name, for traceability/debugging — not shown prominently in the UI, not a functional requirement, just cheap provenance to keep. |
| `CreatedAtUtc` | `DateTimeOffset` | |

## `FilamentProfile` (new table)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Tenant column, same as above. |
| `Name` | `string` | e.g. `"Bambu PLA Basic @ Bambu X1 Carbon"`. Real column, same reason as `MachineProfile.Name`. |
| `SourceFormat` | enum: `OrcaSlicer` \| `BambuStudio` | Same meaning and same "real column" reasoning as `MachineProfile.SourceFormat`. |
| `BaseProfileName` | `string?` | Same semantics as `MachineProfile.BaseProfileName`. |
| `BaseProfileId` | `Guid?` (self-FK → `FilamentProfiles.Id`) | Same resolution rules as `MachineProfile.BaseProfileId`, scoped to the `FilamentProfiles` table (a filament preset's base is always another filament preset, never a machine preset). |
| `RawDataJson` | `string` (`jsonb`) | Same meaning as `MachineProfile.RawDataJson` — the only source for every descriptive field below. |
| `SourceFileName` | `string` | Same meaning as `MachineProfile.SourceFileName`. |
| `CreatedAtUtc` | `DateTimeOffset` | |

## Derived display fields — computed at read time, not persisted columns

Every other field spec.md's Key Entities section names — nozzle size, bed volume, enclosure, supported
materials, and live-integration capability for `MachineProfile`; material, brand, color, and tuned
settings for `FilamentProfile` — is deliberately **not** a stored column on either table. `RawDataJson`
is the single source of truth for all of them; storing a second, derived copy would create exactly the
kind of drift risk FR-012's raw-JSON editor makes real (edit the JSON, and a cached column silently
stops matching it unless every write path remembers to keep it in sync). Instead, `GetMachineProfilesQuery`/
`GetMachineProfileQuery` and their filament equivalents compute these fields on every read, by running
the same per-`SourceFormat` field-extraction logic `IProfileBundleParser` already needs internally to
build `ParsedMachinePreset`/`ParsedFilamentPreset` at preview time (`research.md` §2) — one extraction
function, reused at both preview-from-archive and read-from-stored-row call sites, never duplicated.

This means a raw-JSON edit (FR-012) takes effect on these fields automatically and immediately — there
is no "re-derive and cache" step for `UpdateMachineProfileRawDataCommand`/
`UpdateFilamentProfileRawDataCommand` to get right or get wrong; those handlers now do nothing beyond
validating well-formed JSON (FR-013) and overwriting `RawDataJson`.

**Machine profile derived fields** (API response shape, `contracts/profiles-api.md`): `nozzleSizeMm`,
`bedSizeXMm`/`bedSizeYMm`, `maxHeightMm`, `hasEnclosure`, `supportedMaterials`, `supportsLiveIntegration`
(the last one still the same best-effort `PrinterModelCatalog` cross-reference from `research.md` §8,
just computed on read instead of cached on write — nullable/`false`-defaulting exactly as before when
extraction or matching doesn't resolve).

**Filament profile derived fields**: `material`, `brand`, `colorHex`, `nozzleSizeMm`, `nozzleTempC`,
`bedTempC`, `maxVolumetricSpeedMm3s`, `fanPercent`.

None of these are queryable/filterable/sortable at the database level as a result (no FR asks for
that — US3 only requires them to be *displayed*). If a later phase needs to filter or sort the catalog
by one of these (e.g. "find all 0.4mm machine profiles"), promoting that specific field back to a real
indexed column is a small, contained migration at that point — not something to build speculatively now
without a real requirement driving it (constitution Principle IV).

### Validation rules (both tables)

- `Name` required, non-empty — a preset with no name is not a valid import candidate and is excluded
  from the preview list entirely (treated as unparseable for that one entry, not a whole-bundle
  rejection — FR-007 is about the *bundle* being unrecognizable, not one malformed preset inside an
  otherwise-good bundle).
- `RawDataJson` must be well-formed JSON at all times — enforced both at import (the parser only ever
  produces well-formed JSON from a successfully-parsed preset) and at edit time (FR-013 — the raw JSON
  editor's save endpoint parses the submitted string before persisting; a parse failure rejects the
  save with a 422 and leaves the stored row untouched).
- `BaseProfileId`, when set, must reference a row of the **same** table/type and the **same**
  `OrganizationId` — cross-tenant or cross-type self-references are a programming error, not a possible
  user input (there's no endpoint that could construct one), so this is enforced by the resolution
  algorithm's own lookup scoping (`research.md` §3), not a separate DB constraint beyond the FK itself.
- Neither table enforces a uniqueness constraint on `Name` — FR-009's duplicate handling is an
  *advisory* check surfaced at preview time (`research.md` §5), not a hard constraint, since the spec
  explicitly allows importing a duplicate as a deliberate separate entry.

## Not modeled as persisted entities

- **Import Batch** — spec.md's Key Entities calls this out as "conceptual, not necessarily a
  user-facing entity." Per `research.md` §4, it is not persisted at all — the preview response *is* the
  batch, held client-side between preview and confirm.
- **`ProfileSourceFormat` value** — modeled as a plain enum (`OrcaSlicer`, `BambuStudio`), not a lookup
  table; matches the existing `PrinterModel.Brand` enum precedent (`specs/002-printer-integration/`)
  rather than inventing a second small-catalog pattern for one field.
- **Every descriptive/spec field** (nozzle size, bed volume, enclosure, supported materials,
  live-integration flag, material, brand, color, tuned settings) — see "Derived display fields" above.
  `RawDataJson` is their only persisted form.

## EF Core configuration notes

- Both tables get `builder.IsMultiTenant()` (Finbuckle global query filter), same as `Printers` and
  `ConnectionSecrets` — no hand-rolled tenant `WHERE` clause, per the constitution's Security &
  Multi-Tenancy Isolation section.
- `RawDataJson` is stored as `jsonb` (typed `string` in C#, not a POCO) since its shape is
  preset-defined and arbitrary — there's deliberately no strongly-typed C# model for "a slicer preset's
  full settings," and (per "Derived display fields" above) no typed columns shadowing pieces of it
  either. This is the direct data-layer expression of FR-014 (no structured per-field forms this
  phase). Net effect: each table is genuinely small — `Id`, `OrganizationId`, `Name`, `SourceFormat`,
  `BaseProfileName`, `BaseProfileId`, `RawDataJson`, `SourceFileName`, `CreatedAtUtc`, nothing else —
  which also means neither table needs the `jsonb`-array `ValueComparer` boilerplate
  `PrinterConfiguration.cs` needed for `Printer.AmsUnits` (there's no typed collection column here to
  compare).
- One migration, `AddProfiles`, adds both tables together (single schema-coherent change) per the
  constitution's "one EF Core migration per PR when the schema changes" rule.

## Transient (non-persisted) shapes used by the import flow

These exist only in `Application`/`Api` DTOs and the frontend's in-memory state between preview and
confirm — never a database row (`research.md` §4).

### `ParsedMachinePreset` / `ParsedFilamentPreset` (preview response items)

Carries `Name`, `SourceFormat`, `BaseProfileName`, and `RawDataJson` (the real columns a confirmed
import would persist), plus every "Derived display field" above — computed fresh during this parse
pass by the same extraction logic a stored-row read would use later, so the review list shows the
identical values the catalog will show after import — plus:

| Field | Type | Notes |
|---|---|---|
| `IsDuplicate` | `bool` | Set by the preview handler per `research.md` §5. |
| `IsBaseSelectableInBundle` | n/a — **not a backend field** | FR-006's "base present in bundle but not selected" warning is computed entirely client-side from the full preview array + live selection state (`research.md` §4) — deliberately not a server-computed field, since it changes on every checkbox click and round-tripping to the server for it would violate Constitution Principle V (no needless network round-trips for something derivable from data already in hand). |

This full shape exists **only** for the preview response, to render the review-list UI. It is never
sent back to the server — confirm identifies presets by key alone (below), and the server re-derives
every one of these fields itself from a fresh parse (`research.md` §4's revision note). Keeping this
distinction sharp is the point of the split: one shape is "what the review list displays," the other is
"what the operator decided," and only the second one crosses back over the wire.

### `SelectedProfileKey` (confirm request items — deliberately minimal)

Per `research.md` §4, confirm re-uploads the original file and re-parses it server-side rather than
trusting client-supplied preset content, so the request only needs enough to (a) identify which
already-parsed presets to keep and (b) carry the one genuine decision the server can't re-derive — the
duplicate resolution choice:

| Field | Type | Notes |
|---|---|---|
| `Type` | enum: `Machine` \| `Filament` | Which catalog/array the `Name` below is scoped to. |
| `Name` | `string` | Matched against the freshly re-parsed bundle's preset names within `Type`. A key with no match in the re-parse is a 422 (`research.md` §4's consistency check), not a silently skipped item. |
| `DuplicateAction` | enum: `Skip` \| `ImportAsNew` | Required only when this preset was flagged `IsDuplicate: true` in the preview response; ignored otherwise. `Skip` means the handler does not create a row for this item at all. |

No typed spec fields, no `RawDataJson`, no `BaseProfileName` — all of that is re-derived server-side
from the re-uploaded file using the same `IProfileBundleParser` call preview used, never taken from the
request body.
