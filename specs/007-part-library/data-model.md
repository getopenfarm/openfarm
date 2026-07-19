# Phase 4 Data Model: Part Library (with Sliced Files & In-App Slicing)

Supersedes the previous version of this file — `PartSlicingJob` is renamed `PartProcessingJob` and its
lifecycle now applies uniformly to every file type (`research.md` §2/§3): there is no longer a synchronous,
direct-save path for an already-sliced file. Narrow filament overrides move entirely to the Confirm step for
every case, as an array (`research.md` §7). Six new tables (`Tags`, `Parts`, `PartFilamentRequirements`,
`PartTags`, `PartProcessingJobs`) plus two small additions to existing tables (`Printers.MachineProfileId`,
`Organizations.DefaultSlicerEngine`).

## `Part` (new table)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Tenant column (`IsMultiTenant()`). |
| `Name` | `string` | Required, non-empty. Renameable independently of file replacement. |
| **File fields** (embedded directly — a Part always has exactly one File, never a collection) | | |
| `FileFormat` | enum: `Stl` \| `ThreeMf` \| `GCode` | Determined at upload from the file's own extension/content — never operator-selected. |
| `RequiresSlicing` | `bool` | `Stl` → always `true`. `GCode` → always `false`. `ThreeMf` → determined by `IPartFileClassifier` inspecting embedded metadata (FR-003). Determines which processor (`ISlicerEngine` vs. `IAlreadySlicedFileMetadataParser`) `IPartFileProcessor` delegates to — it no longer determines the API/UI shape, since every file type now goes through the same Processing → Ready for Review → Confirm pipeline. |
| `OriginalFileName` | `string` | As uploaded, for display/re-download. |
| `StoragePath` | `string` | Opaque key resolved by `IPartFileStorage` (`research.md` §8) — never exposed to the frontend directly; downloads go through an API endpoint. |
| `FileSizeBytes` | `long` | |
| `FileUploadedAtUtc` | `DateTimeOffset` | Updated on every replace. |
| **Derived, always-complete print data** (FR-006/FR-011/FR-013 — never partially populated; a row only exists once all of these are successfully derived and the operator has confirmed) | | |
| `NozzleDiameterMm` | `decimal` | Parsed from an already-sliced file, or the farm's auto-detected value used to drive the in-app slice (`research.md` §5). |
| `EstimatedPrintTimeMinutes` | `int` | `> 0`. Whole-print total. |
| `EstimatedFilamentWeightGrams` | `decimal` | `> 0`. Whole-print total. |
| **Slicing configuration** (only meaningful when `RequiresSlicing = true`; ignored otherwise) | | |
| `SlicerEngineOverride` | enum `SlicerEngine?` (`OrcaSlicer` \| `BambuStudio`) | Per-part override of the organization default (FR-008). `null` → the org default (`Organization.DefaultSlicerEngine`) applies. |
| **Thumbnail** (FR-014/FR-014a — best-effort, never blocks saving) | | |
| `ThumbnailBytes` | `byte[]?` | Stored inline (`bytea`), always PNG when present (`research.md` §8 — normalized at ingestion, no content-type column needed). `null` if derivation failed or wasn't possible. |
| `CreatedAtUtc` | `DateTimeOffset` | |
| `UpdatedAtUtc` | `DateTimeOffset` | Bumped whenever a `PartProcessingJob` is confirmed against this part, or its name/tags change. |
| *(xmin)* | Postgres system column | `UseXminAsConcurrencyToken()` — guards the concurrent-replace edge case, the same technique `Spool` already uses. |

No `IsJobReady`/status field of any kind — by construction, every row in `Parts` is fully valid and usable.
The only remaining status distinction a part carries is `RequiresSlicing` itself, and that now only affects
which processor produced the data, not any operator-visible workflow shape.

## `PartFilamentRequirement` (new table — one of a Part's one-or-more material needs, `research.md` §7)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `PartId` | `Guid` (FK → `Parts.Id`) | `ON DELETE CASCADE`. |
| `SlotIndex` | `int?` | `null` for a single-material requirement (the overwhelmingly common case). Set to the file's own detected AMS-style slot index only when an already-sliced file's metadata declares a genuine multi-color mapping — one row per detected slot in that case. |
| `MaterialType` | `string` | Free text, e.g. `"PETG"` — the same untyped-string convention as `Filament.MaterialType`. |
| `NarrowFilamentVariantId` | `Guid?` | Bare, unenforced reference to `FilamentVariant` (`research.md` §7). Mutually exclusive with `NarrowFilamentProfileId`. Set only on `Confirm` (never at initial submit — every narrow override for every requirement is supplied at the same review/confirm moment, `research.md` §7). |
| `NarrowFilamentProfileId` | `Guid?` | Bare, unenforced reference to `FilamentProfile`. Mutually exclusive with `NarrowFilamentVariantId`. Same confirm-time-only timing as above. |
| `CreatedAtUtc` | `DateTimeOffset` | |

Unique per `(PartId, SlotIndex)` where `SlotIndex IS NOT NULL` (a partial unique index); `Application`-layer
validation enforces "exactly one `SlotIndex = null` row unless a genuine multi-slot mapping was parsed."

**The operator's slicing-target Filament selection is not stored as a column anywhere** — for a
requires-slicing add/replace, `filamentId` is transient input to `PartProcessingJob` (below), consumed by
`ISlicerEngine` via `IPartFileProcessor` and never persisted past the job; only the *resulting* derived
`PartFilamentRequirement` row(s) are saved once the job is confirmed.

## `Tag` (new table)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `OrganizationId` | `Guid` | Tenant column. |
| `Name` | `string` | Required, non-empty. Unique per `(OrganizationId, Name)`. |
| `CreatedAtUtc` | `DateTimeOffset` | |

Get-or-created by name when an operator tags a part (`research.md` §9).

## `PartTags` (new join table)

| Field | Type | Notes |
|---|---|---|
| `PartId` | `Guid` (FK → `Parts.Id`) | `ON DELETE CASCADE`. |
| `TagId` | `Guid` (FK → `Tags.Id`) | `ON DELETE CASCADE`. |

Composite PK `(PartId, TagId)`.

## `PartProcessingJob` (new table — Processing → Ready for Review → Confirm/Discard tracking, `research.md` §2/§3)

Renamed from `PartSlicingJob` — it now covers *every* add/replace, whatever the file format, not just
requires-slicing uploads.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | Returned to the client in the `202 Accepted` response as the poll/subscribe/confirm/discard key. |
| `OrganizationId` | `Guid` | Tenant column. |
| `PartId` | `Guid?` | `null` until an **Add** is confirmed; set immediately for a **Replace** (the part already exists). Bare reference, no FK. |
| `Kind` | enum: `Add` \| `Replace` | |
| `Status` | enum: `Processing` \| `ReadyForReview` \| `Confirmed` \| `Failed` \| `Discarded` | `Processing` is the only non-terminal state, entered for **every** file type — for an already-sliced file it typically resolves to `ReadyForReview` almost immediately (no external process involved), but the state machine and API shape are identical either way. |
| `RequiresSlicing` | `bool` | Snapshotted from `IPartFileClassifier` at job creation — tells `PartFileProcessingBackgroundService` (via `IPartFileProcessor`) which underlying mechanism to use; not otherwise operator-visible. |
| `SlicerEngineUsed` | enum `SlicerEngine?` | The resolved engine actually invoked, when `RequiresSlicing = true`; `null` for an already-sliced file. |
| `TargetNozzleDiameterMm` | `decimal?` | Snapshotted from `NozzleDiameterResolver` at job creation, when `RequiresSlicing = true`. `null` for an already-sliced file (its nozzle diameter comes from parsing, not a slicing target). |
| **Preview fields** (`null` until `Status = ReadyForReview`; the source of what the operator reviews) | | |
| `PreviewNozzleDiameterMm` | `decimal?` | |
| `PreviewEstimatedPrintTimeMinutes` | `int?` | |
| `PreviewEstimatedFilamentWeightGrams` | `decimal?` | |
| `PreviewThumbnailBytes` | `byte[]?` | Always PNG when present. |
| `ErrorMessage` | `string?` | Set only on `Failed`, surfaced to the operator (FR-006/edge cases). |
| `RequestedByUserId` | `Guid?` | Captured at submission time — used to attribute a `Failed` job's direct activity-log write (`research.md` §3). |
| `CreatedAtUtc` | `DateTimeOffset` | |
| `ProcessedAtUtc` | `DateTimeOffset?` | Set when `Status` becomes `ReadyForReview` or `Failed`. |
| `ResolvedAtUtc` | `DateTimeOffset?` | Set when `Status` becomes `Confirmed` or `Discarded`. |

## `PartProcessingJobPreviewFilamentRequirement` (new table — the reviewable, not-yet-persisted filament requirement(s) for a job)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `PartProcessingJobId` | `Guid` (FK → `PartProcessingJobs.Id`) | `ON DELETE CASCADE`. |
| `SlotIndex` | `int?` | Same meaning as `PartFilamentRequirement.SlotIndex`. |
| `MaterialType` | `string` | Populated once `Status = ReadyForReview`. |

Populated once, at the moment a job reaches `ReadyForReview` (one row per detected/derived requirement — a
single row for an in-app slice, one per detected slot for a multi-color already-sliced file). The operator
attaches narrow overrides to these rows via the `Confirm` request (keyed by `slotIndex`); on `Confirm`, each
row is copied into a real `PartFilamentRequirement` (with whichever narrow override the operator supplied)
and the job's own copies become historical only.

No history/versioning beyond the current row on any of these tables — once terminal, a `PartProcessingJob`
is a completed audit record. A job never restarts; a discarded or failed add/replace requires a brand-new
request.

## `Printer.MachineProfileId` (new column on the existing `Printers` table)

| Field | Type | Notes |
|---|---|---|
| `MachineProfileId` | `Guid?` | Enforced FK → `MachineProfiles.Id`, `ON DELETE SET NULL` (`research.md` §6). `null` at printer-creation time by default (FR-005b). |

Only a printer with this set counts as a "real fleet member" for `NozzleDiameterResolver` (FR-005a).

## `Organization.DefaultSlicerEngine` (new column on the existing `Organizations` table)

| Field | Type | Notes |
|---|---|---|
| `DefaultSlicerEngine` | enum: `OrcaSlicer` \| `BambuStudio` | Default `OrcaSlicer` (FR-007). The only organization-level slicing setting this phase introduces (`research.md` §10). |

## Relationships at a glance

```text
Organization 1───* Printer *───1 MachineProfile   (new optional link, FR-005b)
Organization 1───1 (DefaultSlicerEngine column, not a separate table)

Organization 1───* Part
Part 1───* PartFilamentRequirement
PartFilamentRequirement *───1 FilamentVariant   (NarrowFilamentVariantId, bare reference, optional, mutually exclusive with below)
PartFilamentRequirement *───1 FilamentProfile   (NarrowFilamentProfileId, bare reference, optional, mutually exclusive with above)
Part *───* Tag                       (via PartTags)
Part 1───* PartProcessingJob         (0 or 1 currently-non-terminal job at a time in practice)
PartProcessingJob 1───* PartProcessingJobPreviewFilamentRequirement   (populated once, at ReadyForReview)
```

No relationship anywhere in this phase points from `Part` to `MachineProfile` directly.

## Validation rules (FluentValidation, `Application` layer — TDD per `research.md` §12)

- Uploaded file: extension must resolve to `Stl`/`ThreeMf`/`GCode` via `IPartFileClassifier`, else `400`
  — checked synchronously at submit, before a job is created. `Name` is **not** validated at submit time —
  it isn't part of the submit request at all (FR-001/FR-009b).
- `filamentId` (transient slicing-target input) required when the classified file `RequiresSlicing`; else
  `400` — also checked before job creation.
- Requires-slicing path additionally requires `NozzleDiameterResolver` to return a non-null value at submit
  time (else `422`, FR-006b) — checked before a `PartProcessingJob` is even created.
- On `Confirm`: `Name` required, non-empty, reasonable max length, **for an `Add`-kind job only** — a
  `Replace`-kind job's confirm request MUST NOT include `name`/`tags` (`400` if it does, since a replace
  never touches them, FR-004). At most one of `NarrowFilamentVariantId` / `NarrowFilamentProfileId` may be
  set per requirement override entry in the request array; each entry's `slotIndex` must match one of the
  job's preview requirements.
- A `Confirm`/`Discard` action is only valid against a job whose `Status = ReadyForReview` (else `409`) —
  the frontend's Save control mirrors this: disabled while `Status != ReadyForReview`, and, for an `Add`,
  additionally disabled until a name has been entered client-side (FR-009b).
- Tag names: required non-empty when tagging; get-or-create is case-sensitive-exact-match per
  `(OrganizationId, Name)`.

## State/lifecycle notes

Every add/replace, regardless of `FileFormat`, follows the same shape:

1. **Submit** (file + `filamentId?` only — no `name`/`tags`, FR-001/FR-009b): `IPartFileClassifier` runs
   synchronously to determine `RequiresSlicing` and validate `filamentId`/nozzle-diameter-availability as
   applicable (`400`/`422` here, no job created if these fail). Otherwise, create
   `PartProcessingJob{Status=Processing}` (`PartId=null` for Add, or the existing part's id for Replace),
   enqueue, return `202`. The frontend opens its add/replace dialog at this point and lets the operator keep
   editing name/tags locally while the steps below run in the background.
2. **Background processing**: `PartFileProcessor` delegates to `ISlicerEngine` (if `RequiresSlicing`) or
   `IAlreadySlicedFileMetadataParser` (if not). On success: populate preview fields + one-or-more
   `PartProcessingJobPreviewFilamentRequirement` rows, `Status=ReadyForReview`, `SignalR` push (the frontend
   enables Save once this arrives *and* a name has been entered, for an Add). On failure: `Status=Failed` +
   `ErrorMessage`, direct activity-log write, `SignalR` push. The existing `Part` (for a Replace) or nothing
   (for an Add) is touched at any point during this step.
3. **Confirm** (the Save action): `POST .../confirm` with `{ name (Add only, required), tags? (Add only),
   filamentRequirementOverrides?: [{slotIndex, narrowFilamentVariantId?, narrowFilamentProfileId?}] }`.
   Creates the `Part` (Add) or replaces its file/derived fields/`PartFilamentRequirement`s (Replace)
   transactionally from the job's preview data plus the supplied name/tags/narrow overrides, in one
   transaction; `Status=Confirmed`; a replace's `name`/`tags` fields, if somehow supplied, are rejected
   rather than silently ignored (`400`). `201`/`200`.
4. **Discard** (the Cancel action, at any point — while `Processing` or once `ReadyForReview`):
   `DELETE .../confirm`. `Status=Discarded`; no `Part` created/changed. `204`.
- **Delete a part**: removes the `Part` row (cascades `PartTags` and `PartFilamentRequirement`s) and calls
  `IPartFileStorage.DeleteAsync`. `PartProcessingJob` rows are historical and simply retain a now-dangling,
  unenforced `PartId` once terminal.
