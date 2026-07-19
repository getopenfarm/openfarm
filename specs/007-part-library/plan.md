# Implementation Plan: Part Library (with Sliced Files & In-App Slicing)

**Branch**: `007-part-library` | **Date**: 2026-07-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-part-library/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add a real Part catalog — an operator attaches a name and exactly one file (STL/3MF/G-code) — replacing
`web/src/features/parts/`'s current mock-data prototype. A part is only ever saved with a resolved nozzle
diameter and material; its estimated print time/weight and per-plate thumbnails, by contrast, may still be
resolving at the moment it's saved (see the two-milestone flow below). **Every add/replace, regardless of
file format, goes through one uniform pipeline**: attaching the file starts background processing
immediately, as a single continuous add-part dialog — the operator keeps entering/editing the part's name
and tags in that same dialog while processing runs. **A sixth round of follow-up clarifications, applied
after the original design review, changes three things from the paragraph below and the rest of this
document**: (1) there is no `filamentId`/Filament-selection field anywhere in the Add/Replace request —
material always resolves from the file itself; (2) thumbnails are derived per plate, not one per file; (3)
Save no longer waits on the estimate — only on nozzle diameter/material.

An already-sliced file (G-code, or a 3MF that already contains sliced G-code) is parsed directly from its
own embedded metadata — scanning every plate and every AMS slot within each for its detected material and
color, then grouping the results into one filament requirement per **distinct** (material, color) pair
rather than one per plate/slot, so e.g. two plates sharing the same material+color collapse into a single
requirement — while a file requiring slicing (STL, or an unsliced 3MF) is handled by a new in-app slicing
capability that automatically invokes a headless slicer engine (OrcaSlicer by default, Bambu Studio
supported, organization-configurable with a per-part override) against a material resolved **from the file
itself, never from the operator**: an unsliced 3MF's own project-level filament assignment (Orca
Slicer/Bambu Studio require picking one to save any project), or, only for an STL — which structurally can
never carry material data — a fixed internal generic default, storing the resulting filament requirement as
fully unconstrained ("any material," now reachable only via this one format) rather than tied to any
specific type, with the tradeoff that its estimate is then a best-effort approximation rather than
guaranteed-accurate for whatever material it's eventually printed in — and whichever nozzle diameter is
currently most common among the farm's registered printers. An unsliced 3MF whose own project material can't
be parsed rejects outright, exactly like an already-sliced file's unparseable metadata — it never falls back
to the STL-only wildcard.

Both paths converge on "ready for review" — but that milestone itself now splits in two: nozzle diameter and
material (and, for an already-sliced file, everything else too, since it all parses atomically) are typically
available almost immediately; for a requires-slicing file, estimated time/weight and per-plate thumbnails may
still be resolving at that point (tracked via a job-level `PreviewEstimateStatus`/part-level `EstimateStatus`:
`Resolving` → `Available`, or, once confirmed, permanently `Unavailable` if that trailing slice fails). The
Save action is enabled as soon as nozzle diameter/material resolve and (for a new part) a name has been
entered — it no longer waits for the estimate. Confirming saves the part from whatever has been derived so
far, together with the name/tags accumulated in the dialog and any narrower filament overrides the operator
attaches per requirement; a background continuation then keeps resolving the estimate/thumbnails afterward,
writing either onto the still-open job's preview (live-updating an unconfirmed review) or directly onto the
already-saved `Part` (pushed via a new `PartEstimateResolved`/`PartEstimateUnavailable` SignalR event) —
whichever currently holds the truth. Discarding instead of confirming still means nothing is saved. Nothing
about the derived numbers themselves is ever hand-editable, at review time or anywhere else in this phase.
This phase finally wires the Printer ↔ Machine Profile link `docs/FEATURE_PLAN.md`'s own Phase 1 dependency
notes earmarked for whichever later phase consumes it — nozzle diameter is read from a printer's linked
Machine Profile, not duplicated onto `Printer`. A thumbnail is derived automatically per plate on a
best-effort basis (normalized to one standard image format) that never blocks saving. There is no
manual-entry fallback anywhere for nozzle diameter or material — an unparseable/unsliceable file or no
printer with a known nozzle diameter reject the operation outright before a resolved result ever exists —
and a part can be tagged and its file replaced (a direct, all-or-nothing replacement, never additive) with
no live printer connection required.

Five genuinely new pieces of backend infrastructure make this possible, none of which has prior art
anywhere in this codebase today (confirmed by direct research, not assumption — see `research.md`):
sliced-file metadata inspection (a new `ZipArchive`-based 3MF/G-code inspector, patterned after but not
extending Phase 2's `ProfileBundleParser`), unsliced-3MF project-material resolution (a sibling `ZipArchive`
reader, `IUnslicedProjectMaterialResolver`, reading an unsliced project's own filament-preset assignment —
what makes `filamentId` removable from the request entirely, `research.md` §13), retained-file storage
(`IPartFileStorage`, local disk — nothing persists uploaded bytes today), headless slicer CLI invocation
(`ISlicerEngine`, a brand-new external process dependency this phase introduces), and one unified async
Processing → Ready for Review → Confirm/Discard pipeline (`PartProcessingJob`, a small persisted tracking
table feeding a bounded `Channel`-backed `PartProcessingBackgroundService`) that covers **every** file type
identically — an already-sliced file simply resolves near-instantly, since reading its own metadata needs no
external process the way slicing does, but goes through the same state machine and API/UI shape rather than
a separate synchronous shortcut. That pipeline now reaches "ready for review" in two effective milestones
rather than one (`research.md` §3b): nozzle diameter/material gate `ReadyForReview` and Confirm; estimated
time/weight and per-plate thumbnails may trail behind for a requires-slicing file, resolved by a background
continuation that targets whichever of the job's own preview or the already-confirmed `Part` currently holds
the truth. Everything else — REPR/FastEndpoints endpoints, multi-tenancy, the activity-log post-processor,
SignalR push on `/hubs/farm`, Orval/multipart codegen — follows the exact patterns Phases 0–3 already
established.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`api/`), TypeScript 5 / React 19 (`web/`) — unchanged from Phase 0–3.

**Primary Dependencies**: No new NuGet packages for zip/3MF/G-code parsing — `System.IO.Compression.ZipArchive`
+ `System.Text.Json.JsonDocument`, the exact technique `ProfileBundleParser` already established
(`research.md` §1). `System.Threading.Channels.Channel<T>` (BCL, no new package) backs the new async
processing queue (`research.md` §3). One genuinely new *external* (non-NuGet) dependency: a headless
OrcaSlicer (default) and/or Bambu Studio CLI binary, installed into the API container image and invoked via
`System.Diagnostics.Process` (`research.md` §4) — no prior art in `third_party/` to build on. No new
frontend dependencies (`@tanstack/react-virtual` stays unused — `research.md` §11 explains why this phase
doesn't need it).

**Storage**: Existing Postgres via `OpenFarmDbContext`. Three migrations in this feature's PR series:
`AddPartsCatalog` (`Tags`, `Parts`, `PartFilamentRequirements`, `PartTags`, `PartProcessingJobs`,
`PartThumbnails`, `PartProcessingJobPreviewThumbnails` — the `PartProcessingJobs` row includes a
`PreviewFilamentRequirements` `jsonb` column, not a separate table, `research.md` §3a; there is no
`FilamentId` column anywhere in this migration — material resolution never persists a client-supplied value,
`research.md` §13), `AddPrinterMachineProfileLink` (one nullable FK column on the existing `Printers`
table), and `AddOrganizationSlicingDefaults` (one enum column on the existing `Organizations` table). A
part's actual file content (STL/3MF/G-code, potentially large) is **not** stored in Postgres — a new
`IPartFileStorage` abstraction persists it to a new bind-mounted volume (`openfarm_part_files:/data/part-
files`, added to `infra/docker-compose.yml`), namespaced by `organizationId`/`partId` (`research.md` §8). A
part's thumbnails, by contrast, are small and bounded, so they stay a `bytea` column on a new small
per-plate child table (`PartThumbnails`/`PartProcessingJobPreviewThumbnails`, `research.md` §8 — reopened
from a single column to one row per plate) — always normalized to PNG at ingestion, so no content-type
column is needed anywhere. `Part.EstimatedPrintTimeMinutes`/`EstimatedFilamentWeightGrams` are nullable,
gated by a new `Part.EstimateStatus` enum (`Resolving | Available | Unavailable`) — the one place a
`Parts` row can legitimately be incomplete, for a requires-slicing part confirmed before its estimate
finished (`research.md` §3b). `Part` uses Postgres's `xmin` system column as an EF Core
optimistic-concurrency token, the same technique `Spool` already uses.

**Testing**: TDD (xUnit + FluentAssertions, `OpenFarm.Domain.Tests`/`OpenFarm.Application.Tests`) for
`NozzleDiameterResolver` (mode + deterministic tie-break), `FilamentRequirementGrouper` (dedup-by-
distinct-(material,color)-pair across every plate/slot — the exact worked example the user supplied while
reviewing this plan, e.g. a 4-plate input producing 3 grouped requirements, is a direct test case), the
Add/Replace/Confirm Part FluentValidation gates (nozzle-diameter-must-be-resolvable unconditionally — no
longer varying by whether a `filamentId` was supplied, since that field no longer exists —
name-required-on-Add-confirm-only, mutually-exclusive narrow overrides per filament requirement,
accepted-file-format checks), and `IPartFileClassifier`'s STL/G-code/3MF requires-slicing dispatch rule.
Test-after/fixture-replay (matching `BambuMqttMessageParser`/`ProfileBundleParser`'s existing carve-out) for
`IAlreadySlicedFileMetadataParser`'s actual byte-level G-code/3MF metadata parsing (including multi-plate/
multi-slot detected-material/color extraction and per-plate thumbnail extraction, fed into
`FilamentRequirementGrouper`), `IUnslicedProjectMaterialResolver`'s project-settings parsing (`research.md`
§13), and `ISlicerEngine`'s real CLI invocation (developed against a `FakeSlicerEngine` first; real-CLI
validation is manual, never a CI gate).
Test-after for orchestration-only `Application` handlers, `PartFileProcessor`'s parse-vs-slice dispatch,
`PartProcessingBackgroundService`'s queue-consumption loop (including its post-`ReadyForReview`/post-
`Confirmed` continuation targeting logic, `research.md` §3b), EF Core repository/configuration code, and
React components/pages. `WebApplicationFactory` + Testcontainers.PostgreSql for API integration tests
covering the full Processing → Ready for Review → Confirm/Discard cycle for both an already-sliced file
(including a multi-requirement, multi-plate-thumbnail fixture) and a requires-slicing file
(`FakeSlicerEngine`), every FR-006 rejection case (including an unsliced 3MF with unparseable project
material), the printer↔machine-profile-link + nozzle-diameter-auto-detection round trip, and the early-
confirm path specifically — confirming while `PreviewEstimateStatus = Resolving`, then asserting the
trailing continuation correctly targets the confirmed `Part` (not the terminal job row) on both success and
a subsequently-injected failure. Vitest + RTL for the rewritten `web/src/features/parts/`, including its
single continuous Add/Replace dialog, its Save-gating logic (nozzle diameter/material only), and its
"estimating…" indicator swapping to real numbers on a live `PartEstimateResolved` push.

**Target Platform**: Linux containers (Docker Compose), ASP.NET Core host, Vite-built SPA — unchanged. The
API container image gains a new build step installing the OrcaSlicer (and optionally Bambu Studio) headless
CLI.

**Project Type**: Web application — existing `api/` + `web/` monorepo, extended in place.

**Performance Goals**: SC-001/SC-002 (add a part in under 2 minutes / 1 minute) — the initial `POST`/`PUT`
returns immediately regardless of file type; an already-sliced file's processing typically completes in
well under a second (no external process), so its wall-clock cost against these targets is dominated by the
operator's own review/confirm interaction, not backend latency. SC-006 (≥90% of files requiring slicing
succeed on first attempt) is a correctness property of the slicer integration, not a load target.
`MaxConcurrentSlicingOperations` (default 2) bounds how many CPU/IO-heavy slicer CLI processes run at once —
this cap is effectively never touched by the parsing branch, since it's cheap enough not to need it.

**Constraints**: FR-009/FR-009a/FR-009b — every add/replace request MUST NOT block on the processing
result, a successful result MUST reach a reviewable state rather than auto-save, the operator MUST be able
to keep editing name/tags concurrently with processing, and no path ever allows editing the derived numbers
themselves. FR-009c/FR-009d/FR-009e — Confirm MUST be available as soon as nozzle diameter/material resolve,
independent of the estimate/thumbnails; a deferred failure after an early confirm MUST leave the `Part`
saved with its estimate marked `Unavailable`, never rolled back or flagged as needing reprocessing, and MUST
be pushed to the client over the same real-time channel as every other update — no view falls back to manual
refresh. FR-005/FR-010 — automatic in-app slicing MUST NOT persist a sliced-output file, and the same
`ISlicerEngine` capability MUST be reusable, unmodified, by Phase 7's future on-demand job-dispatch-time
slicing. No manual-entry fallback anywhere for nozzle diameter or material (FR-006/FR-002) — every rejection
or discard path before confirmation must leave zero partial state; `EstimateStatus` is the sole, narrow,
post-confirmation exception (FR-009d).

**Scale/Scope**: Single self-hosted organization scope, unchanged from prior phases. SC-005 (filter 500+
parts by tag in under 10 seconds) is comfortably served by the existing `DataTable` + pagination pattern
already used by every other catalog feature — no virtualization concern this phase (`research.md` §11).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture**: `Domain/Parts/` holds `Part`, `PartFilamentRequirement`, `PartThumbnail`,
  `EstimateStatus`, `Tag`, `PartProcessingJob`, `PartFileFormat`, `SlicerEngine` — framework-agnostic, no
  `FastEndpoints`/EF references. `Application/Parts/` defines `IPartFileClassifier`,
  `IAlreadySlicedFileMetadataParser`, `IUnslicedProjectMaterialResolver` (new, §13), `ISlicerEngine`, the
  unifying `IPartFileProcessor`, `IPartFileStorage`, `IPartEventPublisher`, `NozzleDiameterResolver`, and the
  command/query handlers (including
  `ConfirmPartProcessingJobCommandHandler`/`DiscardPartProcessingJobCommandHandler`) — `Infrastructure`
  implements every one of those seams (`Infrastructure/Parts/`), including the new
  `PartProcessingBackgroundService`, following `PrinterConnectionBackgroundService`'s existing location
  convention. `Api` endpoints stay thin FastEndpoints adapters plus
  `FarmHubPartEventPublisher : IPartEventPublisher`, mirroring `FarmHubPrinterEventPublisher` exactly.
  `ISlicerEngine`'s reusability by Phase 7 (FR-010) is exactly what this boundary is for — `IPartFileProcessor`
  is the phase-local unifying facade over it, `IAlreadySlicedFileMetadataParser`, and
  `IUnslicedProjectMaterialResolver`, not a replacement for any of them. The §3b trailing continuation stays
  inside `PartProcessingBackgroundService` (`Infrastructure`) and writes to `Part` through the same
  `Application`-defined repository contracts everything else uses — it does not reach into `Domain`/
  `Application` any differently than the rest of the pipeline already does. **Pass.**
- **II. Test Discipline**: TDD for `NozzleDiameterResolver`, `FilamentRequirementGrouper`, the Add/Replace/
  Confirm FluentValidation gates, and `IPartFileClassifier`'s dispatch rule — all deterministic, well-specified
  logic, the same bar this
  codebase already holds `FeedUnitKindCatalog.Resolve`/`PrinterModelCatalog.Resolve` to. Test-after/
  fixture-replay for the actual G-code/3MF byte-level metadata parsing (including multi-slot and per-plate-
  thumbnail extraction), `IUnslicedProjectMaterialResolver`'s project-settings parsing, and the real slicer
  CLI invocation. Test-after for orchestration handlers, `PartFileProcessor`'s dispatch logic, the background
  service's queue loop (including its §3b continuation-targeting logic), EF Core plumbing, and React
  components. **Pass.**
- **III. Consistent UX**: `PartProcessingJobStatusChanged`/`PartUpserted`/`PartEstimateResolved`/
  `PartEstimateUnavailable`/`PartDeleted` push through the existing `/hubs/farm` hub — no view falls back to
  polling, including the trailing estimate/thumbnail update after an early confirm (§3b), which is exactly
  the kind of "no manual refresh" case this principle exists for. Frontend nav/permission gating checks the
  `permissions` catalog, never role names. Forms use react-hook-form + zod (the file object itself follows
  the existing `ImportProfilesDialog` precedent of plain `useState`, `research.md` §11); primitives come
  from `src/components/ui/`. The single continuous Add/Replace Part dialog (`research.md` §11) is itself an
  application of this principle: one interaction pattern for every file type and every stage
  (attach/edit/review/save), not several inconsistent ones — extended, not broken, by Save now enabling
  earlier than the full result (§3b). All API errors surface as `ProblemDetails`. **Pass.**
- **IV. Build for Expansion, Not Speculation**: `ISlicerEngine` is built once, reused unmodified by Phase 7
  (FR-010). `Printer.MachineProfileId` finally fills the deferred link `docs/FEATURE_PLAN.md`'s Phase 1
  notes earmarked. `PartFilamentRequirement` becoming a collection, the parse/slice unification behind
  `IPartFileProcessor`, `filamentId`'s removal in favor of `IUnslicedProjectMaterialResolver`, per-plate
  thumbnails, and the two-milestone `ReadyForReview`/`EstimateStatus` split are all this principle applied to
  real, stated needs (a genuine AMS multi-color mapping; two paths that had already converged in behavior; a
  direct operator objection to being asked for a material a 3MF already declares; a direct operator ask to
  see every plate; a direct operator ask for Save to not block on the slow part) rather than speculative
  abstraction. Conversely, this phase deliberately does **not** build several things it has no current,
  stated need for: no `SlicingDefaults` table for a single enum column, no TanStack Virtual for a catalog
  SC-005 bounds at 500+ rows, no Hangfire-style durable job runner, no thumbnail content-type tracking now
  that every source is normalized to one format at ingestion, no retry/reprocessing mechanism for a failed
  deferred estimate (FR-009d's `Unavailable` is genuinely terminal, not a queued retry this phase has any
  stated need for), and — despite FR-002a introducing a fixed fallback material — still no
  organization-configurable default material setting of any kind; the fallback is a hardcoded system
  constant with exactly one purpose (letting an STL's "any material" wildcard run at all), not a Settings
  field this principle would otherwise warn against speculatively adding. **Pass.**
- **V. Performance and Real-Time Responsiveness**: every add/replace runs as a `BackgroundService`-consumed
  async pipeline specifically so it never blocks a request thread (FR-009) — this holds uniformly now, not
  just for the slicing branch, and now extends past `Confirm` itself for a requires-slicing part's deferred
  estimate (§3b), which likewise never blocks the Confirm request thread. `MaxConcurrentSlicingOperations`
  bounds CPU/IO-heavy external-process concurrency. No new unbounded-list virtualization concern.
  `PartProcessingJobStatusChanged`/`PartUpserted`/`PartEstimateResolved`/`PartEstimateUnavailable` replace
  what would otherwise be manual-refresh-driven UX — including the specific case (an estimate resolving
  after the part is already saved and visible) that a naive implementation would most tempt into a polling
  fallback. **Pass.**
- **Security & Multi-Tenancy Isolation**: `Parts`, `PartFilamentRequirements`, `PartThumbnails`, `Tags`,
  `PartTags`, `PartProcessingJobs`, `PartProcessingJobPreviewThumbnails` all use `IsMultiTenant()`
  exclusively. Stored file paths are namespaced by `organizationId` first. Every new endpoint carries a real
  `[Permission("...")]` attribute — the Confirm/Discard job-resolution endpoints reuse the same
  `parts:create`/`parts:replace-file` permission their originating Add/Replace request needed. Activity-log
  coverage: the Confirm endpoint (the actual part-mutating moment, for **every** file type now) is a normal
  `IActivityLoggable`-on-Request-DTO-covered HTTP endpoint, uniformly. The direct-`IActivityLogWriter` writes
  from `PartProcessingBackgroundService` are for (a) a pre-confirm processing **failure** and (b) the §3b
  continuation's post-confirm outcome (estimate resolved or permanently unavailable) — both attributed to the
  job's `RequestedByUserId` rather than `ActorType.System`, since both happen with no HTTP request in flight.
  Auth is the existing real JWT flow throughout. **Pass.**

No Constitution Check violations. `research.md` records every non-trivial design call this feature makes —
most notably the unification of the parse/slice paths behind `IPartFileProcessor` (§2), the redesigned
`PartProcessingJob` pipeline and its narrowed background activity-logging need (§3), the §3b trailing
continuation that can target either a job's preview or an already-confirmed `Part`, `PartFilamentRequirement`
becoming a collection (§7), the enforced-vs-unenforced FK choice made twice in opposite directions for good,
stated reasons (§6 vs. §7), removing `filamentId` in favor of file-driven material resolution (§13), and
per-plate thumbnails (§8) — for visibility in Complexity Tracking below.

**Post-Phase-1-design re-check** (per `research.md`/`data-model.md`/`contracts/`, including two follow-up
revisions the user raised directly while reviewing this plan, then a further sixth round raised directly
during a later spec-alignment review — see `checklists/requirements.md`'s notes on those rounds): confirms
all of the above with no new violations. `PartProcessingBackgroundService` (in `Infrastructure.Parts`) taking
a direct dependency on `Application.Parts.IPartFileProcessor`/`IPartFileStorage`/`IPartEventPublisher`
mirrors the exact `Infrastructure → Application` dependency direction `PrinterConnectionBackgroundService`
already establishes — the sixth round's §3b trailing continuation reuses this exact same dependency shape to
write to `Part` post-confirm, rather than introducing a new access path.

## Project Structure

### Documentation (this feature)

```text
specs/007-part-library/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
│   └── part-library-api.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
api/src/OpenFarm.Domain/Parts/
  Part.cs                              # entity — see data-model.md for full field list. EstimateStatus
                                          # gates EstimatedPrintTimeMinutes/EstimatedFilamentWeightGrams
                                          # (both nullable now); PlateCount + PartThumbnail collection
                                          # replace the old single ThumbnailBytes column (research.md §3b/§8)
  PartFilamentRequirement.cs           # entity — child of Part; MaterialType (string? — null means "any
                                          # material," now STL-only, research.md §7a)/DetectedColorHex/
                                          # DetectedSubBrand/narrow overrides. One row per distinct
                                          # (MaterialType, DetectedColorHex) pair across the whole file — no
                                          # SlotIndex (research.md §7)
  PartThumbnail.cs                     # entity — child of Part; {PlateIndex, Bytes}, one row per plate
                                          # that successfully produced a thumbnail (research.md §8)
  EstimateStatus.cs                    # enum: Resolving, Available, Unavailable (research.md §3b)
  PartFileFormat.cs                    # enum: Stl, ThreeMf, GCode
  SlicerEngine.cs                      # enum: OrcaSlicer, BambuStudio
  Tag.cs                               # entity
  PartProcessingJob.cs                 # entity — Kind/Status enums + preview fields (incl. a
                                          # PreviewFilamentRequirements value list, jsonb-mapped, not a
                                          # child entity — research.md §3a; PreviewEstimateStatus/
                                          # PreviewPlateCount mirror Part's — research.md §3b/§8), uniform
                                          # for every file type. No FilamentId field — removed entirely
                                          # (research.md §13)
  PartProcessingJobKind.cs / PartProcessingJobStatus.cs   # Status gains a ReadyForReview -> Failed
                                                             # transition (research.md §3)
  PreviewFilamentRequirement.cs         # small record/value type — {RequirementIndex, MaterialType,
                                          # DetectedColorHex?, DetectedSubBrand?} — not an EF-tracked
                                          # entity, just the jsonb-serialized shape (research.md §3a/§7)
  PartProcessingJobPreviewThumbnail.cs  # entity — child of PartProcessingJob; {PlateIndex, Bytes},
                                          # mirrors PartThumbnail; copied into it on Confirm (research.md §8)
  PartNotFoundException.cs / TagNotFoundException.cs / DuplicatePartTagException.cs
  UnsupportedPartFileFormatException.cs / PartFileParsingFailedException.cs
  SlicingFailedException.cs / NoNozzleDiameterAvailableException.cs
  UnslicedProjectMaterialParsingFailedException.cs   # new — unsliced 3MF material unparseable (research.md §13)
  PartProcessingJobNotReadyForReviewException.cs / PartConcurrencyConflictException.cs

api/src/OpenFarm.Domain/Printers/
  Printer.cs                           # + MachineProfileId (Guid?) property, LinkMachineProfile/Unlink methods

api/src/OpenFarm.Domain/Organizations/ (or wherever Organization already lives)
  Organization.cs                      # + DefaultSlicerEngine property, SetDefaultSlicerEngine method

api/src/OpenFarm.Application/Parts/
  IPartRepository.cs / ITagRepository.cs / IPartProcessingJobRepository.cs
  IPartFileClassifier.cs                # Classify(stream, fileName) -> (PartFileFormat, requiresSlicing) — sync
  IAlreadySlicedFileMetadataParser.cs   # ParseAsync(stream, format) -> SlicedFileMetadata (1+ requirements,
                                          # per-plate thumbnails) | failure
  IUnslicedProjectMaterialResolver.cs   # new — ResolveAsync(stream) -> UnslicedProjectMaterial
                                          # {MaterialType, PlateCount} | failure — an unsliced 3MF's own
                                          # project settings, never the operator (research.md §13)
  ISlicerEngine.cs                      # SliceAsync(engine, modelStream, materialType, nozzleDiameterMm, ct)
                                          # -> SlicingResult — reusable by Phase 7 unmodified (FR-010)
  IPartFileProcessor.cs                 # ProcessAsync(...) -> PartProcessingResult — the single seam
                                          # PartProcessingBackgroundService depends on; delegates internally
                                          # to ISlicerEngine, IAlreadySlicedFileMetadataParser, or
                                          # IUnslicedProjectMaterialResolver (the last only for the
                                          # nozzle/material milestone of a requires-slicing 3MF, research.md §3b)
  IPartFileStorage.cs                   # SaveAsync/OpenReadAsync/DeleteAsync, keyed by org/part id
  IPartEventPublisher.cs                # Application-defined contract; Api implements. Gains
                                          # PublishPartEstimateResolvedAsync/PublishPartEstimateUnavailableAsync
                                          # (research.md §3b)
  NozzleDiameterResolver.cs             # static Resolve(printers, profiles) -> decimal? — TDD (research.md §5)
  FilamentRequirementGrouper.cs         # static Group(detectedRequirements) -> IReadOnlyList<PreviewFilamentRequirement>
                                          # — dedups by distinct (MaterialType, DetectedColorHex) pair across
                                          # every plate/slot IAlreadySlicedFileMetadataParser found — TDD (research.md §7/§12)
  AddPartCommand.cs / Handler / Validator          # file only, no filamentId — always creates a job, never a
                                                      # Part (research.md §13)
  ReplacePartFileCommand.cs / Handler / Validator  # same shape, existing Part's name/tags untouched
  ConfirmPartProcessingJobCommand.cs / Handler / Validator   # name/tags (Add only) + narrow overrides -> Part,
                                                                # copying whatever EstimateStatus/thumbnails
                                                                # the job currently holds (research.md §3b)
  DiscardPartProcessingJobCommand.cs / Handler     # valid from Processing or ReadyForReview
  DeletePartCommand.cs / Handler
  TagPartCommand.cs / Handler / UntagPartCommand.cs / Handler
  GetPartsQuery.cs / Handler                        # tag filter, pagination
  GetPartQuery.cs / Handler                          # includes per-requirement unresolved-narrow-override
                                                        # flags and the PartThumbnail/EstimateStatus shape
  GetPartProcessingJobQuery.cs / Handler
  LinkPrinterMachineProfileCommand.cs / Handler / Validator
  GetSlicingDefaultsQuery.cs / Handler / UpdateSlicingDefaultsCommand.cs / Handler
  PartProcessingWorkItem.cs             # queue payload shape consumed by the background service

api/src/OpenFarm.Infrastructure/Parts/
  PartRepository.cs / TagRepository.cs / PartProcessingJobRepository.cs
  PartFileClassifier.cs                 # ZipArchive-based, mirrors ProfileBundleParser's technique (research.md §1)
  AlreadySlicedFileMetadataParser.cs    # G-code comment + Bambu slice_info.config parsing; walks EVERY plate
                                          # in a multi-plate 3MF (not just the first), reading each plate's own
                                          # per-slot material+color and its own embedded preview image, then
                                          # calls FilamentRequirementGrouper
  UnslicedProjectMaterialResolver.cs    # new — ZipArchive read of Metadata/project_settings.config,
                                          # implements IUnslicedProjectMaterialResolver (research.md §13)
  CliSlicerEngine.cs                    # System.Diagnostics.Process wrapper around OrcaSlicer/Bambu Studio CLI;
                                          # exports a thumbnail per plate, not just one (research.md §8)
  FakeSlicerEngine.cs                   # test/dev double — deterministic canned SlicingResult
  PartFileProcessor.cs                  # implements IPartFileProcessor — resolves nozzle diameter/material
                                          # first (IUnslicedProjectMaterialResolver or the fixed STL fallback,
                                          # or IAlreadySlicedFileMetadataParser's full atomic parse), then, for
                                          # a requires-slicing file only, continues on to ISlicerEngine for the
                                          # estimate/thumbnails — the two-milestone split PartProcessingBackground
                                          # Service depends on (research.md §3b)
  LocalDiskPartFileStorage.cs           # implements IPartFileStorage
  PartProcessingBackgroundService.cs    # Channel<PartProcessingWorkItem> consumer, IServiceScopeFactory +
                                          # CreateTenantScope() pattern from PrinterConnectionBackgroundService;
                                          # on a pre-confirm failure, writes IActivityLogWriter directly
                                          # (research.md §3); on the estimate/thumbnail continuation completing
                                          # (or failing) after the job is already Confirmed, writes directly to
                                          # Part instead of the (now-frozen) job row and calls
                                          # IPartEventPublisher's PartEstimateResolved/Unavailable (research.md §3b)
  SlicerOptions.cs                      # CLI paths, timeout, MaxConcurrentSlicingOperations, and
                                          # GenericFallbackMaterialProfile per SlicerEngine (e.g. OrcaSlicer:
                                          # "Generic PLA") — used only for an STL's "any material" wildcard,
                                          # the only remaining case that needs it (research.md §7a); bound from
                                          # config, never operator-configurable
  Persistence/Configurations/PartConfiguration.cs / PartFilamentRequirementConfiguration.cs /
    PartThumbnailConfiguration.cs / TagConfiguration.cs / PartTagConfiguration.cs /
    PartProcessingJobConfiguration.cs / PartProcessingJobPreviewThumbnailConfiguration.cs
    # PartConfiguration: FileFormat/SlicerEngineOverride/EstimateStatus HasConversion<string>(),
    #   UseXminAsConcurrencyToken()
    # PartFilamentRequirementConfiguration: partial unique index (PartId, MaterialType, DetectedColorHex)
    #   WHERE DetectedColorHex IS NOT NULL, bare Guid? columns for NarrowFilamentVariantId/
    #   NarrowFilamentProfileId (no HasOne)
    # PartThumbnailConfiguration / PartProcessingJobPreviewThumbnailConfiguration: composite PK
    #   (PartId|JobId, PlateIndex), ON DELETE CASCADE from their respective parent
    # PartProcessingJobConfiguration: PreviewFilamentRequirements as jsonb via HasConversion(serialize,
    #   deserialize) + a custom ValueComparer, mirroring PrinterConfiguration's AmsUnits mapping — no
    #   separate child-table configuration needed for that field specifically (research.md §3a); no
    #   FilamentId column anywhere (research.md §13)
  Persistence/Migrations/<timestamp>_AddPartsCatalog.cs
  Persistence/Migrations/<timestamp>_AddPrinterMachineProfileLink.cs
    # PrinterConfiguration: + HasOne<MachineProfile>().WithMany().HasForeignKey(...).OnDelete(SetNull)
  Persistence/Migrations/<timestamp>_AddOrganizationSlicingDefaults.cs

api/src/OpenFarm.Api/
  Endpoints/Parts/CreatePartEndpoint.cs / ReplacePartFileEndpoint.cs / DeletePartEndpoint.cs /
    ListPartsEndpoint.cs / GetPartEndpoint.cs / GetPartFileEndpoint.cs /
    TagPartEndpoint.cs / UntagPartEndpoint.cs
    # no GetPartThumbnailEndpoint — thumbnailBase64 travels inline on List/Get responses (research.md §8);
    # a bare byte-stream endpoint wouldn't be renderable via <img src> under this app's Bearer-only auth
  Endpoints/Tags/ListTagsEndpoint.cs
  Endpoints/PartProcessingJobs/GetPartProcessingJobEndpoint.cs /
    ConfirmPartProcessingJobEndpoint.cs / DiscardPartProcessingJobEndpoint.cs
    # no GetPartProcessingJobThumbnailEndpoint — same reasoning; preview.thumbnailBase64 travels inline on
    # both the GET response and the PartProcessingJobStatusChanged SignalR push
  Endpoints/Printers/LinkPrinterMachineProfileEndpoint.cs   # new file alongside existing printer endpoints
  Endpoints/Settings/GetSlicingDefaultsEndpoint.cs / UpdateSlicingDefaultsEndpoint.cs
  Hubs/FarmHubPartEventPublisher.cs      # implements IPartEventPublisher
  ExceptionHandling/DomainExceptionHandler.cs   # extended with this feature's exception -> status mappings

web/src/features/parts/                 # replaces the mock-data prototype
  PartsPage.tsx / PartDetailPage.tsx     # catalog list (DataTable + pagination, tag filter bar) + detail view
  components/AddOrReplacePartDialog.tsx  # the single continuous dialog (research.md §11): attaches file
                                           # (no material field — research.md §13), fires POST/PUT
                                           # immediately, keeps name/tags editable via RHF+zod while a
                                           # PartProcessingJob runs, shows the review panel inline once
                                           # ReadyForReview, Save disabled until nozzle diameter/material
                                           # resolve + named — no longer waits on estimateStatus
                                           # (research.md §3b) — Cancel discards
  components/PartProcessingReviewSection.tsx   # the in-dialog preview + per-requirement narrow-override
                                                 # picker; renders an "estimating…" indicator in place of
                                                 # time/weight/thumbnails while estimateStatus = Resolving
  components/PartsTable.tsx / PartFilterBar.tsx  # PartsTable shows the same "estimating…"/"unavailable"
                                                    # indicator per estimateStatus (research.md §3b)
  components/PartThumbnailGallery.tsx    # new (renamed from the single-thumbnail PartThumbnail) — renders
                                           # {thumbnails: [{plateIndex, thumbnailBase64}]} against
                                           # {plateCount} as a per-plate strip/grid, each a data: URI,
                                           # graceful per-plate no-thumbnail fallback (FR-014a); no fetch of
                                           # its own (research.md §8)
  components/PartFilamentRequirementsList.tsx   # new — renders 1+ grouped requirements (material + detected
                                                  # color swatch), each independently narrowable
  components/PartTagsEditor.tsx
  hooks/usePartsHub.ts                   # subscribes to PartProcessingJobStatusChanged/PartUpserted/
                                           # PartEstimateResolved/PartEstimateUnavailable/PartDeleted —
                                           # the last two drive the live "estimating… -> resolved" transition
                                           # for an already-saved, early-confirmed part (research.md §3b)
  data/ (Orval-generated hooks; mockParts.ts / mockPartsStore.ts deleted)

web/src/lib/signalr/farmHubConnection.ts # new — extracted shared HubConnectionBuilder setup (research.md §11),
                                           # used by both this feature's usePartsHub and the existing
                                           # features/printers/hooks/useFarmHub.ts (updated to consume it)

web/src/features/printers/
  # existing feature, extended: printer-detail page gains a small "Link Machine Profile" control
  # wired to PUT /printers/{id}/machine-profile

web/src/features/settings/
  # existing feature, extended: a Slicing Defaults section (default engine select)
```

**Structure Decision**: Existing four-project Clean Architecture backend + single-SPA frontend, extended in
place — no new top-level project. `web/src/features/parts/` replaces its existing mock-data prototype,
following `features/printers`/`features/filament` as the real-backend template. The shared SignalR
connection setup is extracted out of `features/printers/hooks/useFarmHub.ts` into
`web/src/lib/signalr/farmHubConnection.ts` since this phase adds a second real consumer of the same hub
connection.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. The design decisions below are recorded for visibility, not because any
is a deviation:

| Decision | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Every add/replace goes through one unified Processing → Ready for Review → Confirm/Discard pipeline, including already-sliced files, rather than a two-shape design (synchronous save for already-sliced, async review for requires-slicing) | The two shapes had already converged on nearly everything — same derived-data shape, same rejection rules, same per-requirement narrow-override mechanics for a multi-color file — leaving only one real difference (parse vs. slice) that belongs behind one interface, not exposed as two API/UI contracts | The two-shape design (an earlier revision of this plan) was superseded once it became clear maintaining it meant two response contracts, two frontend flows, and two sets of tests for a difference that's purely mechanical (external process or not) and imperceptible to the operator in practice (an already-sliced file still resolves near-instantly) |
| `name`/`tags` moved out of the initial add/replace request entirely, supplied only at Confirm, alongside a single continuous dialog that stays open across attach → process → review → save | The operator experience the user described is one continuous action, not two round trips to sequence — attaching a file should start work immediately without first demanding a finished name, and the operator should be able to keep typing while that runs | Requiring `name` upfront (an earlier revision of this plan) was reconsidered once it became clear that gates the *start* of background work behind a field that has nothing to do with processing the file itself — moving it to Confirm removes an artificial ordering constraint with no correctness reason behind it |
| A background-completed **failure** still gets a direct, operator-attributed (`ActorType.User`) activity-log write from `PartProcessingBackgroundService`, while a **success** no longer needs one (Confirm is a normal synchronous endpoint the existing post-processor already covers, for every file type now) | FR-017 requires a processing failure to be logged, but that outcome is only known inside the background service, with no HTTP request in flight at that moment | Logging failures only via the job's own row (no activity-log entry at all) was considered and rejected — FR-017 explicitly wants failures visible in the shared activity log |
| `PartFilamentRequirement` is a child collection, not scalar fields on `Part` | A real already-sliced multi-color/AMS file genuinely declares more than one material/color requirement | A JSON column holding the same array was considered and rejected in favor of a real child table — the narrow-override fields need to be queryable/joinable against `FilamentVariant`/`FilamentProfile` for read-time resolution, which a JSON blob would make materially harder for no clear benefit given the row count per part is always small |
| Filament requirements are identified by a distinct `(MaterialType, DetectedColorHex)` pair, grouped across every plate/slot in the file, rather than one row per detected `SlotIndex` | The user's own worked example (a 4-plate file, 2 plates sharing one material+color) requires collapsing repeated pairs into one requirement, not one row per plate/slot — a per-`SlotIndex` design (an earlier revision of this plan) would have produced 4 duplicate-looking rows instead of 3 distinct ones | Keeping `SlotIndex` as the identity and just adding a `DetectedColorHex` display field alongside it was considered and rejected — it would still show 4 rows for the 4-plate example, not the 3 the user's own framing explicitly calls for |
| Multi-plate 3MF scanning for filament-requirement detection is reopened (every plate is scanned, not just the first), while the file is still never split into multiple Parts and the thumbnail still comes from the first plate only | The user directly confirmed this scope via a concrete example (plate-level materials must be detected and shown); nothing about splitting the file or picking a different thumbnail plate was asked for or implied | Reopening the *entire* multi-plate limitation (also attempting per-plate thumbnails or treating each plate as its own Part) was considered and rejected as a scope expansion well beyond what was actually requested — the reopening is deliberately narrow, scoped to filament-requirement detection only |
| `Printer.MachineProfileId` is an **enforced** nullable FK (`OnDelete(SetNull)`), while `PartFilamentRequirement.NarrowFilamentVariantId`/`NarrowFilamentProfileId` are **unenforced** bare `Guid?` columns | Each field's correctness requirement is genuinely different: FR-005a's farm-wide nozzle-diameter computation silently produces a wrong answer if a dangling link is tolerated, while spec.md explicitly wants a deleted narrow-override reference to degrade gracefully per-requirement | Applying one uniform rule was considered and rejected — spec.md itself asks for different failure behavior in each case |
| Thumbnail stored inline (`bytea`), always normalized to PNG with no content-type column, while the part's actual file goes through a new `IPartFileStorage` disk abstraction | A thumbnail is small, bounded, atomic with its owning row, and — once every source format is normalized at ingestion — always the same image type; the file itself can be large and needs independent re-download/streaming | Storing a per-row content-type alongside the bytes was considered and dropped once it became clear every real source can simply be re-encoded to one format at the single point of ingestion |
| `PartProcessingJob.PreviewFilamentRequirements` is a `jsonb` column, not a separate child table | A job's preview requirement(s) are small (1–4 entries), read exactly once (by `Confirm`), and never independently queried/joined — the same shape `Printer.AmsUnits` already stores as `jsonb` | A dedicated `PartProcessingJobPreviewFilamentRequirement` child table (an earlier revision of this plan) was reconsidered directly by the user and dropped — it added a fourth migration-relevant entity, its own EF configuration, and a cascade-delete relationship for data whose entire lifetime is "written once, superseded once," with no query/join need `Part.PartFilamentRequirement` (the real, final table, §7) actually has |
| Thumbnails travel as inline `thumbnailBase64` in JSON/SignalR payloads, with no separate `GET .../thumbnail` byte-stream endpoint | This app authenticates via JWT Bearer tokens only, so a plain `<img src="...">` cannot attach the required `Authorization` header against an authenticated byte-stream endpoint — `features/printers/components/CameraView.tsx` already establishes inline base64 as this codebase's actual working pattern for an authenticated image | A dedicated `GET /parts/{id}/thumbnail` (and job-preview equivalent) — an earlier revision of this contract — was caught and dropped directly by the user reviewing the plan: as designed it would not have been renderable via a bare `<img>` tag at all, only fetchable via an authenticated client call the contract never specified, so it was a latent design gap, not a valid simpler alternative |
| A requires-slicing file with no Filament specified defaults to "any material" (`MaterialType: null`), sliced against a fixed internal fallback profile, instead of being rejected | The user directly asked for STL to support a genuine no-material-commitment default, deferring the real material choice to whenever the part is actually matched to a machine/job — reopening FR-002's "always required" rule and the separate "no fallback material, ever" decision, specifically for this one wildcard case | Keeping Filament strictly required (the original, multiply-ratified design) was the simpler alternative, and was directly superseded by the user's own request; an organization-configurable default material was also considered (and re-rejected) as a way to reduce friction without a true wildcard — it would still force every organization to pick *some* material up front, which isn't what was asked for |
| An already-sliced file's requirement can never be "any material," only a requires-slicing file's can | A sliced G-code's process parameters (temps, retraction, flow) are physically fixed to one material — swapping materials against them isn't a compatibility nuance, it's a likely-bad print; only a file that still requires slicing gets a genuinely fresh slice whenever its real material becomes known | Allowing the operator to relax an already-sliced file's parsed material to "any" was considered and rejected outright — it would let the catalog claim a physical capability the file doesn't actually have, undermining the "derived data reflects reality" principle this whole spec is built on |
| `filamentId` is removed from the Add/Replace request entirely (sixth follow-up round), with a new `IUnslicedProjectMaterialResolver` reading an unsliced 3MF's own project settings instead | The operator directly objected to being asked to choose a material before the system had even looked at the file — and pointed out Orca/Bambu project files already carry a real filament assignment pre-slice, making an operator prompt for that specific format redundant | Keeping `filamentId` optional (the fifth follow-up's design, `research.md`'s superseded §7a) was the simpler incremental change, but left an operator-facing field that added no value for a 3MF (which always has its own answer) and only a fallback value for an STL anyway — removing it entirely and sourcing material purely from the file is both simpler for the operator and more accurate for a 3MF |
| Per-plate thumbnails (`PartThumbnail`/`PartProcessingJobPreviewThumbnail`, new child tables) replace the single first-plate-only `ThumbnailBytes` column, for both already-sliced and requires-slicing multi-plate files | The operator directly asked to see every plate, not just the first, when reviewing/browsing a multi-plate part — and explicitly wanted this for a requires-slicing multi-plate 3MF too, not only an already-sliced one | Keeping thumbnails first-plate-only (the original, multiply-ratified design) was simpler and had already survived several earlier reopenings of the multi-plate limitation that stopped short of this — but the operator's direct ask here isn't optional scope, it's a stated requirement; a per-plate child table (mirroring `PartFilamentRequirement`'s own shape) was the natural fit once binary payloads ruled out reusing `PreviewFilamentRequirements`'s `jsonb` approach (`research.md` §8) |
| Confirm no longer waits on the estimate — `ReadyForReview` and Confirm are now gated on nozzle diameter/material alone; estimate/thumbnails may trail via a background continuation targeting the job's preview or the confirmed `Part` (`EstimateStatus`, new) | The operator directly argued that print-time/weight is convenience data an operator tuning their own slicer already knows, not gating data — and that large-file slicing shouldn't block saving when the only genuinely slow part is the estimate/render, not the nozzle-diameter/material resolution (which, once `filamentId` is gone, happens *before* the slicer even runs) | Keeping the original single-milestone `ReadyForReview` (gating Confirm on the full result, `research.md`'s original §3) was simpler — one state transition, no post-confirm background writes to reason about — but directly contradicted the operator's stated priority; splitting the milestone, rather than e.g. polling for a hypothetical fast-path, was the smallest change that let Save reflect exactly what the operator said actually blocks them |
