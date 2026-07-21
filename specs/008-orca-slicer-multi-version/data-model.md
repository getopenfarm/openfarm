# Phase 1 Data Model: Multi-Engine, Multi-Version Slicer Management

Derives the entities, value types, and persistence deltas from the spec's Key Entities and Functional
Requirements. New value types live in `OpenFarm.Domain.Parts`; persistence changes are one hand-authored
EF migration (`AddSlicerVersionTracking`). The **bundled slicer set is filesystem-only** — nothing about
available slicers is persisted; only *selections* and *recorded results* touch the database.

## Domain value types & enums (new / changed)

### `SlicerEngine` (enum — changed)

```
OrcaSlicer | BambuStudio | SuperSlicer     // + SuperSlicer added (FR-001/FR-022)
```

Persisted as `text` via `HasConversion<string>()` (existing convention). Adding the member is additive —
existing `"OrcaSlicer"`/`"BambuStudio"` values are unaffected.

### `SlicerPlatform` (enum — new)

```
Linux | Windows | MacOs
```

The host platform a slicer applies to (FR-009/FR-010). Resolved for the running host by
`Infrastructure.Parts.HostPlatform` (`RuntimeInformation.IsOSPlatform`). Not persisted on entities (a
recorded slice's platform is implicitly the host that produced it); carried on discovered/catalog entries
and the manifest.

### `SlicerRef` (value object — new)

A validated *selection identity*: `(SlicerEngine Engine, string Version)`. Used for the org default, the
per-part override input, and the resolved slice target. `Version` is the exact upstream string, verbatim.
Equality is by (engine, ordinal-version-string). Not itself persisted as a unit — stored as two columns
(engine enum + version string) wherever a selection/result is recorded.

**Validation rules**: `Version` non-empty; an instance is only *usable* once checked against the catalog
(presence + host platform) by `ResolveSlicerSelection` (FR-007/FR-010) — the type itself carries no
availability guarantee.

### `AvailableSlicer` (value object — new, not persisted)

A discovered catalog entry: `(SlicerEngine Engine, string Version, SlicerPlatform Platform)` plus a
backend-internal, never-exposed executable location resolved within its bundle leaf. Availability = the
leaf exists (FR-002); no execution/validation. Two leaves resolving to the same `(engine, version,
platform)` are de-duplicated deterministically (stable order by resolved path) with a logged warning
(spec Edge Cases).

### `SlicerVersionComparer` (domain service — new)

Total order over version strings **within one engine** for "newest" (FR-003). Parses dotted numeric
components (variable length: `2.2.0`, `2.5.59.13`, `01.10.00.00`), compares component-wise (shorter
zero-padded), non-numeric suffix orders before the clean release, lexical only as tiebreak. Pure,
deterministic → TDD.

## Aggregate / entity changes

### `Organization` (changed)

| Field | Change | Notes |
|-------|--------|-------|
| `DefaultSlicerEngine : SlicerEngine` | kept, semantics narrowed | now the engine half of an **optional** default pair |
| `DefaultSlicerVersion : string?` | **new** | version half; `NULL` ⇒ no explicit default set → newest-available fallback (§5) |

- The *effective* default is **computed at read time** against the catalog, never persisted as a resolved
  value (research §5): explicit-and-present → else OrcaSlicer-newest → else any-engine-newest → else none.
- Behavior: `SetDefaultSlicer(SlicerRef)` replaces `SetDefaultSlicerEngine(engine)`; setting is gated by
  `ResolveSlicerSelection` so an unavailable pair is rejected (FR-007) and the change is activity-logged
  (FR-019). Org-scoped (`organization_id`) though one org exists today (FR: per-tenant default).
- Because the set is append-only (FR-008a), a persisted explicit default can never become invalid.

### `PartProcessingJob` (changed)

| Field | Change | Notes |
|-------|--------|-------|
| `SlicerEngineUsed : SlicerEngine?` | kept | already present |
| `SlicerVersionUsed : string?` | **new** | exact version recorded at slice time (FR-005) |

Set together from the resolved `SlicerRef` when the requires-slicing job is created / sliced. `NULL` for
files that never slice (already-sliced uploads) — unchanged from today for the engine field.

### `Part` (changed)

| Field | Change | Notes |
|-------|--------|-------|
| `SlicerEngineOverride : SlicerEngine?` | kept (intent clarified) | the engine actually produced with (the override was the input) |
| `SlicerVersionUsed : string?` | **new** | exact version actually produced with (FR-005), displayed for audit |

Recorded when the Part is created/replaced from a confirmed job. Historical rows (pre-feature) keep
`SlicerVersionUsed = NULL`, shown as legacy/unknown — no backfill (spec Assumptions). Changing the org
default never rewrites an existing Part's recorded pair (FR-008).

## Persistence delta — migration `AddSlicerVersionTracking`

Hand-authored (attribute-based, no `dotnet ef`), one migration for the whole cohesive delta:

```
Parts               ADD COLUMN "SlicerVersionUsed"      text NULL
PartProcessingJobs  ADD COLUMN "SlicerVersionUsed"      text NULL
Organizations       ADD COLUMN "DefaultSlicerVersion"   text NULL
```

- No new tables (the catalog is filesystem-only).
- `Down` drops the three columns.
- EF config: add `builder.Property(x => x.SlicerVersionUsed)` (plain nullable text) to
  `PartConfiguration`, `PartProcessingJobConfiguration`; add `DefaultSlicerVersion` to
  `OrganizationConfiguration`. Engine columns keep their existing `HasConversion<string>()`.
- Tenant scoping unchanged (`IsMultiTenant()` already applied on Parts/Jobs; Organization is the tenant
  root).

## Configuration delta (not database)

- **Removed**: `Slicing:OrcaSlicerCliPath`, `Slicing:BambuStudioCliPath` (the path knobs — FR-020).
- **Added**: `SlicerBundle:RootDirectory` (the scanned bundle root; backend-internal).
- **Kept**: `Slicing:MaxConcurrentSlicingOperations`, `Slicing:CliTimeout`, the fixed
  `GenericFallbackMaterialProfile` map (extended with a `SuperSlicer` entry).

## Provisioning artifacts (repo files, not runtime data)

- **`slicers.manifest.json`** — maintainer-authored desired state: a list of `(engine, version,
  platforms[])` entries (FR-012). Schema in `contracts/slicer-manifest.schema.json`.
- **`slicers.lock.json`** — generated resolved state: per `(engine, version, platform)` the resolved
  GitHub asset URL + SHA-256, for reproducible integrity verification (FR-014) and desired-vs-present
  reconciliation (FR-017).

## Lifecycle & state notes

- **Discovery** (startup, once): scan `RootDirectory` → immutable `AvailableSlicer` set in the singleton
  catalog. No status; presence = available.
- **Selection resolution** (per default-set and per slice): `ResolveSlicerSelection(requested?) →
  SlicerRef` or typed failure (`SlicerUnavailableException` → RFC 7807). Requested = explicit override,
  else effective default.
- **Slice recording**: on success the resolved `SlicerRef` is written to the job and, on confirm, the
  Part. Failure routes through the existing `SlicingFailedException → MarkFailed` terminal path (no new
  non-terminal state; Constitution V preserved).
