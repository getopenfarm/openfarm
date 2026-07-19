# Implementation Plan: Part Library (with Sliced Files & In-App Slicing)

**Branch**: `007-part-library` | **Date**: 2026-07-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-part-library/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add a real Part catalog — an operator attaches a name, one file (STL/3MF/G-code), and (when the file needs
slicing) a Filament selection — replacing `web/src/features/parts/`'s current mock-data prototype. A part
is only ever saved fully described: nozzle diameter, one-or-more filament requirements, and an estimated
print time/weight are always successfully derived before a save completes. **Every add/replace, regardless
of file format, goes through one uniform pipeline**: attaching the file (plus a Filament, if the file needs
slicing) starts background processing immediately, as a single continuous add-part dialog — the operator
keeps entering/editing the part's name and tags in that same dialog while processing runs, and the Save
action stays disabled until processing reaches a "ready for review" state (and, for a new part, a name has
been entered). An already-sliced file (G-code, or a 3MF that already contains sliced G-code) is parsed
directly from its own embedded metadata — including a genuine AMS-style multi-color mapping, which parses
into one filament requirement per detected slot — while a file requiring slicing (STL, or an unsliced 3MF)
is handled by a new in-app slicing capability that automatically invokes a headless slicer engine (OrcaSlicer
by default, Bambu Studio supported, organization-configurable with a per-part override) against the
specified Filament and whichever nozzle diameter is currently most common among the farm's registered
printers. Both paths converge on the same "ready for review" state: the operator sees the derived nozzle
diameter, material type(s), estimated time/weight, and thumbnail, may attach a narrower filament override
per requirement now that a real result exists to narrow, and then either confirms/saves (creating the part
or applying the replacement immediately from the already-derived data, together with the name/tags
accumulated in the dialog) or discards it (nothing saved). Nothing about the derived numbers themselves is
ever hand-editable, at review time or anywhere else in this phase. This phase finally wires the Printer ↔
Machine Profile link `docs/FEATURE_PLAN.md`'s own Phase 1 dependency notes earmarked for whichever later
phase consumes it — nozzle diameter is read from a printer's linked Machine Profile, not duplicated onto
`Printer`. A thumbnail is always derived automatically on a best-effort basis (normalized to one standard
image format) that never blocks saving. There is no manual-entry fallback anywhere — an
unparseable/unsliceable file, a missing required Filament, or no printer with a known nozzle diameter all
reject the operation outright before processing even starts, and a part can be tagged and its file replaced
(a direct, all-or-nothing replacement, never additive) with no live printer connection required.

Four genuinely new pieces of backend infrastructure make this possible, none of which has prior art
anywhere in this codebase today (confirmed by direct research, not assumption — see `research.md`):
sliced-file metadata inspection (a new `ZipArchive`-based 3MF/G-code inspector, patterned after but not
extending Phase 2's `ProfileBundleParser`), retained-file storage (`IPartFileStorage`, local disk — nothing
persists uploaded bytes today), headless slicer CLI invocation (`ISlicerEngine`, a brand-new external
process dependency this phase introduces), and one unified async Processing → Ready for Review →
Confirm/Discard pipeline (`PartProcessingJob`, a small persisted tracking table feeding a bounded
`Channel`-backed `PartProcessingBackgroundService`) that covers **every** file type identically — an
already-sliced file simply resolves near-instantly, since reading its own metadata needs no external
process the way slicing does, but goes through the same state machine and API/UI shape rather than a
separate synchronous shortcut. Everything else — REPR/FastEndpoints endpoints, multi-tenancy, the
activity-log post-processor, SignalR push on `/hubs/farm`, Orval/multipart codegen — follows the exact
patterns Phases 0–3 already established.

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
`PartProcessingJobPreviewFilamentRequirements`), `AddPrinterMachineProfileLink` (one nullable FK column on
the existing `Printers` table), and `AddOrganizationSlicingDefaults` (one enum column on the existing
`Organizations` table). A part's actual file content (STL/3MF/G-code, potentially large) is **not** stored
in Postgres — a new `IPartFileStorage` abstraction persists it to a new bind-mounted volume
(`openfarm_part_files:/data/part-files`, added to `infra/docker-compose.yml`), namespaced by
`organizationId`/`partId` (`research.md` §8). A part's thumbnail, by contrast, is small and bounded, so it
stays a plain `bytea` column directly on `Parts` (and, transiently, on `PartProcessingJobs` while a preview
is pending) — always normalized to PNG at ingestion, so no content-type column is needed anywhere. `Part`
uses Postgres's `xmin` system column as an EF Core optimistic-concurrency token, the same technique `Spool`
already uses.

**Testing**: TDD (xUnit + FluentAssertions, `OpenFarm.Domain.Tests`/`OpenFarm.Application.Tests`) for
`NozzleDiameterResolver` (mode + deterministic tie-break), the Add/Replace/Confirm Part FluentValidation
gates (Filament-required-when-slicing, name-required-on-Add-confirm-only, mutually-exclusive narrow
overrides per filament requirement, accepted-file-format checks), and `IPartFileClassifier`'s STL/G-code/3MF
requires-slicing dispatch rule. Test-after/fixture-replay (matching `BambuMqttMessageParser`/
`ProfileBundleParser`'s existing carve-out) for `IAlreadySlicedFileMetadataParser`'s actual byte-level
G-code/3MF metadata parsing (including multi-slot mapping extraction) and `ISlicerEngine`'s real CLI
invocation (developed against a `FakeSlicerEngine` first; real-CLI validation is manual, never a CI gate).
Test-after for orchestration-only `Application` handlers, `PartFileProcessor`'s parse-vs-slice dispatch,
`PartProcessingBackgroundService`'s queue-consumption loop, EF Core repository/configuration code, and React
components/pages. `WebApplicationFactory` + Testcontainers.PostgreSql for API integration tests covering the
full Processing → Ready for Review → Confirm/Discard cycle for both an already-sliced file (including a
multi-requirement fixture) and a requires-slicing file (`FakeSlicerEngine`), every FR-006 rejection case, and
the printer↔machine-profile-link + nozzle-diameter-auto-detection round trip. Vitest + RTL for the rewritten
`web/src/features/parts/`, including its single continuous Add/Replace dialog and its Save-gating logic.

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
to keep editing name/tags concurrently with processing, and neither path ever allows editing the derived
numbers themselves. FR-005/FR-010 — automatic in-app slicing MUST NOT persist a sliced-output file, and the
same `ISlicerEngine` capability MUST be reusable, unmodified, by Phase 7's future on-demand
job-dispatch-time slicing. No manual-entry fallback anywhere (FR-006) — every rejection or discard path must
leave zero partial state.

**Scale/Scope**: Single self-hosted organization scope, unchanged from prior phases. SC-005 (filter 500+
parts by tag in under 10 seconds) is comfortably served by the existing `DataTable` + pagination pattern
already used by every other catalog feature — no virtualization concern this phase (`research.md` §11).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture**: `Domain/Parts/` holds `Part`, `PartFilamentRequirement`, `Tag`,
  `PartProcessingJob`, `PartFileFormat`, `SlicerEngine` — framework-agnostic, no `FastEndpoints`/EF
  references. `Application/Parts/` defines `IPartFileClassifier`, `IAlreadySlicedFileMetadataParser`,
  `ISlicerEngine`, the new unifying `IPartFileProcessor`, `IPartFileStorage`, `IPartEventPublisher`,
  `NozzleDiameterResolver`, and the command/query handlers (including
  `ConfirmPartProcessingJobCommandHandler`/`DiscardPartProcessingJobCommandHandler`) — `Infrastructure`
  implements every one of those seams (`Infrastructure/Parts/`), including the new
  `PartProcessingBackgroundService`, following `PrinterConnectionBackgroundService`'s existing location
  convention. `Api` endpoints stay thin FastEndpoints adapters plus
  `FarmHubPartEventPublisher : IPartEventPublisher`, mirroring `FarmHubPrinterEventPublisher` exactly.
  `ISlicerEngine`'s reusability by Phase 7 (FR-010) is exactly what this boundary is for — `IPartFileProcessor`
  is the phase-local unifying facade over it and `IAlreadySlicedFileMetadataParser`, not a replacement for
  either. **Pass.**
- **II. Test Discipline**: TDD for `NozzleDiameterResolver`, the Add/Replace/Confirm FluentValidation gates,
  and `IPartFileClassifier`'s dispatch rule — all deterministic, well-specified logic, the same bar this
  codebase already holds `FeedUnitKindCatalog.Resolve`/`PrinterModelCatalog.Resolve` to. Test-after/
  fixture-replay for the actual G-code/3MF byte-level metadata parsing (including multi-slot extraction) and
  the real slicer CLI invocation. Test-after for orchestration handlers, `PartFileProcessor`'s dispatch
  logic, the background service's queue loop, EF Core plumbing, and React components. **Pass.**
- **III. Consistent UX**: `PartProcessingJobStatusChanged`/`PartUpserted`/`PartDeleted` push through the
  existing `/hubs/farm` hub — no view falls back to polling. Frontend nav/permission gating checks the
  `permissions` catalog, never role names. Forms use react-hook-form + zod (the file object itself follows
  the existing `ImportProfilesDialog` precedent of plain `useState`, `research.md` §11); primitives come
  from `src/components/ui/`. The single continuous Add/Replace Part dialog (`research.md` §11) is itself an
  application of this principle: one interaction pattern for every file type and every stage
  (attach/edit/review/save), not several inconsistent ones. All API errors surface as `ProblemDetails`.
  **Pass.**
- **IV. Build for Expansion, Not Speculation**: `ISlicerEngine` is built once, reused unmodified by Phase 7
  (FR-010). `Printer.MachineProfileId` finally fills the deferred link `docs/FEATURE_PLAN.md`'s Phase 1
  notes earmarked. `PartFilamentRequirement` becoming a collection, and the parse/slice unification behind
  `IPartFileProcessor`, are both this principle applied to real, stated needs (a genuine AMS multi-color
  mapping; two paths that had already converged in behavior) rather than speculative abstraction.
  Conversely, this phase deliberately does **not** build several things it has no current, stated need for:
  no `SlicingDefaults` table for a single enum column, no TanStack Virtual for a catalog SC-005 bounds at
  500+ rows, no Hangfire-style durable job runner, and no thumbnail content-type tracking now that every
  source is normalized to one format at ingestion. **Pass.**
- **V. Performance and Real-Time Responsiveness**: every add/replace runs as a `BackgroundService`-consumed
  async pipeline specifically so it never blocks a request thread (FR-009) — this holds uniformly now, not
  just for the slicing branch. `MaxConcurrentSlicingOperations` bounds CPU/IO-heavy external-process
  concurrency. No new unbounded-list virtualization concern. `PartProcessingJobStatusChanged`/`PartUpserted`
  replace what would otherwise be manual-refresh-driven UX. **Pass.**
- **Security & Multi-Tenancy Isolation**: `Parts`, `PartFilamentRequirements`, `Tags`, `PartTags`,
  `PartProcessingJobs` all use `IsMultiTenant()` exclusively. Stored file paths are namespaced by
  `organizationId` first. Every new endpoint carries a real `[Permission("...")]` attribute — the Confirm/
  Discard job-resolution endpoints reuse the same `parts:create`/`parts:replace-file` permission their
  originating Add/Replace request needed. Activity-log coverage: the Confirm endpoint (the actual
  part-mutating moment, for **every** file type now) is a normal `IActivityLoggable`-on-Request-DTO-covered
  HTTP endpoint, uniformly. The **one** remaining direct-`IActivityLogWriter` write from
  `PartProcessingBackgroundService` is for a processing **failure**, attributed to the job's
  `RequestedByUserId` rather than `ActorType.System`. Auth is the existing real JWT flow throughout.
  **Pass.**

No Constitution Check violations. `research.md` records every non-trivial design call this feature makes —
most notably the unification of the parse/slice paths behind `IPartFileProcessor` (§2), the redesigned
`PartProcessingJob` pipeline and its narrowed background activity-logging need (§3), `PartFilamentRequirement`
becoming a collection (§7), and the enforced-vs-unenforced FK choice made twice in opposite directions for
good, stated reasons (§6 vs. §7) — for visibility in Complexity Tracking below.

**Post-Phase-1-design re-check** (per `research.md`/`data-model.md`/`contracts/`, including two follow-up
revisions the user raised directly while reviewing this plan — see `checklists/requirements.md`'s notes on
those rounds): confirms all of the above with no new violations. `PartProcessingBackgroundService` (in
`Infrastructure.Parts`) taking a direct dependency on `Application.Parts.IPartFileProcessor`/
`IPartFileStorage`/`IPartEventPublisher` mirrors the exact `Infrastructure → Application` dependency
direction `PrinterConnectionBackgroundService` already establishes.

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
  Part.cs                              # entity — see data-model.md for full field list
  PartFilamentRequirement.cs           # entity — child of Part, SlotIndex?/MaterialType/narrow overrides
  PartFileFormat.cs                    # enum: Stl, ThreeMf, GCode
  SlicerEngine.cs                      # enum: OrcaSlicer, BambuStudio
  Tag.cs                               # entity
  PartProcessingJob.cs                 # entity — Kind/Status enums + preview fields, uniform for every file type
  PartProcessingJobKind.cs / PartProcessingJobStatus.cs
  PartProcessingJobPreviewFilamentRequirement.cs   # entity — child of PartProcessingJob
  PartNotFoundException.cs / TagNotFoundException.cs / DuplicatePartTagException.cs
  UnsupportedPartFileFormatException.cs / PartFileParsingFailedException.cs
  SlicingFailedException.cs / NoNozzleDiameterAvailableException.cs
  PartProcessingJobNotReadyForReviewException.cs / PartConcurrencyConflictException.cs

api/src/OpenFarm.Domain/Printers/
  Printer.cs                           # + MachineProfileId (Guid?) property, LinkMachineProfile/Unlink methods

api/src/OpenFarm.Domain/Organizations/ (or wherever Organization already lives)
  Organization.cs                      # + DefaultSlicerEngine property, SetDefaultSlicerEngine method

api/src/OpenFarm.Application/Parts/
  IPartRepository.cs / ITagRepository.cs / IPartProcessingJobRepository.cs
  IPartFileClassifier.cs                # Classify(stream, fileName) -> (PartFileFormat, requiresSlicing) — sync
  IAlreadySlicedFileMetadataParser.cs   # ParseAsync(stream, format) -> SlicedFileMetadata (1+ requirements) | failure
  ISlicerEngine.cs                      # SliceAsync(engine, modelStream, materialType, nozzleDiameterMm, ct)
                                          # -> SlicingResult — reusable by Phase 7 unmodified (FR-010)
  IPartFileProcessor.cs                 # ProcessAsync(...) -> PartProcessingResult — the single seam
                                          # PartProcessingBackgroundService depends on; delegates internally
                                          # to ISlicerEngine or IAlreadySlicedFileMetadataParser
  IPartFileStorage.cs                   # SaveAsync/OpenReadAsync/DeleteAsync, keyed by org/part id
  IPartEventPublisher.cs                # Application-defined contract; Api implements
  NozzleDiameterResolver.cs             # static Resolve(printers, profiles) -> decimal? — TDD (research.md §5)
  AddPartCommand.cs / Handler / Validator          # file + filamentId? only; always creates a job, never a Part
  ReplacePartFileCommand.cs / Handler / Validator  # same shape, existing Part's name/tags untouched
  ConfirmPartProcessingJobCommand.cs / Handler / Validator   # name/tags (Add only) + narrow overrides -> Part
  DiscardPartProcessingJobCommand.cs / Handler     # valid from Processing or ReadyForReview
  DeletePartCommand.cs / Handler
  TagPartCommand.cs / Handler / UntagPartCommand.cs / Handler
  GetPartsQuery.cs / Handler                        # tag filter, pagination
  GetPartQuery.cs / Handler                          # includes per-requirement unresolved-narrow-override flags
  GetPartProcessingJobQuery.cs / Handler
  LinkPrinterMachineProfileCommand.cs / Handler / Validator
  GetSlicingDefaultsQuery.cs / Handler / UpdateSlicingDefaultsCommand.cs / Handler
  PartProcessingWorkItem.cs             # queue payload shape consumed by the background service

api/src/OpenFarm.Infrastructure/Parts/
  PartRepository.cs / TagRepository.cs / PartProcessingJobRepository.cs
  PartFileClassifier.cs                 # ZipArchive-based, mirrors ProfileBundleParser's technique (research.md §1)
  AlreadySlicedFileMetadataParser.cs    # G-code comment + Bambu slice_info.config/multi-slot/plate-preview parsing
  CliSlicerEngine.cs                    # System.Diagnostics.Process wrapper around OrcaSlicer/Bambu Studio CLI
  FakeSlicerEngine.cs                   # test/dev double — deterministic canned SlicingResult
  PartFileProcessor.cs                  # implements IPartFileProcessor — branches to ISlicerEngine or
                                          # IAlreadySlicedFileMetadataParser based on RequiresSlicing
  LocalDiskPartFileStorage.cs           # implements IPartFileStorage
  PartProcessingBackgroundService.cs    # Channel<PartProcessingWorkItem> consumer, IServiceScopeFactory +
                                          # CreateTenantScope() pattern from PrinterConnectionBackgroundService;
                                          # on failure only, writes IActivityLogWriter directly (research.md §3)
  SlicerOptions.cs                      # CLI paths, timeout, MaxConcurrentSlicingOperations — bound from config
  Persistence/Configurations/PartConfiguration.cs / PartFilamentRequirementConfiguration.cs /
    TagConfiguration.cs / PartTagConfiguration.cs / PartProcessingJobConfiguration.cs /
    PartProcessingJobPreviewFilamentRequirementConfiguration.cs
    # PartConfiguration: FileFormat/SlicerEngineOverride HasConversion<string>(), UseXminAsConcurrencyToken()
    # PartFilamentRequirementConfiguration: partial unique index (PartId, SlotIndex) WHERE SlotIndex IS NOT NULL,
    #   bare Guid? columns for NarrowFilamentVariantId/NarrowFilamentProfileId (no HasOne)
  Persistence/Migrations/<timestamp>_AddPartsCatalog.cs
  Persistence/Migrations/<timestamp>_AddPrinterMachineProfileLink.cs
    # PrinterConfiguration: + HasOne<MachineProfile>().WithMany().HasForeignKey(...).OnDelete(SetNull)
  Persistence/Migrations/<timestamp>_AddOrganizationSlicingDefaults.cs

api/src/OpenFarm.Api/
  Endpoints/Parts/CreatePartEndpoint.cs / ReplacePartFileEndpoint.cs / DeletePartEndpoint.cs /
    ListPartsEndpoint.cs / GetPartEndpoint.cs / GetPartThumbnailEndpoint.cs / GetPartFileEndpoint.cs /
    TagPartEndpoint.cs / UntagPartEndpoint.cs
  Endpoints/Tags/ListTagsEndpoint.cs
  Endpoints/PartProcessingJobs/GetPartProcessingJobEndpoint.cs / GetPartProcessingJobThumbnailEndpoint.cs /
    ConfirmPartProcessingJobEndpoint.cs / DiscardPartProcessingJobEndpoint.cs
  Endpoints/Printers/LinkPrinterMachineProfileEndpoint.cs   # new file alongside existing printer endpoints
  Endpoints/Settings/GetSlicingDefaultsEndpoint.cs / UpdateSlicingDefaultsEndpoint.cs
  Hubs/FarmHubPartEventPublisher.cs      # implements IPartEventPublisher
  ExceptionHandling/DomainExceptionHandler.cs   # extended with this feature's exception -> status mappings

web/src/features/parts/                 # replaces the mock-data prototype
  PartsPage.tsx / PartDetailPage.tsx     # catalog list (DataTable + pagination, tag filter bar) + detail view
  components/AddOrReplacePartDialog.tsx  # the single continuous dialog (research.md §11): attaches file,
                                           # fires POST/PUT immediately, keeps name/tags editable via RHF+zod
                                           # while a PartProcessingJob runs, shows the review panel inline once
                                           # ReadyForReview, Save disabled until ready + named, Cancel discards
  components/PartProcessingReviewSection.tsx   # the in-dialog preview + per-requirement narrow-override picker
  components/PartsTable.tsx / PartFilterBar.tsx
  components/PartThumbnail.tsx           # new — static image + graceful no-thumbnail fallback (FR-014a)
  components/PartFilamentRequirementsList.tsx   # new — renders 1+ requirements with per-slot narrow overrides
  components/PartTagsEditor.tsx
  hooks/usePartsHub.ts                   # subscribes to PartProcessingJobStatusChanged/PartUpserted/PartDeleted
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
| `Printer.MachineProfileId` is an **enforced** nullable FK (`OnDelete(SetNull)`), while `PartFilamentRequirement.NarrowFilamentVariantId`/`NarrowFilamentProfileId` are **unenforced** bare `Guid?` columns | Each field's correctness requirement is genuinely different: FR-005a's farm-wide nozzle-diameter computation silently produces a wrong answer if a dangling link is tolerated, while spec.md explicitly wants a deleted narrow-override reference to degrade gracefully per-requirement | Applying one uniform rule was considered and rejected — spec.md itself asks for different failure behavior in each case |
| Thumbnail stored inline (`bytea`), always normalized to PNG with no content-type column, while the part's actual file goes through a new `IPartFileStorage` disk abstraction | A thumbnail is small, bounded, atomic with its owning row, and — once every source format is normalized at ingestion — always the same image type; the file itself can be large and needs independent re-download/streaming | Storing a per-row content-type alongside the bytes was considered and dropped once it became clear every real source can simply be re-encoded to one format at the single point of ingestion |
