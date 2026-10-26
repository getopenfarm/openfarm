# Phase 4 Data Model: Part Library (with Sliced Files & In-App Slicing)

Supersedes the previous version of this file — `PartSlicingJob` is renamed `PartProcessingJob` and its
lifecycle now applies uniformly to every file type (`research.md` §2/§3): there is no longer a synchronous,
direct-save path for an already-sliced file. Narrow filament overrides move entirely to the Confirm step for
every case, as an array (`research.md` §7). A job's preview filament requirement(s) are stored as a `jsonb`
column directly on `PartProcessingJob` rather than a separate child table, mirroring `Printer.AmsUnits`'s
existing pattern for a small, bounded, never-independently-queried value-object list (`research.md` §3) —
one fewer table than the previous revision of this file had. `PartFilamentRequirement` also drops `SlotIndex`
entirely: a requirement is now identified by its detected `(MaterialType, DetectedColorHex)` pair, grouped
across **every** plate and AMS slot in the file (not just the first plate) — a partial reopening of the
earlier "multi-plate 3MF is an accepted, unaddressed limitation" decision, scoped specifically to
filament-requirement detection (`research.md` §7); `Part`'s estimated print time/weight are now explicitly
the sum across every plate. `PartFilamentRequirement.MaterialType` (and its preview counterpart) is also now
`string?`, not a required `string` — `null` represents a genuine "any material" wildcard (`research.md` §7a).

**A sixth follow-up round supersedes three more things, further to the above:**

1. **`filamentId` is removed from `PartProcessingJob` entirely** (and from every request that used to carry
   it) — material is now always resolved from the file itself (`research.md` §1/§7a/§13), never
   operator-supplied. "Any material" (`MaterialType = null`) is now reachable only for an STL.
2. **`Part.ThumbnailBytes`/`PartProcessingJob.PreviewThumbnailBytes` (single columns) are replaced by
   `PartThumbnail`/`PartProcessingJobPreviewThumbnail` (new per-plate child tables)** plus a new
   `Part.PlateCount`/`PartProcessingJob.PreviewPlateCount` field — every plate of a multi-plate file now gets
   its own derived thumbnail, not only the first (`research.md` §8).
3. **`Part.EstimatedPrintTimeMinutes`/`EstimatedFilamentWeightGrams` become nullable, gated by a new
   `Part.EstimateStatus` enum (`Resolving | Available | Unavailable`)**, and `PartProcessingJob` gains the
   equivalent `PreviewEstimateStatus` (`Resolving | Available`) — nozzle diameter and material now resolve,
   and gate `ReadyForReview`/save, independently of the estimate and thumbnails for a requires-slicing file;
   a background continuation fills the estimate in afterward, targeting whichever of the job's preview or
   the already-confirmed `Part` currently holds the truth (`research.md` §3b). This is the one legitimate
   way a `Part` can carry incomplete derived data in this phase.

Six new tables (`Tags`, `Parts`, `PartFilamentRequirements`, `PartTags`, `PartProcessingJobs`,
`PartThumbnails`), one new small child table for job previews (`PartProcessingJobPreviewThumbnails`), plus
two small additions to existing tables (`Printers.MachineProfileId`, `Organizations.DefaultSlicerEngine`).

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
| **Always-resolved compatibility data** (FR-006/FR-011 — never partially populated; a row only exists once these are successfully resolved and the operator has confirmed, whether or not the estimate below has finished, `research.md` §3b) | | |
| `NozzleDiameterMm` | `decimal` | Parsed from an already-sliced file, or the farm's auto-detected value used to drive the in-app slice (`research.md` §5). |
| **Estimate** (FR-013 — MAY still be resolving at the moment the row is created, for a requires-slicing file confirmed early; `research.md` §3b) | | |
| `EstimateStatus` | enum: `Resolving` \| `Available` \| `Unavailable` | `Available` immediately for a file that doesn't require slicing (parsed atomically alongside `NozzleDiameterMm`/material — `research.md` §1). For a requires-slicing file, starts `Resolving` at row creation if the operator confirmed before the slice finished (FR-009c/FR-009d); a trailing background continuation (`research.md` §3b) flips it to `Available` (populating the two fields below) or, on a deferred slicing failure, `Unavailable` — permanently, with no retry in this phase. |
| `EstimatedPrintTimeMinutes` | `int?` | `> 0` when set. `null` while `EstimateStatus != Available`. The part's plate's own estimate (`PlateNumber`); a legacy part without a plate keeps the sum across every plate. |
| `EstimatedFilamentWeightGrams` | `decimal?` | `> 0` when set. `null` while `EstimateStatus != Available`. The part's plate's own, same rule as above. |
| **Slicing configuration** (only meaningful when `RequiresSlicing = true`; ignored otherwise) | | |
| `SlicerEngineOverride` | enum `SlicerEngine?` (`OrcaSlicer` \| `BambuStudio`) | Per-part override of the organization default (FR-008). `null` → the org default (`Organization.DefaultSlicerEngine`) applies. |
| **Thumbnails** (FR-014/FR-014a — per plate, best-effort, never blocks saving; `research.md` §8) | | |
| `PlateNumber` | `int?` | `>= 1`. The plate of the file this part prints (a part is one plate), chosen at review. Always `1` for an STL or bare G-code. `null` for a part confirmed before plates were chosen: it prints plate 1, else the file's only sliced plate. |
| `PlateName` | `string?` | The slicer's name for that plate (`plater_name`), when it has one. |
| *(see `PartThumbnail`, new child table below)* | | The plate's thumbnail, `PlateIndex = PlateNumber - 1`; the lowest `PlateIndex` is the catalog/list-view thumbnail. |
| `CreatedAtUtc` | `DateTimeOffset` | |
| `UpdatedAtUtc` | `DateTimeOffset` | Bumped whenever a `PartProcessingJob` is confirmed against this part, its name/tags change, or the `research.md` §3b trailing continuation resolves/fails its estimate. |
| *(xmin)* | Postgres system column | `UseXminAsConcurrencyToken()` — guards the concurrent-replace edge case, the same technique `Spool` already uses. |

No `IsJobReady`/status field of any kind — by construction, every row in `Parts` has a fully resolved
nozzle diameter and material the moment it exists. `EstimateStatus` is the one narrow exception where a
saved row can legitimately be incomplete (`Resolving`) or permanently short a value (`Unavailable`) —
everything else about the row is exactly as complete as before.

## `PartThumbnail` (new child table — one row per plate that successfully produced a thumbnail, `research.md` §8)

| Field | Type | Notes |
|---|---|---|
| `PartId` | `Guid` (FK → `Parts.Id`) | `ON DELETE CASCADE`. |
| `PlateIndex` | `int` | `0`-based, `< Part.PlateCount`. |
| `Bytes` | `byte[]` | Always PNG (normalized at ingestion — `research.md` §8). Never `null`; a plate with no derivable thumbnail simply has no row for it, rather than a row with `null` bytes. |

Composite PK `(PartId, PlateIndex)`. Populated once, when a `PartProcessingJob` is confirmed (copied from
`PartProcessingJobPreviewThumbnail`, below) or when the `research.md` §3b trailing continuation resolves a
requires-slicing part's per-plate renders after an early confirm.

## `PartFilamentRequirement` (new table — one of a Part's one-or-more material needs, `research.md` §7)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (PK) | |
| `PartId` | `Guid` (FK → `Parts.Id`) | `ON DELETE CASCADE`. |
| `MaterialType` | `string?` | Free text, e.g. `"PETG"` — the same untyped-string convention as `Filament.MaterialType`. Part of the requirement's grouping/dedup identity (`research.md` §7) together with `DetectedColorHex`. `null` means fully unconstrained — "any material" (`research.md` §7a) — and can only arise from a requires-slicing file added with no Filament specified; an already-sliced file's requirement always has a real, non-null value (FR-006c rejects an unparseable one outright). |
| `DetectedColorHex` | `string?` | Best-effort, parsed from the file's own metadata (e.g. Bambu's `filament_colour` G-code header field). `null` if not parseable — never blocks saving (unlike `MaterialType`/estimates). Purely informational + grouping data; **never itself a matching criterion** (`research.md` §7) — matching still runs on `MaterialType` alone, or the narrow override if one is set. |
| `DetectedSubBrand` | `string?` | Best-effort, e.g. `"Bambu PETG-CF"` — mirrors `AmsTray.SubBrand`'s existing shape (Phase 1/3 telemetry). Purely informational, not part of the grouping key. |
| `NarrowFilamentVariantId` | `Guid?` | Bare, unenforced reference to `FilamentVariant` (`research.md` §7). Mutually exclusive with `NarrowFilamentProfileId`. Set only on `Confirm` (never at initial submit — every narrow override for every requirement is supplied at the same review/confirm moment, `research.md` §7). Applies to every plate/slot the file detected as sharing this requirement's `(MaterialType, DetectedColorHex)` pair. |
| `NarrowFilamentProfileId` | `Guid?` | Bare, unenforced reference to `FilamentProfile`. Mutually exclusive with `NarrowFilamentVariantId`. Same confirm-time-only timing as above. |
| `CreatedAtUtc` | `DateTimeOffset` | |

One row per **distinct `(MaterialType, DetectedColorHex)` pair** detected anywhere in the file — across
every plate and every AMS slot within each plate — not one row per slot or plate (`research.md` §7); a file
where multiple plates/slots share the same pair still produces exactly one row for that pair. A row with
`MaterialType = null` (the "any material" case, `research.md` §7a) is always the sole row for its part — an
in-app slice never produces more than one requirement regardless. No DB-level uniqueness constraint is
enforced across these columns (Postgres would treat every `NULL`-containing row as distinct from every
other by default, which doesn't usefully express "no duplicate pairs" once `MaterialType` itself can be
`null`) — deduplication is guaranteed entirely by `FilamentRequirementGrouper` (`research.md` §7, TDD per
§12) running once, at the `Application` layer, before any row is written; nothing downstream ever inserts a
second row for a pair a `Part` already has.

**There is no operator-supplied slicing-target Filament to store anywhere** — `filamentId` no longer exists
on any request (`research.md` §13). For a requires-slicing add/replace, the slicing target is resolved
internally, from the file itself (an unsliced 3MF's own project settings) or a fixed fallback (an STL) —
never a client-supplied value — and, like before, never persisted past the job; only the *resulting* derived
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
| `TargetMaterialType` | `string?` | The slicing target actually resolved for a requires-slicing file (`research.md` §13 — an unsliced 3MF's own project material) or the fixed fallback profile name (an STL, `research.md` §7a) — `null` for an already-sliced file. No longer a client-supplied `FilamentId`; that field is removed entirely (`research.md` §13). |
| **Preview fields — nozzle diameter/material populate as soon as `Status = ReadyForReview`; estimate/thumbnails may trail (`PreviewEstimateStatus`, `research.md` §3b)** | | |
| `PreviewNozzleDiameterMm` | `decimal?` | `null` until `Status = ReadyForReview`. |
| `PreviewEstimateStatus` | enum: `Resolving` \| `Available` | Set the moment `Status` becomes `ReadyForReview`. `Available` immediately for a file that doesn't require slicing (its estimate parses atomically alongside nozzle diameter/material). `Resolving` for a requires-slicing file, until the `research.md` §3b continuation completes — at which point, if the job is *still* `ReadyForReview`, this flips to `Available` and the two fields below populate; if the slice instead fails while still `ReadyForReview`, the whole job transitions to `Failed` (there is no `Unavailable` value at the job level — only `Part.EstimateStatus`, post-confirm, can end up permanently short a value). |
| `PreviewEstimatedPrintTimeMinutes` | `int?` | `null` while `PreviewEstimateStatus != Available`. |
| `PreviewEstimatedFilamentWeightGrams` | `decimal?` | `null` while `PreviewEstimateStatus != Available`. |
| `PreviewPlateCount` | `int?` | `null` until `Status = ReadyForReview`; `>= 1` after. Mirrors `Part.PlateCount` (`research.md` §8). |
| *(see `PartProcessingJobPreviewThumbnail`, new child table below)* | | Zero-to-`PreviewPlateCount` rows, populated as each plate's thumbnail becomes available — for an already-sliced file, all at once at `ReadyForReview`; for a requires-slicing file, potentially trailing alongside the estimate. |
| `PreviewFilamentRequirements` | `IReadOnlyList<PreviewFilamentRequirement>` (`jsonb`) | One entry per distinct `(MaterialType, DetectedColorHex)` pair `FilamentRequirementGrouper` derives across every plate/slot (`research.md` §7) — a single entry for an in-app slice, one per distinct pair for a multi-color/multi-plate already-sliced file. Each entry: `{ RequirementIndex: int, MaterialType: string?, DetectedColorHex: string?, DetectedSubBrand: string? }` — `MaterialType` is `null` only for the single-entry "any material" case, now STL-only (`research.md` §7a; never for a 3MF or G-code). `RequirementIndex` is a stable 0-based index (first-seen grouping order) that the `Confirm` request's override entries key against, replacing what `SlotIndex` would have done in a per-slot (rather than grouped) design. Populated the moment `Status` becomes `ReadyForReview` (does not wait on `PreviewEstimateStatus`). Stored as `jsonb` with `HasConversion`/a custom `ValueComparer`, the exact same technique `Printer.AmsUnits`/`Printer.ExternalSpoolTray` already use for a small, bounded, never-independently-queried value-object list (`research.md` §3) — **not** a separate child table. |
| `ErrorMessage` | `string?` | Set only on `Failed` (reachable from either `Processing` or, now, `ReadyForReview` — `research.md` §3), surfaced to the operator (FR-006/edge cases). |
| `RequestedByUserId` | `Guid?` | Captured at submission time — used to attribute a `Failed` job's direct activity-log write (`research.md` §3), and to attribute the `research.md` §3b trailing continuation's direct `Part` update when it targets an already-`Confirmed` job. |
| `CreatedAtUtc` | `DateTimeOffset` | |
| `ProcessedAtUtc` | `DateTimeOffset?` | Set when `Status` first becomes `ReadyForReview` (nozzle diameter/material resolved — no longer tied to the estimate) or `Failed`. |
| `ResolvedAtUtc` | `DateTimeOffset?` | Set when `Status` becomes `Confirmed` or `Discarded`. |

## `PartProcessingJobPreviewThumbnail` (new child table — mirrors `PartThumbnail` for a job's preview, `research.md` §8)

| Field | Type | Notes |
|---|---|---|
| `JobId` | `Guid` (FK → `PartProcessingJobs.Id`) | `ON DELETE CASCADE`. |
| `PlateIndex` | `int` | `0`-based, `< PartProcessingJob.PreviewPlateCount`. |
| `Bytes` | `byte[]` | Always PNG. |

Composite PK `(JobId, PlateIndex)`. On `Confirm`, every row is copied into `PartThumbnail` against the new
`Part.Id`; the job's own copy becomes historical only, same as `PreviewFilamentRequirements`.

`PreviewFilamentRequirements` is populated once, at the moment a job reaches `ReadyForReview`, by running
`FilamentRequirementGrouper` over whatever `IPartFileProcessor` derived. The operator attaches narrow
overrides via the `Confirm` request (keyed by `requirementIndex`); on `Confirm`, each entry is turned into a
real `PartFilamentRequirement` row (`MaterialType`/`DetectedColorHex`/`DetectedSubBrand` copied verbatim,
plus whichever narrow override the operator supplied) and the job's own JSON copy becomes historical only.

No history/versioning beyond the current row — once terminal, a `PartProcessingJob` is a completed audit
record. A job never restarts; a discarded or failed add/replace requires a brand-new request. **One
exception to "terminal means untouched"**: a `Confirmed` job's own row still never changes, but the
`research.md` §3b background continuation it kicked off may still be running past that point, writing its
eventual result onto the linked `Part` instead (never back onto the job row) — the job stays a frozen,
accurate record of what was reviewed and confirmed at that moment, not a live-updating one.

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
Part 1───* PartThumbnail             (0..PlateCount rows, PlateIndex 0 = catalog thumbnail)
PartFilamentRequirement *───1 FilamentVariant   (NarrowFilamentVariantId, bare reference, optional, mutually exclusive with below)
PartFilamentRequirement *───1 FilamentProfile   (NarrowFilamentProfileId, bare reference, optional, mutually exclusive with above)
Part *───* Tag                       (via PartTags)
Part 1───* PartProcessingJob         (0 or 1 currently-non-terminal job at a time in practice)
PartProcessingJob 1───* PartProcessingJobPreviewThumbnail   (0..PreviewPlateCount rows; copied into PartThumbnail on Confirm)
PartProcessingJob.PreviewFilamentRequirements   (jsonb column, not a table — populated once, at ReadyForReview)
```

No relationship anywhere in this phase points from `Part` to `MachineProfile` directly.

## Validation rules (FluentValidation, `Application` layer — TDD per `research.md` §12)

- Uploaded file: extension must resolve to `Stl`/`ThreeMf`/`GCode` via `IPartFileClassifier`, else `400`
  — checked synchronously at submit, before a job is created. `Name` is **not** validated at submit time —
  it isn't part of the submit request at all (FR-001/FR-009b). There is no `filamentId` field to validate —
  it no longer exists on the request (`research.md` §13).
- Requires-slicing path requires `NozzleDiameterResolver` to return a non-null value at submit time (else
  `422`, FR-006a) — checked before a `PartProcessingJob` is even created. This is unconditional now (it no
  longer varies by whether a `filamentId` was supplied, since none ever is).
- An unsliced 3MF whose `IUnslicedProjectMaterialResolver` result can't be parsed rejects the whole add/
  replace outright (`422`, FR-006) at the point processing reaches that step — **not** a synchronous
  submit-time check like the two above, since it requires opening and reading the file's own project
  settings, the same async-pipeline treatment already given to an already-sliced file's metadata parse
  (`research.md` §13). An STL never hits this check at all — it always resolves via the fixed fallback
  (`research.md` §7a).
- On `Confirm`: `Name` required, non-empty, reasonable max length, **for an `Add`-kind job only** — a
  `Replace`-kind job's confirm request MUST NOT include `name`/`tags` (`400` if it does, since a replace
  never touches them, FR-004). At most one of `NarrowFilamentVariantId` / `NarrowFilamentProfileId` may be
  set per requirement override entry in the request array; each entry's `requirementIndex` must match one of
  the job's preview requirements (`PreviewFilamentRequirements`).
- A `Confirm`/`Discard` action is only valid against a job whose `Status = ReadyForReview` (else `409`) —
  the frontend's Save control mirrors this: disabled while `Status != ReadyForReview`, and, for an `Add`,
  additionally disabled until a name has been entered client-side (FR-009b). Notably, `Confirm` no longer
  additionally requires `PreviewEstimateStatus = Available` — `ReadyForReview` itself is now reachable, and
  Confirm-able, before the estimate/thumbnails finish resolving for a requires-slicing job (`research.md`
  §3b).
- Tag names: required non-empty when tagging; get-or-create is case-sensitive-exact-match per
  `(OrganizationId, Name)`.

## State/lifecycle notes

Every add/replace, regardless of `FileFormat`, follows the same shape — now in two effective milestones
rather than one (`research.md` §3b):

1. **Submit** (file only — no `filamentId`, `name`, or `tags`, FR-001/FR-002/FR-009b): `IPartFileClassifier`
   runs synchronously to determine `RequiresSlicing`, and `NozzleDiameterResolver`'s availability is checked
   (`400`/`422` here, no job created if either fails). Otherwise, create `PartProcessingJob{Status=Processing}`
   (`PartId=null` for Add, or the existing part's id for Replace), enqueue, return `202`. The frontend opens
   its add/replace dialog at this point and lets the operator keep editing name/tags locally while the steps
   below run in the background.
2. **Background processing, milestone 1 — nozzle diameter/material**: `PartFileProcessor` resolves the
   target material — `IUnslicedProjectMaterialResolver` (unsliced 3MF, `research.md` §13), the fixed
   fallback (STL, `research.md` §7a), or nothing extra needed yet (already-sliced file — its material is
   part of the same parse as everything else, step 2b below). On success: populate
   `PreviewNozzleDiameterMm`/`PreviewFilamentRequirements`, `Status=ReadyForReview`,
   `PreviewEstimateStatus=Resolving` (requires-slicing) or proceed directly to 2b (already-sliced), `SignalR`
   push (the frontend enables Save once this arrives *and* a name has been entered, for an Add — it no
   longer waits on `PreviewEstimateStatus`). On failure (unparseable unsliced-3MF material): `Status=Failed`
   + `ErrorMessage`, direct activity-log write, `SignalR` push.
2b. **Background processing, milestone 2 — estimate/thumbnails** (`research.md` §3b): for an already-sliced
   file, this runs as part of the same atomic parse as step 2 (`IAlreadySlicedFileMetadataParser`, scanning
   every plate/slot and running `FilamentRequirementGrouper`) — success populates everything at once,
   `PreviewEstimateStatus=Available` from the start; failure here means step 2 itself failed (`Status=Failed`,
   as above), since parsing is all-or-nothing. For a requires-slicing file, `ISlicerEngine` runs as a
   continuation *after* step 2 already moved the job to `ReadyForReview` — on success, populate
   `PreviewEstimatedPrintTimeMinutes`/`PreviewEstimatedFilamentWeightGrams`/
   `PartProcessingJobPreviewThumbnail` rows, `PreviewEstimateStatus=Available`, `SignalR` push refreshing the
   open preview (`Status` stays `ReadyForReview`) — **unless the job has since become `Confirmed`**, in which
   case the same success instead writes directly onto the linked `Part`
   (`EstimatedPrintTimeMinutes`/`EstimatedFilamentWeightGrams`/`PartThumbnail` rows,
   `EstimateStatus=Available`) and pushes a `PartEstimateResolved` event instead. On failure: if still
   `ReadyForReview`, `Status=Failed` (retroactive rejection, FR-006/SC-007); if already `Confirmed`, instead
   `Part.EstimateStatus=Unavailable` (permanent) and a `PartEstimateUnavailable` event — the job row itself is
   never touched post-confirm either way.
3. **Confirm** (the Save action, available as soon as `Status=ReadyForReview` regardless of
   `PreviewEstimateStatus`): `POST .../confirm` with `{ name (Add only, required), tags? (Add only),
   filamentRequirementOverrides?: [{requirementIndex, narrowFilamentVariantId?, narrowFilamentProfileId?}] }`.
   Creates the `Part` (Add) or replaces its file/derived fields/`PartFilamentRequirement`s/`PartThumbnail`s
   (Replace) transactionally from whatever the job's preview holds *so far* — `EstimateStatus`/
   `EstimatedPrintTimeMinutes`/`EstimatedFilamentWeightGrams`/`PartThumbnail` rows are copied verbatim from
   `PreviewEstimateStatus`/the two preview estimate fields/`PartProcessingJobPreviewThumbnail`, whatever
   state they're currently in — a supplied override applies to the whole grouped requirement, i.e. every
   plate/slot the file detected as sharing that requirement's `(MaterialType, DetectedColorHex)` pair, in one
   transaction; `Status=Confirmed`; a replace's `name`/`tags` fields, if somehow supplied, are rejected rather
   than silently ignored (`400`). `201`/`200`. If `PreviewEstimateStatus=Resolving` at this moment, step 2b's
   continuation is still running and will complete against the now-created/updated `Part` per above.
4. **Discard** (the Cancel action, at any point — while `Processing` or once `ReadyForReview`):
   `DELETE .../confirm`. `Status=Discarded`; no `Part` created/changed. `204`. Best-effort-cancels an
   in-flight step-2b slice if one is still running; if it completes anyway, its result is simply dropped.
- **Delete a part**: removes the `Part` row (cascades `PartTags`, `PartFilamentRequirement`s, and
  `PartThumbnail`s). Assembly and production references block removal, including completed runs.
  Shared content-addressed files are retained until reference-aware garbage collection exists.
  `PartProcessingJob` rows are historical and
  simply retain a now-dangling, unenforced `PartId` once terminal; if a step-2b continuation is still
  in-flight against a job whose `Part` was deleted in the meantime, its write is a no-op (the `Part` no
  longer exists to update).

### Production references (October 13, 2026)

Production requirements retain a historical `PartId` and name; only nullable `LibraryPartId` has a
catalog FK. Deletion detaches verified independent production references under a row lock, retains
content-addressed files, and never cascades to production history. Unresolved legacy jobs and
eligible unlimited runs remain protected.

### Finished legacy jobs

Cancelled, Failed and Completed jobs with missing snapshots permit source part removal. Deletion
preserves history and marks missing snapshots permanently Unavailable: retry and download return 409.
Valid snapshots remain independent. Unfinished unresolved jobs, assemblies and eligible unlimited
runs still protect their source. Cancelling one unlimited job does not cancel its owning run.
