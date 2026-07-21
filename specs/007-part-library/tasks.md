# Tasks: Part Library (with Sliced Files & In-App Slicing)

**Input**: Design documents from `/specs/007-part-library/` (plan.md, spec.md, research.md, data-model.md, contracts/part-library-api.md, quickstart.md)

**Tests**: Included. `plan.md`/`research.md` §12 explicitly mandate TDD for `NozzleDiameterResolver`,
`FilamentRequirementGrouper`, the Add/Replace/Confirm FluentValidation gates, and `IPartFileClassifier`'s
dispatch rule (tests precede implementation below); test-after/fixture-replay for the byte-level
G-code/3MF/project-settings parsers and the real slicer CLI invocation; test-after for orchestration
handlers, the background service's queue loop, EF Core plumbing, and React components; `WebApplicationFactory`
+ Testcontainers.PostgreSql integration tests for the full Processing → Ready for Review → Confirm/Discard
cycle.

**Organization**: Tasks are grouped by user story (spec.md priorities) to enable independent implementation
and testing. **Note on independence for this feature specifically**: spec.md itself frames User Stories 1–3
as jointly "this phase's minimum demoable capability" (all P1, explicitly "ranked alongside" each other) —
US1 (catalog/dialog), US2 (in-app slicing), and US3 (already-sliced parsing) each add one distinct piece of
`PartFileProcessor`'s behavior, and a fully working add-a-part flow needs all three. Each story below is
still independently *testable* against the Foundational scaffolding (using fakes/fixtures), but a live
end-to-end demo of US1's own acceptance scenarios needs at least one of US2 or US3 to also be done. **US3 has
no external-process dependency (parsing only) and is the fastest path to a working demo** — teams may
implement it before US2 despite the priority-order numbering below.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US5, mapping to spec.md's five user stories
- Every task lists its exact file path(s), per `plan.md`'s own Project Structure section

## Path Conventions

Existing monorepo layout (unchanged): `api/src/OpenFarm.{Domain,Application,Infrastructure,Api}/`,
`api/tests/OpenFarm.{Domain,Application,Infrastructure}.Tests/` + `OpenFarm.Api.IntegrationTests/`,
`web/src/features/parts/`, `infra/`.

---

## Phase 1: Setup

**Purpose**: External infrastructure this feature's slicing capability needs, independent of any story.

- [X] T001 Add an `openfarm_part_files` bind-mounted volume (and the API service's mount of it) to `infra/docker-compose.yml`, for the new `IPartFileStorage` disk abstraction
- [X] T002 [P] Add a build step to the API container's Dockerfile installing the headless OrcaSlicer CLI (and optionally Bambu Studio CLI) binary
- [X] T003 [P] Create `api/src/OpenFarm.Infrastructure/Parts/SlicerOptions.cs` (CLI binary paths, timeout, `MaxConcurrentSlicingOperations` default 2, `GenericFallbackMaterialProfile` per `SlicerEngine`) and bind it from `appsettings.json`/`.env.example`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entities, migration, storage/eventing seams, the classifier, the unifying processor/background
service, and the Add → Ready for Review → Confirm/Discard endpoints — the shared pipeline every user story
runs through. **No user story's independent test is runnable until this phase is complete.**

### Domain (`api/src/OpenFarm.Domain/Parts/`)

- [X] T004 [P] `PartFileFormat.cs` (enum: `Stl` \| `ThreeMf` \| `GCode`)
- [X] T005 [P] `SlicerEngine.cs` (enum: `OrcaSlicer` \| `BambuStudio`)
- [X] T006 [P] `EstimateStatus.cs` (enum: `Resolving` \| `Available` \| `Unavailable`)
- [X] T007 [P] `PartProcessingJobKind.cs` (`Add` \| `Replace`) and `PartProcessingJobStatus.cs` (`Processing` \| `ReadyForReview` \| `Confirmed` \| `Failed` \| `Discarded`)
- [X] T008 [P] `Tag.cs` entity
- [X] T009 [P] `PreviewFilamentRequirement.cs` (value record: `RequirementIndex`, `MaterialType?`, `DetectedColorHex?`, `DetectedSubBrand?`)
- [X] T010 [P] `PartFilamentRequirement.cs` entity (`MaterialType` `string?`, `DetectedColorHex?`, `DetectedSubBrand?`, `NarrowFilamentVariantId?`/`NarrowFilamentProfileId?` unenforced `Guid?`)
- [X] T011 [P] `PartThumbnail.cs` entity (`PartId`, `PlateIndex`, `Bytes`)
- [X] T012 [P] `PartProcessingJobPreviewThumbnail.cs` entity (`JobId`, `PlateIndex`, `Bytes`)
- [X] T013 `Part.cs` entity (depends on T004–T012: file fields, `NozzleDiameterMm`, `EstimateStatus`+nullable estimate fields, `SlicerEngineOverride`, `PlateCount`, `PartFilamentRequirement`/`PartThumbnail` collections, `UseXminAsConcurrencyToken()`)
- [X] T014 `PartProcessingJob.cs` entity (depends on T005–T009, T012: `Kind`/`Status`, snapshot fields, `Preview*` fields incl. `PreviewFilamentRequirements` shape, `ErrorMessage`, `RequestedByUserId`)
- [X] T015 [P] Domain exceptions in `api/src/OpenFarm.Domain/Parts/`: `PartNotFoundException.cs`, `TagNotFoundException.cs`, `DuplicatePartTagException.cs`, `UnsupportedPartFileFormatException.cs`, `PartFileParsingFailedException.cs`, `SlicingFailedException.cs`, `NoNozzleDiameterAvailableException.cs`, `UnslicedProjectMaterialParsingFailedException.cs`, `PartProcessingJobNotReadyForReviewException.cs`, `PartConcurrencyConflictException.cs`

### Application seams (`api/src/OpenFarm.Application/Parts/`)

- [X] T016 [P] `IPartRepository.cs`, `ITagRepository.cs`, `IPartProcessingJobRepository.cs`
- [X] T017 [P] `IPartFileStorage.cs` (`SaveAsync`/`OpenReadAsync`/`DeleteAsync`, keyed by org/part id)
- [X] T018 [P] `IPartEventPublisher.cs` (job status changed, `PartUpserted`, `PartEstimateResolved`, `PartEstimateUnavailable`, `PartDeleted`)
- [X] T019 [P] Contract-only declarations: `IPartFileClassifier.cs`, `IAlreadySlicedFileMetadataParser.cs`, `IUnslicedProjectMaterialResolver.cs`, `ISlicerEngine.cs` (concrete implementations arrive in US2/US3 — `PartFileProcessor` below compiles and is tested against these interfaces regardless)
- [X] T020 `IPartFileProcessor.cs` + `PartProcessingWorkItem.cs` (queue payload shape)

### Infrastructure plumbing

- [X] T021 [P] Write failing tests for `IPartFileClassifier`'s dispatch rule (STL/G-code always by extension; `.3mf` by Bambu `Metadata/slice_info.config` zip-entry presence) in `api/tests/OpenFarm.Infrastructure.Tests/Parts/PartFileClassifierTests.cs`
- [X] T022 `api/src/OpenFarm.Infrastructure/Parts/PartFileClassifier.cs` — implement to pass T021 (TDD, `ZipArchive`-based, patterned after `ProfileBundleParser`)
- [X] T023 [P] `api/src/OpenFarm.Infrastructure/Parts/LocalDiskPartFileStorage.cs` — implements `IPartFileStorage` against the new volume (T001)
- [X] T024 [P] `api/src/OpenFarm.Api/Hubs/FarmHubPartEventPublisher.cs` — implements `IPartEventPublisher` via `IHubContext<FarmHub>` + `FarmHub.GroupName(orgId)`, mirroring `FarmHubPrinterEventPublisher`
- [X] T025 [P] `api/src/OpenFarm.Infrastructure/Parts/PartRepository.cs`, `TagRepository.cs`, `PartProcessingJobRepository.cs`
- [X] T026 [P] EF configurations in `api/src/OpenFarm.Infrastructure/Persistence/Configurations/`: `PartConfiguration.cs` (incl. `HasConversion<string>()` for enums, `UseXminAsConcurrencyToken()`), `PartFilamentRequirementConfiguration.cs` (partial unique index on `(PartId, MaterialType, DetectedColorHex)` where color is non-null), `PartThumbnailConfiguration.cs`, `TagConfiguration.cs`, `PartTagConfiguration.cs`, `PartProcessingJobConfiguration.cs` (`PreviewFilamentRequirements` as `jsonb` via `HasConversion` + custom `ValueComparer`), `PartProcessingJobPreviewThumbnailConfiguration.cs` — all `IsMultiTenant()`
- [X] T027 Generate `api/src/OpenFarm.Infrastructure/Persistence/Migrations/<timestamp>_AddPartsCatalog.cs` from T004–T026 (`Tags`, `Parts`, `PartFilamentRequirements`, `PartTags`, `PartProcessingJobs`, `PartThumbnails`, `PartProcessingJobPreviewThumbnails`)

### Unifying processor, background service, and endpoints

- [X] T028 `api/src/OpenFarm.Infrastructure/Parts/PartFileProcessor.cs` — implements `IPartFileProcessor`; dispatch skeleton delegating to the injected `IAlreadySlicedFileMetadataParser`/`IUnslicedProjectMaterialResolver`/`ISlicerEngine` seams (concrete bodies land in US2/US3; orchestration itself is test-after per `research.md` §12)
- [X] T029 [P] `api/tests/OpenFarm.Application.Tests/Parts/PartFileProcessorTests.cs` — dispatch-only tests using fakes for each of T028's three dependencies
- [X] T030 `api/src/OpenFarm.Infrastructure/Parts/PartProcessingBackgroundService.cs` — bounded `Channel<PartProcessingWorkItem>` consumer, `IServiceScopeFactory`+`CreateTenantScope()` pattern from `PrinterConnectionBackgroundService`, the `Processing → ReadyForReview/Failed` transition, the §3b continuation-targeting skeleton (`ReadyForReview` refresh vs. `Confirmed`-then-write-`Part` vs. `Discarded`-drop), and a direct `IActivityLogWriter` write attributed to `RequestedByUserId` on a pre-confirm `Failed`
- [ ] T031 [P] `api/tests/OpenFarm.Infrastructure.Tests/Parts/PartProcessingBackgroundServiceTests.cs` — queue-consumption and continuation-targeting tests (test-after)
- [X] T032 [P] Write failing tests for `AddPartCommandValidator` (accepted file-format check only — no `filamentId`/`name`/`tags` fields exist to validate at submit) in `api/tests/OpenFarm.Application.Tests/Parts/AddPartCommandValidatorTests.cs`
- [X] T033 `api/src/OpenFarm.Application/Parts/AddPartCommand.cs` / `AddPartCommandHandler.cs` / `AddPartCommandValidator.cs` — creates `PartProcessingJob{Kind=Add, PartId=null}`, enqueues, to pass T032
- [X] T034 [P] Write failing tests for `ConfirmPartProcessingJobCommandValidator` (name required for `Add` only, `Replace` rejects `name`/`tags`, at most one narrow-override field per entry, `requirementIndex` must match a preview requirement) in `api/tests/OpenFarm.Application.Tests/Parts/ConfirmPartProcessingJobCommandValidatorTests.cs`
- [X] T035 `api/src/OpenFarm.Application/Parts/ConfirmPartProcessingJobCommand.cs` / `Handler.cs` / `Validator.cs` — creates/updates `Part` from the job's preview as-is (whatever `PreviewEstimateStatus` currently holds), to pass T034
- [X] T036 [P] `api/src/OpenFarm.Application/Parts/DiscardPartProcessingJobCommand.cs` / `Handler.cs` (valid from `Processing` or `ReadyForReview`; best-effort-cancels an in-flight slice)
- [X] T037 [P] `api/src/OpenFarm.Application/Parts/GetPartProcessingJobQuery.cs` / `Handler.cs`
- [X] T038 [P] `api/src/OpenFarm.Api/Endpoints/Parts/CreatePartEndpoint.cs` (`POST /api/v1/parts`, multipart `AllowFileUploads()`, `[Permission("parts:create")]`, `202`)
- [X] T039 [P] `api/src/OpenFarm.Api/Endpoints/PartProcessingJobs/GetPartProcessingJobEndpoint.cs`, `ConfirmPartProcessingJobEndpoint.cs` (`POST .../confirm`), `DiscardPartProcessingJobEndpoint.cs` (`DELETE .../confirm`)
- [X] T040 `api/src/OpenFarm.Api/ExceptionHandling/DomainExceptionHandler.cs` — extend with this feature's exception→`ProblemDetails` mappings (`PartNotFoundException`→404, `UnsupportedPartFileFormatException`→400, `NoNozzleDiameterAvailableException`→422, `PartProcessingJobNotReadyForReviewException`→409, `PartConcurrencyConflictException`→409)
- [ ] T041 [P] `api/tests/OpenFarm.Api.IntegrationTests/Parts/CreatePartEndpointTests.cs` — submit-time `400` (unrecognized format) case
- [X] T042 Register `PartProcessingBackgroundService` as a hosted service and every new Foundational interface's implementation in DI (`api/src/OpenFarm.Api` composition root)

### Frontend shared infra

- [X] T043 Extract shared `HubConnectionBuilder` setup into `web/src/lib/signalr/farmHubConnection.ts`
- [X] T044 Update `web/src/features/printers/hooks/useFarmHub.ts` to consume `farmHubConnection.ts` (T043) with no behavior change

**Checkpoint**: Foundation ready — an add request can be submitted, classified, queued, and its job polled/confirmed/discarded, though `ReadyForReview` isn't reachable yet (US2/US3 supply the two concrete processing paths).

---

## Phase 3: User Story 1 - Add and browse parts in the catalog (Priority: P1) 🎯 MVP

**Goal**: A real, browsable Part catalog — list/detail endpoints, and the single continuous Add/Replace dialog
(attach → concurrently edit name/tags → review → confirm) driven by SignalR, no polling.

**Independent Test**: Attach one file the system can process, confirm processing begins immediately while
name/tags stay editable, Save enables once nozzle diameter/material resolve (+ name entered), confirm, and see
the part in the catalog with per-plate thumbnails and real nozzle/material data.

### Implementation for User Story 1

- [X] T045 [P] [US1] `api/src/OpenFarm.Application/Parts/GetPartsQuery.cs` / `GetPartsQueryHandler.cs` (pagination; tag filter added in US5)
- [X] T046 [P] [US1] `api/src/OpenFarm.Application/Parts/GetPartQuery.cs` / `GetPartQueryHandler.cs` (detail shape incl. `narrowFilamentVariantUnresolved`/`narrowFilamentProfileUnresolved` flags)
- [X] T047 [P] [US1] `api/src/OpenFarm.Api/Endpoints/Parts/ListPartsEndpoint.cs` (`GET /api/v1/parts`, `[Permission("parts:list")]`)
- [X] T048 [P] [US1] `api/src/OpenFarm.Api/Endpoints/Parts/GetPartEndpoint.cs` (`GET /api/v1/parts/{id}`, `[Permission("parts:read")]`)
- [X] T049 [P] [US1] `api/src/OpenFarm.Api/Endpoints/Parts/GetPartFileEndpoint.cs` (`GET /api/v1/parts/{id}/file`, raw byte-stream download)
- [X] T050 [P] [US1] `api/src/OpenFarm.Application/Parts/DeletePartCommand.cs` / `Handler.cs` + `api/src/OpenFarm.Api/Endpoints/Parts/DeletePartEndpoint.cs` (`DELETE /api/v1/parts/{id}`, cascades tags/requirements/thumbnails, calls `IPartFileStorage.DeleteAsync`)
- [X] T051 [US1] Rebuild `api/` to regenerate `api/openapi.json` and run `bunx orval` in `web/` to produce `src/lib/api/generated` hooks for every Part Library endpoint built so far
- [X] T052 [P] [US1] `web/src/features/parts/hooks/usePartsHub.ts` — subscribes to `PartProcessingJobStatusChanged`/`PartUpserted`/`PartDeleted` (`PartEstimateResolved`/`PartEstimateUnavailable` wired in US2)
- [X] T053 [P] [US1] `web/src/features/parts/components/PartThumbnailGallery.tsx` — per-plate strip against `{thumbnails, plateCount}`, graceful missing-plate fallback (FR-014a)
- [X] T054 [P] [US1] `web/src/features/parts/components/PartFilamentRequirementsList.tsx` — renders 1+ grouped requirements with narrow-override picker
- [X] T055 [P] [US1] `web/src/features/parts/components/PartProcessingReviewSection.tsx` — in-dialog preview panel, "still resolving"/"estimating…" indicator (FR-009a)
- [X] T056 [US1] `web/src/features/parts/components/AddOrReplacePartDialog.tsx` (Add mode) — file attach fires `POST` immediately; RHF+zod name/tags stay editable concurrently (subscribed via T052); Save `disabled` until `ReadyForReview` + name entered; Cancel fires Discard (depends on T052–T055)
- [X] T057 [P] [US1] `web/src/features/parts/components/PartsTable.tsx` — real-data table (thumbnail, `materialTypes`, `estimateStatus` indicator per row)
- [X] T058 [US1] `web/src/features/parts/PartsPage.tsx` — `DataTable` + pagination wired to `GET /parts`, "Add Part" opens T056
- [X] T059 [US1] `web/src/features/parts/PartDetailPage.tsx` — wired to `GET /parts/{id}`, renders T053/T054, delete action
- [X] T060 [US1] Delete `web/src/features/parts/data/mockParts.ts`, `mockPartsStore.ts`, and now-superseded mock-only components (`FolderTile.tsx`, `NewFolderDialog.tsx`, `PartCard.tsx`, `MaterialAutoMatchSection.tsx`, `lib/colorMatch.ts`/`.test.ts`); confirm no dangling imports — Assemblies (Phase 6, still mock-only) depended on this store, so its own decoupled copy was relocated to `web/src/features/assemblies/data/`
- [ ] T061 [P] [US1] Vitest+RTL tests for `PartsPage`/`PartDetailPage`/`AddOrReplacePartDialog`/`PartsTable` covering Save-gating and catalog rendering
- [ ] T062 [US1] `api/tests/OpenFarm.Api.IntegrationTests/Parts/PartCatalogEndpointTests.cs` — full Processing→ReadyForReview→Confirm cycle for a simple already-sliced G-code fixture (from US3, T091), asserting the part appears via `GET /parts`/`GET /parts/{id}` with per-plate thumbnail + nozzle/material data
- [ ] T063 [P] [US1] `api/tests/OpenFarm.Api.IntegrationTests/Parts/DeletePartEndpointTests.cs` and `GetPartFileEndpointTests.cs`

**Checkpoint**: Catalog browse/detail/delete and the full dialog UX work end-to-end once US2 or US3 supplies at least one concrete processing path.

---

## Phase 4: User Story 2 - Automatically derive a part's data via in-app slicing (Priority: P1)

**Goal**: Automatic in-app slicing (STL / unsliced 3MF) targeting the farm's auto-detected nozzle diameter and
a file-resolved material, with the two-milestone (`ReadyForReview` before the estimate) split.

**Independent Test**: Given a file requiring slicing and at least one printer with a linked Machine Profile,
confirm the target material/nozzle diameter resolve with no operator input, the engine is invoked, nozzle/
material reach a reviewable, confirmable state ahead of the estimate/thumbnail, a full success additionally
shows per-plate thumbnails + estimate, and nothing appears in the catalog until confirmed.

### Implementation for User Story 2

- [X] T064 [P] [US2] `api/src/OpenFarm.Domain/Printers/Printer.cs` — add `MachineProfileId` (`Guid?`) + `LinkMachineProfile`/`UnlinkMachineProfile` methods
- [X] T065 [P] [US2] `api/src/OpenFarm.Domain/Organizations/Organization.cs` — add `DefaultSlicerEngine` + `SetDefaultSlicerEngine` method
- [X] T066 [US2] `api/src/OpenFarm.Infrastructure/Persistence/Configurations/PrinterConfiguration.cs` — `HasOne<MachineProfile>().WithMany().HasForeignKey(...).OnDelete(SetNull)`
- [X] T067 [US2] Generate `api/src/OpenFarm.Infrastructure/Persistence/Migrations/<timestamp>_AddPrinterMachineProfileLink.cs`
- [X] T068 [US2] Generate `api/src/OpenFarm.Infrastructure/Persistence/Migrations/<timestamp>_AddOrganizationSlicingDefaults.cs`
- [X] T069 [P] [US2] Write failing tests for `NozzleDiameterResolver` (mode selection; smallest-diameter tie-break; no qualifying printer → `null`) in `api/tests/OpenFarm.Application.Tests/Parts/NozzleDiameterResolverTests.cs`
- [X] T070 [US2] `api/src/OpenFarm.Application/Parts/NozzleDiameterResolver.cs` — `Resolve(printers, profiles)` via `IProfileBundleParser.ExtractMachineDisplayFields`, to pass T069
- [X] T071 [P] [US2] `api/src/OpenFarm.Application/Printers/LinkPrinterMachineProfileCommand.cs` / `Handler.cs` / `Validator.cs`
- [X] T072 [P] [US2] `api/src/OpenFarm.Api/Endpoints/Printers/LinkPrinterMachineProfileEndpoint.cs` (`PUT /api/v1/printers/{id}/machine-profile`)
- [X] T073 [P] [US2] `api/src/OpenFarm.Application/Parts/GetSlicingDefaultsQuery.cs` / `Handler.cs`, `UpdateSlicingDefaultsCommand.cs` / `Handler.cs` / `Validator.cs`
- [X] T074 [P] [US2] `api/src/OpenFarm.Api/Endpoints/Settings/GetSlicingDefaultsEndpoint.cs`, `UpdateSlicingDefaultsEndpoint.cs`
- [ ] T075 [US2] Extend `AddPartCommandValidator`/`Handler` (T033) — synchronous `422` gate when `RequiresSlicing` and `NozzleDiameterResolver` (T070) returns `null` (FR-006a), before job creation
- [ ] T076 [P] [US2] Fixture-replay tests for `IUnslicedProjectMaterialResolver` (parseable + unparseable `Metadata/project_settings.config`) in `api/tests/OpenFarm.Infrastructure.Tests/Parts/UnslicedProjectMaterialResolverTests.cs` + fixtures under `Parts/Fixtures/`
- [X] T077 [US2] `api/src/OpenFarm.Infrastructure/Parts/UnslicedProjectMaterialResolver.cs` — `ZipArchive` read of project settings (material + plate count), to pass T076
- [X] T078 [P] [US2] `api/src/OpenFarm.Infrastructure/Parts/FakeSlicerEngine.cs` — deterministic canned `SlicingResult` test/dev double
- [X] T079 [US2] `api/src/OpenFarm.Infrastructure/Parts/CliSlicerEngine.cs` — `System.Diagnostics.Process` wrapper invoking the configured OrcaSlicer/Bambu Studio CLI, per-plate thumbnail export, temp-output cleanup (manual real-CLI validation only, never a CI gate)
- [X] T080 [US2] Extend `api/src/OpenFarm.Infrastructure/Parts/PartFileProcessor.cs` (T028) — wire the requires-slicing branch: STL → `SlicerOptions.GenericFallbackMaterialProfile`; unsliced 3MF → `IUnslicedProjectMaterialResolver` (T077); both → `ISlicerEngine.SliceAsync`, setting `PreviewEstimateStatus=Resolving` then handing off to the continuation
- [X] T081 [US2] Extend `api/src/OpenFarm.Infrastructure/Parts/PartProcessingBackgroundService.cs` (T030) — wire the real §3b continuation: on slice completion, refresh the job's preview (still `ReadyForReview`) or write directly onto `Part` + publish `PartEstimateResolved`/`PartEstimateUnavailable` (already `Confirmed`), or drop the result (`Discarded`)
- [ ] T082 [P] [US2] `api/tests/OpenFarm.Api.IntegrationTests/Parts/RequiresSlicingPartEndpointTests.cs` — STL "any material" full cycle, unsliced-3MF-with-real-material full cycle, unsliced-3MF-unparseable-material rejection, no-linked-printer `422` rejection, using `FakeSlicerEngine`
- [ ] T083 [P] [US2] `api/tests/OpenFarm.Api.IntegrationTests/Parts/EarlyConfirmEstimateContinuationTests.cs` — confirm while `PreviewEstimateStatus=Resolving`, assert the trailing continuation correctly targets the confirmed `Part` on both success and an injected failure (FR-009c/FR-009d)
- [ ] T084 [P] [US2] `api/tests/OpenFarm.Api.IntegrationTests/Printers/LinkPrinterMachineProfileEndpointTests.cs` — link/unlink round trip + mixed-nozzle-diameter tie-break determination
- [ ] T085 [US2] `web/src/features/printers/PrinterDetailPage.tsx` — "Link Machine Profile" control wired to T072
- [ ] T086 [US2] `web/src/features/settings/` — Slicing Defaults section (default engine select) wired to T074
- [ ] T087 [US2] Extend `AddOrReplacePartDialog.tsx`/`PartProcessingReviewSection.tsx` (T056/T055) — `SlicerEngineOverride` picker (FR-008) + "estimating…" indicator swapping to real numbers on `PartEstimateResolved` (extend `usePartsHub`, T052)
- [ ] T088 [P] [US2] Vitest+RTL tests for the Link Machine Profile control, Slicing Defaults section, and the dialog's estimating→resolved transition

**Checkpoint**: STL and unsliced-3MF adds work end-to-end, including the early-confirm/trailing-estimate flow.

---

## Phase 5: User Story 3 - Manually provide an already-sliced file (Priority: P1)

**Goal**: Parse nozzle diameter/material(s)/estimates/per-plate thumbnails directly from an already-sliced
file's own metadata — grouped by distinct (material, color) pair across every plate/slot.

**Independent Test**: Add/replace with an already-sliced file; confirm nozzle diameter/material(s)/estimates
parse directly, a thumbnail is extracted per plate, review looks identical to a slicer-derived result, and
confirming stores the file as-is; an unparseable file rejects outright with no review step reached.

### Implementation for User Story 3

- [X] T089 [P] [US3] Write failing tests for `FilamentRequirementGrouper` (dedup by distinct `(material, color)` pair; the 4-plate/3-requirement worked example) in `api/tests/OpenFarm.Application.Tests/Parts/FilamentRequirementGrouperTests.cs`
- [X] T090 [US3] `api/src/OpenFarm.Application/Parts/FilamentRequirementGrouper.cs` — `Group(detectedRequirements)`, to pass T089
- [X] T091 [P] [US3] Author already-sliced test fixtures (single-material G-code; 4-plate multi-color `.gcode.3mf` with plates 1/3 sharing black PETG, plate 2 white PLA, plate 4 red PETG) under `api/tests/OpenFarm.Infrastructure.Tests/Parts/Fixtures/` — the single-material G-code is a committed fixture file; the 4-plate `.gcode.3mf` is built programmatically in the test itself (clearer to review as a diff than a committed binary zip)
- [X] T092 [P] [US3] Fixture-replay tests for `IAlreadySlicedFileMetadataParser` (G-code header parse, multi-plate Bambu `slice_info.config` parse, per-plate thumbnail extraction, unparseable → failure) in `api/tests/OpenFarm.Infrastructure.Tests/Parts/AlreadySlicedFileMetadataParserTests.cs`
- [X] T093 [US3] `api/src/OpenFarm.Infrastructure/Parts/AlreadySlicedFileMetadataParser.cs` — implements `IAlreadySlicedFileMetadataParser`, calls `FilamentRequirementGrouper` (T090), to pass T092
- [X] T094 [US3] Extend `api/src/OpenFarm.Infrastructure/Parts/PartFileProcessor.cs` (T028/T080) — wire the not-requires-slicing branch: atomic parse via T093 populating nozzle/material/estimate/thumbnails together, `PreviewEstimateStatus=Available` immediately
- [ ] T095 [P] [US3] `api/tests/OpenFarm.Api.IntegrationTests/Parts/AlreadySlicedPartEndpointTests.cs` — single-material full cycle, the 4-plate/3-requirement fixture (asserts exactly 3 grouped requirements, summed estimate, 4 thumbnails), every FR-006c rejection case (unparseable nozzle/material/estimate)

**Checkpoint**: All three P1 stories complete — every acceptance scenario in spec.md's User Stories 1–3 is runnable end-to-end. **This is the MVP.**

---

## Phase 6: User Story 4 - Replace a part's file (Priority: P2)

**Goal**: A destructive, all-or-nothing file replacement that keeps the part's identity/tags, either fully
succeeds or changes nothing.

**Independent Test**: Replace a tagged part's file; confirm identity/tags survive while file/thumbnails/derived
data update. Separately, a failing replacement leaves the part completely unchanged.

### Implementation for User Story 4

- [X] T096 [P] [US4] `api/src/OpenFarm.Application/Parts/ReplacePartFileCommand.cs` / `Handler.cs` / `Validator.cs` (`Kind=Replace`, existing `Part`'s name/tags untouched)
- [X] T097 [P] [US4] `api/src/OpenFarm.Api/Endpoints/Parts/ReplacePartFileEndpoint.cs` (`PUT /api/v1/parts/{id}/file`, `[Permission("parts:replace-file")]`)
- [ ] T098 [US4] Verify/extend `ConfirmPartProcessingJobCommandValidator` (T034/T035) covers the `Replace`-kind `400` case (name/tags supplied) against a real `ReplacePartFileCommand`-created job
- [X] T099 [US4] Wire `Part`'s `xmin` concurrency-conflict path — a concurrent replace throws `PartConcurrencyConflictException`, mapped to `409` (T040)
- [X] T100 [US4] `web/src/features/parts/components/AddOrReplacePartDialog.tsx` — Replace mode (reuse T056, minus name/tags fields)
- [X] T101 [US4] `web/src/features/parts/PartDetailPage.tsx` (T059) — "Replace file" action opening T100
- [ ] T102 [P] [US4] `api/tests/OpenFarm.Api.IntegrationTests/Parts/ReplacePartFileEndpointTests.cs` — successful replace (keeps name/tags), failed replace before confirm (unchanged), failed replace after early confirm (estimate unavailable, new file/nozzle/material kept), concurrent-replace `409`
- [ ] T103 [P] [US4] Vitest+RTL tests for Replace-mode dialog (no name/tags fields; Cancel leaves the existing part untouched)

**Checkpoint**: Replace works independently of Add, on top of whichever of US2/US3 the target file needs.

---

## Phase 7: User Story 5 - Organize the catalog with tags (Priority: P3)

**Goal**: Tag parts and filter the catalog by tag.

**Independent Test**: Tag several parts with different values; filter the catalog down to just the parts
sharing a given tag.

### Implementation for User Story 5

- [X] T104 [P] [US5] `api/src/OpenFarm.Application/Parts/TagPartCommand.cs` / `Handler.cs`, `UntagPartCommand.cs` / `Handler.cs` (get-or-create by name)
- [X] T105 [P] [US5] `api/src/OpenFarm.Application/Parts/ListTagsQuery.cs` / `Handler.cs`
- [X] T106 [P] [US5] `api/src/OpenFarm.Api/Endpoints/Parts/TagPartEndpoint.cs` (`POST /parts/{id}/tags`), `UntagPartEndpoint.cs` (`DELETE /parts/{id}/tags/{tagId}`)
- [X] T107 [P] [US5] `api/src/OpenFarm.Api/Endpoints/Tags/ListTagsEndpoint.cs` (`GET /api/v1/tags`)
- [X] T108 [US5] Extend `GetPartsQuery`/`GetPartsQueryHandler` (T045) — repeatable `tag` query param, AND-semantics filter
- [X] T109 [US5] Rebuild `api/`, regenerate `api/openapi.json`, run `bunx orval` to pick up the tag endpoints + filter param — done together with T051's regeneration pass
- [ ] T110 [P] [US5] `web/src/features/parts/components/PartTagsEditor.tsx`
- [X] T111 [P] [US5] `web/src/features/parts/components/PartFilterBar.tsx` — tag filter chips wired to `GET /parts?tag=`
- [X] T112 [US5] Wire `PartFilterBar` (T111) into `PartsPage.tsx` (T058)
- [ ] T113 [P] [US5] `api/tests/OpenFarm.Api.IntegrationTests/Parts/PartTagsEndpointTests.cs` — tag/untag/get-or-create/filter-by-tag
- [ ] T114 [P] [US5] Vitest+RTL tests for `PartTagsEditor`/`PartFilterBar`

**Checkpoint**: All five user stories independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T115 [P] `api/tests/OpenFarm.Api.IntegrationTests/ActivityLog/PartLibraryActivityLogTests.cs` — FR-017: part creation, file replacement, a discarded reviewed slice, and an org slicer-default change all recorded with actor/timestamp
- [ ] T116 Update `docs/FEATURE_PLAN.md` — fold the former Phase 23 (In-app slicing) into Phase 4, renumber the former Phase 24 down to Phase 23 (per spec.md Assumptions)
- [ ] T117 [P] Final regeneration pass: `api/openapi.json` + `bunx orval` in `web/`, confirm `src/lib/api/generated` has zero drift from the finalized contract
- [ ] T118 Run `dotnet format api/OpenFarm.slnx --verify-no-changes` and `bun run lint` / `bunx tsc -b` clean across the whole feature
- [ ] T119 Run `dotnet test api/OpenFarm.slnx` and `bun run test` full suites, confirm green
- [ ] T120 Execute `quickstart.md` Scenarios 1–7 end-to-end against `infra/docker-compose.yml` (manual/local validation, including the real OrcaSlicer CLI carve-out from T079) — partially attempted directly against a real Postgres + `dotnet run` (no Docker in that sandbox): migrations applied cleanly, `CreatePartEndpoint` returned real job ids over both curl and a real browser (Playwright), but the long-lived API process was repeatedly reaped by that sandbox's process lifecycle management before a job could finish processing, so the full Confirm → catalog → detail loop was never watched end-to-end live. Backstopped instead with `AlreadySlicedFileMetadataParserTests` (T092) asserting the exact values (nozzle/material/estimate/thumbnail) that would have populated the review panel. Needs a real re-run against `infra/docker-compose.yml` in an environment without that constraint before merge.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup (T003's `SlicerOptions` skeleton is referenced by T028). **Blocks every user story.**
- **User Stories (Phase 3–7)**: All depend on Foundational. US1/US2/US3 (all P1) are mutually interdependent for a *live end-to-end demo* (see the Organization note above) but each is separately assignable — US3 has no external-process dependency and is the fastest to a working demo. US4 depends on US2 and/or US3 having produced at least one working processing path (it replaces a file through the identical pipeline). US5 is purely additive on top of US1's `GetPartsQuery`/`PartsPage`.
- **Polish (Phase 8)**: Depends on all desired user stories being complete.

### Within Each Phase

- Domain entities before EF configurations before the migration (T004–T027).
- Interface declarations before the orchestrator that depends on them (T016–T020 before T028).
- TDD tests before their implementation (T021→T022, T032→T033, T034→T035, T069→T070, T089→T090).
- Fixture-replay tests are authored alongside (not strictly before) their parser, since fixture bytes and parser logic co-evolve (T076/T077, T091–T093).
- Backend endpoints before their Orval regeneration task (T038/T039 before T051; T072/T074 don't block T051 — only T038/T039/T047–T050 do).
- Frontend components before the dialog/page that composes them (T052–T055 before T056; T056 before T058).

### Parallel Opportunities

- Nearly all of T004–T012 (Domain value objects/enums) run in parallel — different files, no cross-dependencies.
- T016–T019 (Application interface declarations) run in parallel.
- T023–T026 (storage, event publisher, repositories, EF configs) run in parallel once T013–T020 exist.
- Once Foundational (Phase 2) is done, US1/US2/US3 can be staffed in parallel by different developers/agents — each mainly touches its own new files (`PartFileProcessor.cs` and `PartProcessingBackgroundService.cs` are the two files US2 and US3 both extend, so those two specific edits should be sequenced or merged carefully, not run blind in parallel).
- US4 and US5 can be staffed in parallel with each other once US1–US3 land.

---

## Parallel Example: Foundational Domain layer

```bash
Task: "PartFileFormat.cs enum in api/src/OpenFarm.Domain/Parts/PartFileFormat.cs"
Task: "SlicerEngine.cs enum in api/src/OpenFarm.Domain/Parts/SlicerEngine.cs"
Task: "EstimateStatus.cs enum in api/src/OpenFarm.Domain/Parts/EstimateStatus.cs"
Task: "PartProcessingJobKind.cs / PartProcessingJobStatus.cs in api/src/OpenFarm.Domain/Parts/"
Task: "Tag.cs entity in api/src/OpenFarm.Domain/Parts/Tag.cs"
```

## Parallel Example: User Story 2 vs. User Story 3

```bash
# US2 (slicing path) and US3 (already-sliced parsing path) are independent workstreams once
# Foundational is done — their only shared-file touchpoints are PartFileProcessor.cs (T080 vs T094)
# and PartProcessingBackgroundService.cs (T081, US2 only), which should merge sequentially:
Task: "NozzleDiameterResolver TDD (T069/T070) + CliSlicerEngine/FakeSlicerEngine (T078/T079)"
Task: "FilamentRequirementGrouper TDD (T089/T090) + AlreadySlicedFileMetadataParser (T092/T093)"
```

---

## Implementation Strategy

### MVP First (User Stories 1–3 together)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (blocks everything).
3. Complete Phases 3–5 (US1, US2, US3) — per the Organization note, these three together are the actual
   demoable MVP for this feature; spec.md itself treats them as one unit.
4. **STOP and VALIDATE**: run `quickstart.md` Scenarios 1–4c end-to-end.
5. Deploy/demo if ready.

### Incremental Delivery

1. Setup + Foundational → pipeline scaffolding ready, nothing reaches `ReadyForReview` yet.
2. Add US3 (fastest, no external process) → an already-sliced-file add/replace works end-to-end.
3. Add US2 → STL/unsliced-3MF automatic slicing works end-to-end, including the early-confirm split.
4. Add US1's remaining catalog/dialog polish → full browse/tag-free catalog UX.
5. Add US4 → file replacement.
6. Add US5 → tagging/filtering.
7. Polish (Phase 8) → activity-log coverage, docs renumbering, full lint/test pass, quickstart validation.

### Parallel Team Strategy

With multiple developers/agents: complete Setup + Foundational together, then split US2 (slicing) and US3
(parsing) across two people/agents in parallel (coordinating on the two shared files noted above), while a
third starts US1's frontend components (T052–T055) against Foundational's endpoints using fixtures/fakes
directly, ahead of US2/US3 landing. US4/US5 start once US1–US3 are stable.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- Commit after each task or logical group; run `dotnet format`/`bun run lint` before committing (CI-enforced).
- `PartFileProcessor.cs` and `PartProcessingBackgroundService.cs` are each touched by more than one phase
  (Foundational → US2 → US3, or Foundational → US2 twice) — treat each edit there as sequential, not [P],
  even across different user-story phases.
- Avoid: vague tasks, same-file conflicts run in parallel, cross-story dependencies that break independence
  beyond the two explicitly-shared files called out above.

---

## Phase 9: Convergence

- [X] T121 [US1] Retire the old page-routed Add/Edit flow: delete `web/src/features/parts/PartAddPage.tsx`, `PartAddPage.test.tsx`, and `PartEditPage.tsx` (all still on `mockPartsStore`), and update `web/src/app/routes.tsx` so `/parts`'s "Add Part" opens `AddOrReplacePartDialog` (T056) in place instead of navigating to a `/parts/new` route, and the `:id/edit` route becomes `:id`, routing to `PartDetailPage` (T059) per plan.md Project Structure / FR-001 (missing)
