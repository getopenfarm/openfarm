# Tasks: Phase 7 — Production runs, jobs & print queue

**Input**: Design documents from `/specs/014-production-run-queue/` (plan.md, spec.md, research.md,
data-model.md, contracts/production-run-queue-api.md, quickstart.md)

**Tests**: Included. `plan.md`'s Constitution Check scopes TDD to exactly: `PrintJobStatusTransitions`
(a Domain state machine, mirrors `PrinterStatusTransitions`), `ProductionDispatchMatcher.SelectNextJob`
(a pure selection function, research.md R4), the BOM-explosion-to-`PartRequirement` snapshot path
(`WebApplicationFactory` + Testcontainers, reusing `013-assembly-bom`'s fixtures), `ProductionDispatchBackgroundService`'s
startup reconciliation (the constitution's Durable Recovery guardrail), and `IJobTelemetrySync`'s
Cancelled-vs-Failed disambiguation rule (research.md R12, a small pure function). Everything else
(orchestration-only command/query handlers, EF configuration, repositories, `BambuLanConnector`'s real
FTPS/MQTT body — manual hardware verification only, never CI — and every React component/page) is
test-after, per constitution Principle II and this feature's own Constitution Check.

**Organization**: Tasks are grouped by user story (spec.md priorities: US1 P1, US2 P2, US3 P3, US4 P3).
spec.md's Clarifications-driven capabilities that don't have their own numbered user story — remote
pause/resume/cancel (FR-024), shortfall & requeue (FR-025), filament consumption on failure (FR-013a),
run-cancel never force-stopping a printing job (FR-017/FR-024d), and zero-printer-required creation
(FR-018) — are all scoped to **US1** below: every one of them is a facet of the same "watch a job go from
queued to produced, however it actually ends" lifecycle US1's own Acceptance Scenario 4 (Completed/Failed/
Cancelled → AwaitingClear) already puts in scope, and quickstart.md's Stories 5–9 (which validate exactly
these FRs) are all listed as sub-scenarios under US1's Independent Test below rather than as separate
phases, consistent with spec.md defining only four prioritized user stories. `ProductionDispatchMatcher`
is built incrementally across three stories rather than once in full: **US1** ships Unassigned-only
nozzle/material matching, **US2** adds the least-capable tie-break (FR-007b) and the Pinned-job branch
(FR-007c), **US3** adds the backfill branch (FR-006b) — matching each capability to the story whose
Acceptance Scenarios actually exercise it (spec.md US2 Acceptance Scenario 3 is the tie-break; US3's are
backfill). Likewise `PrintJob.Dispatch`'s Follow-latest revision re-read is US4-only; US1–US3 only ever
exercise Snapshot (FR-009a's default for every line created before US4's toggle exists to change it).

**Known pre-existing state this plan must account for**: no `Production`-shaped code exists anywhere in
this codebase yet — every file under `api/src/OpenFarm.{Domain,Application,Infrastructure,Api}/Production/`
and `web/src/features/production-runs/` is new. Three **existing, already-shipped** areas get amended,
not replaced: `Part` (Domain, +3 fields), `IPrinterConnector`/`IPartFileStorage` (Application, +4/1
methods), and `PrinterConnectionBackgroundService`/`BambuLanConnector`/`SimulatorPrinterConnector`/
`LocalDiskPartFileStorage`/the Part-file-replace command handler (Infrastructure) — treat every task that
touches one of these five existing files as a real amendment to shipped Phase 1/4 behavior, per plan.md's
Complexity Tracking (the non-destructive `ReplaceFile` change especially).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US4, mapping to spec.md's four user stories
- Every task lists its exact file path(s), per `plan.md`'s Project Structure section

## Path Conventions

Existing monorepo layout (unchanged): `api/src/OpenFarm.{Domain,Application,Infrastructure,Api}/`,
`api/tests/OpenFarm.{Domain,Application}.Tests/` + `OpenFarm.Api.IntegrationTests/`,
`web/src/features/production-runs/`.

---

## Phase 1: Setup

**Purpose**: Confirm the baseline is clean before extending it.

- [X] T001 Confirm baseline is green: `dotnet build api/OpenFarm.slnx --configuration Release`, `dotnet test api/OpenFarm.slnx`, `cd web && bun run lint && bunx tsc -b && bun run test`

**Checkpoint**: Baseline confirmed clean — any later failure is attributable to this feature's changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: All five new entities, every domain exception, the `PrintJobStatusTransitions` state machine,
the `Part` amendment, every repository/read-model contract, persistence (EF configs + both migrations),
the content-addressed file-storage change, and error-mapping plumbing — every user story below reads or
writes through these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain (`api/src/OpenFarm.Domain/Production/`)

- [X] T002 [P] Enums: `ProductionRunStatus.cs` (`Active`/`Complete`/`Closed`/`Cancelled`), `RunLineTargetKind.cs` (`Part`/`Assembly`), `RevisionMode.cs` (`Snapshot`/`FollowLatest`), `PrintJobStatus.cs` (`Queued`/`Printing`/`Paused`/`Failed`/`Completed`/`Cancelled`), `AssignmentMode.cs` (`Unassigned`/`Pinned`)
- [X] T003 Domain exceptions, one file each, in `api/src/OpenFarm.Domain/Production/`: `ProductionRunNotFoundException.cs`, `RunLineNotFoundException.cs`, `PrintJobNotFoundException.cs`, `RunLineMustTargetExactlyOnePartOrAssemblyException.cs`, `ProductionRunMustHaveAtLeastOneLineException.cs`, `RunLineTargetNotFoundException.cs`, `InvalidPrintJobStatusTransitionException.cs`, `InvalidRunCompletionException.cs`, `PinnedJobRequiresPrinterException.cs`, `PrintJobAlreadyDispatchedException.cs`, `ProductionRunConcurrencyConflictException.cs`, `FilamentConsumptionSumMismatchException.cs`, `JobNotEligibleForRemoteControlException.cs`, `PrintJobAlreadyRequeuedException.cs`, `PrintJobNotEligibleForRequeueException.cs` (data-model.md's exception table) (depends on T002)
- [X] T004 [P] TDD: `PrintJobStatusTransitionsTests.cs` in `api/tests/OpenFarm.Domain.Tests/Production/` — every legal edge succeeds (`Queued→Printing`, `Printing→Paused`, `Paused→Printing`, `Printing/Paused→Failed`, `Printing/Paused→Completed`, `Queued/Printing/Paused→Cancelled`); every other transition (e.g. `Completed→Printing`, `Cancelled→Printing`, `Queued→Paused`) throws `InvalidPrintJobStatusTransitionException` (data-model.md) (depends on T003)
- [X] T005 `PrintJobStatusTransitions.cs` in `api/src/OpenFarm.Domain/Production/` — static guarded-transition table mirroring `PrinterStatusTransitions`'s own shape, to pass T004 (depends on T004)
- [X] T006 [P] `RunLineSpec.cs` / `JobFilamentRequirementSpec.cs` factory-input records in `api/src/OpenFarm.Domain/Production/` (depends on T002)
- [X] T007 `ProductionRun.cs` entity — `Create(organizationId, name, dueDate, priority, notes, lines)`, `MarkComplete()` (throws `InvalidRunCompletionException` if any finite requirement unsatisfied), `Close()`, `Cancel()`; `Name = null` behaves identically everywhere (FR-001a) (depends on T003, T006)
- [X] T008 `RunLine.cs` entity — `Create` (defaults `RevisionMode` to `Snapshot` for finite/`FollowLatest` for unlimited per FR-009a, `Quantity == null` is the unlimited discriminator), `SetRevisionMode(mode)`, `SetBackfillLimits(printerCap, cumulativeUnitCeiling)` (depends on T003, T006)
- [X] T009 [P] `PartRequirement.cs` entity — `Create(organizationId, runLineId, partId, quantity)`, no back-reference to `Assembly`/`BomLine` beyond `RunLineId` (research.md R8, the FR-003 snapshot guarantee) (depends on T002)
- [X] T010 `PrintJob.cs` entity — `Create`, `Pin`/`Unpin`, `Reorder(newQueuePosition)`, `Dispatch(printerId, fileRevisionHash, fileRevisionNumber)`, `RequestCancel()` (sets `CancelRequestedAtUtc` only, never touches `Status` — FR-024a), `MarkStarted`/`MarkPaused`/`MarkResumed`/`MarkFailed`/`MarkCompleted`/`MarkCancelled` (all guarded by T005), `MarkRequeuedAs(replacementJobId)` (throws `PrintJobAlreadyRequeuedException` if already set), `RecordFilamentConsumption(consumptionLineId, spoolId, gramsConsumed)` (data-model.md) (depends on T003, T005, T006)
- [X] T011 [P] `JobFilamentConsumption.cs` entity — created at `PrintJob.Create` time with `SpoolId`/`GramsConsumed` null, filled in via `RecordFilamentConsumption` (depends on T002)

### `Part` amendment (`api/src/OpenFarm.Domain/Parts/`)

- [X] T012 Amend `Part.cs` — add `UnitsPerPrint` (`int`, default 1, FR-005a), `RevisionHash` (`string`, SHA-256 hex, FR-009c), `RevisionNumber` (`int`, starts at 1, incremented on every `ReplaceFile`); `Create`/`ReplaceFile` accept the pre-computed hash from their caller (research.md R6 — hashing happens in the Application-layer caller, same place `StoragePath` is already resolved)

### Application seams (`api/src/OpenFarm.Application/Production/`)

- [X] T013 [P] `IProductionRunRepository.cs` / `IPartRequirementRepository.cs` / `IPrintJobRepository.cs` (CRUD + org-scoped lookups, mirrors `IAssemblyRepository`'s shape)
- [X] T014 [P] `IProductionDispatcher.cs` — `void RequestDispatchAttempt(Guid printerId)` (research.md R4)
- [X] T015 [P] `IProductionEventPublisher.cs` — mirrors `IPrinterEventPublisher`'s shape (job/run status-change and dispatch-assignment events)
- [X] T016 [P] `IJobTelemetrySync.cs` — `Task ObserveAsync(Guid printerId, string gcodeState, CancellationToken ct)` (research.md R12)
- [X] T017 [P] Read-side records: `QueueJobRow.cs`, `RunDetailView.cs`/`RunLineSummary.cs`/`PartRequirementSummary.cs` in `api/src/OpenFarm.Application/Production/` (data-model.md's Read-side shapes — `PartRequirementSummary.JobsNeedingRequeue` is a plain filter, never a stored aggregate, research.md R13)
- [X] T018 Amend `IPartFileStorage.cs` in `api/src/OpenFarm.Application/Parts/` — save path keyed by content hash, not owner id (research.md R6, FR-009d)

### Infrastructure (`api/src/OpenFarm.Infrastructure/Production/` and `Persistence/`)

- [X] T019 [P] `ProductionRunRepository.cs` / `PartRequirementRepository.cs` / `PrintJobRepository.cs` in `api/src/OpenFarm.Infrastructure/Production/` — implement T013's interfaces; catch `DbUpdateConcurrencyException` from `SaveChangesAsync`, rethrow as `ProductionRunConcurrencyConflictException`, mirroring `AssemblyRepository`'s pattern (depends on T007-T011, T013)
- [X] T020 EF configurations in `api/src/OpenFarm.Infrastructure/Persistence/Configurations/`: `ProductionRunConfiguration.cs` (`xmin` `IsRowVersion()`, `Status` `HasConversion<string>()`, `.IsMultiTenant()`), `RunLineConfiguration.cs` (`TargetKind`/`RevisionMode`/`DefaultAssignmentMode` `HasConversion<string>()`, `CHECK (Quantity IS NULL OR Quantity >= 1)`, `CHECK (DefaultAssignmentMode = 'Unassigned' OR DefaultPinnedPrinterId IS NOT NULL)`, FK to `Printers` `ON DELETE SET NULL`), `PartRequirementConfiguration.cs` (`UNIQUE (RunLineId, PartId)`, FK to `Parts` `ON DELETE RESTRICT`), `PrintJobConfiguration.cs` (`xmin` `IsRowVersion()`, `Status`/`AssignmentMode` `HasConversion<string>()`, FKs to `Printers` `ON DELETE SET NULL`, index `(OrganizationId, Status, AssignmentMode, QueuePosition)`, partial index `(OrganizationId, PinnedPrinterId) WHERE PinnedPrinterId IS NOT NULL`), `JobFilamentConsumptionConfiguration.cs` (FK to `Spools` `ON DELETE SET NULL`) — per data-model.md's full table shapes; also added `CancelRequestedAtUtc`/`RequeuedAsJobId` (self-FK) to `PrintJobConfiguration.cs`, missed in data-model.md's original table sketch and backfilled there during this pass (depends on T007-T011)
- [X] T021 Amend the existing `PartConfiguration.cs` — map `UnitsPerPrint`/`RevisionHash`/`RevisionNumber` columns (depends on T012)
- [X] T022 Generate `api/src/OpenFarm.Infrastructure/Persistence/Migrations/<timestamp>_AddPartRevisionAndUnitsPerPrint.cs` via `dotnet ef migrations add AddPartRevisionAndUnitsPerPrint` — three additive `Parts` columns (`UnitsPerPrint int NOT NULL DEFAULT 1`, `RevisionHash text NOT NULL DEFAULT ''`, `RevisionNumber int NOT NULL DEFAULT 1`) plus a one-time raw-SQL data-backfill step, in the same migration, computing `RevisionHash` for every existing `Part` row from its currently stored file so no row is left on the placeholder default (data-model.md) (depends on T021)
- [X] T023 Generate `api/src/OpenFarm.Infrastructure/Persistence/Migrations/<timestamp>_AddProductionRunsAndJobs.cs` via `dotnet ef migrations add AddProductionRunsAndJobs` — creates `ProductionRuns`, `RunLines`, `PartRequirements`, `PrintJobs`, `JobFilamentConsumptions` with every FK/CHECK/index from T020; no change to any existing table (depends on T020, T022)
- [X] T024 Map every T003 exception to its HTTP status in the switch expression in `api/src/OpenFarm.Api/ExceptionHandling/DomainExceptionHandler.cs` (data-model.md's exception table: NotFound → 404; `RunLineMustTargetExactlyOnePartOrAssemblyException`/`ProductionRunMustHaveAtLeastOneLineException` → 422/400 per table; `InvalidPrintJobStatusTransitionException`/`InvalidRunCompletionException` → 422; `PinnedJobRequiresPrinterException`/`FilamentConsumptionSumMismatchException` → 400; `PrintJobAlreadyDispatchedException`/`ProductionRunConcurrencyConflictException`/`JobNotEligibleForRemoteControlException`/`PrintJobAlreadyRequeuedException`/`PrintJobNotEligibleForRequeueException` → 409) (depends on T003)
- [X] T025 Amend `LocalDiskPartFileStorage.cs` in `api/src/OpenFarm.Infrastructure/Parts/` — content-addressed path convention `{organizationId}/{revisionHash}{extension}`, implementing T018's amended interface (research.md R6) (depends on T018)
- [X] T026 Locate the Application-layer command handler that invokes `IPartFileStorage.DeleteAsync` on `Part.ReplaceFile`'s pre-replace path (`ReplacePartFileCommandHandler` or equivalent, Phase 4) and remove that unconditional delete call — the old revision's bytes are left in place so a Snapshot-mode job can still resolve them later (research.md R6, plan.md's post-Phase-1-design re-check note); pass the pre-computed hash into `Part.ReplaceFile` (T012) from here (depends on T012, T025)
- [X] T027 Register `IProductionRunRepository`→`ProductionRunRepository`, `IPartRequirementRepository`→`PartRequirementRepository`, `IPrintJobRepository`→`PrintJobRepository` as scoped services in `api/src/OpenFarm.Api/Program.cs` (depends on T019)

**Checkpoint**: Entities, persistence, content-addressed storage, and error mapping exist — User Story 1
can now begin.

---

## Phase 3: User Story 1 - Queue a part or assembly and watch it get produced end-to-end (Priority: P1) 🎯 MVP

**Goal**: An operator queues a Part/Assembly at a quantity in one action; the system explodes any Assembly
BOM once via Phase 6, snapshots `PartRequirement`s, eagerly creates whole-plate `PrintJob`s, auto-dispatches
Unassigned jobs to a matching idle printer with no operator action, pushes the file and detects
start/finish for a live-integrated printer (manual mark-started otherwise), moves the printer to
`AwaitingClear` on every terminal transition, lets the operator remotely pause/resume/cancel a printing
job with the printer's own telemetry as the only writer of the job's status, records filament consumption
even on failure, never force-stops a printing job when the whole run is cancelled, and leaves a
cancelled/failed finite job's requirement visibly short until an explicit one-click requeue. Requires
nothing beyond the catalog Parts/Assemblies already existing (no printer/connection/spool needed to create
a run).

**Independent Test**: quickstart.md Story 1 (queue → explode → auto-dispatch → complete → AwaitingClear →
unnamed-run display label → named-run confirmation gate), Story 5 (0-printer run creation, mismatch
flagged not blocking), Story 6 (filament recorded on a failed job), Story 7 (run cancel leaves a printing
job untouched), Story 8 (telemetry-confirmed pause/resume/cancel, never optimistic), and Story 9
(shortfall visibility + one-click requeue) — all independently exercisable with none of US2–US4 built yet.

### Tests for User Story 1

> T028 and T036 are TDD — write first, confirm every case fails before their paired implementation lands.
> The rest are test-after, written alongside/after their implementation.

- [X] T028 [P] [US1] TDD: `ProductionDispatchMatcherTests.cs` in `api/tests/OpenFarm.Domain.Tests/Production/` — a printer's nozzle diameter/loaded material matching a queued Unassigned job selects it; a non-matching printer selects nothing; multiple Unassigned candidates pick the topmost by `QueuePosition` (FR-008); no candidates returns null — pure, no DB (research.md R4 steps 1–3/6 only; tie-break and Pinned/backfill branches land in US2/US3)
- [ ] T029 [P] [US1] Test-after: `ProductionRunCrudEndpointTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — create/list/get round trip; empty `lines` rejected 400; a line with both/neither target kind rejected 400; `Pinned` with no `pinnedPrinterId` rejected 400; unresolvable `targetId` rejected 404; `close` before `Complete` rejected 409
- [ ] T030 [US1] TDD: `RunCreationExplosionSnapshotTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/`, against real Testcontainers Postgres, reusing `013-assembly-bom`'s fixture assemblies — a Part line at qty 20 with `unitsPerPrint=4` creates 5 `Queued`/`Unassigned` jobs (Acceptance Scenario 1); an Assembly line with nested sub-assemblies at qty 3 creates exactly one `PartRequirement` per distinct part, quantity summed across every BOM path (Acceptance Scenario 2); a later edit to the source Assembly's BOM does not alter the already-created run's requirements (FR-003) (depends on T028)
- [ ] T031 [P] [US1] Test-after: `AwaitingClearGatingTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — a job reaching `Completed`/`Failed`/`Cancelled` moves its printer to `AwaitingClear`, never `Idle` (FR-014a); a second dispatch attempt against that printer assigns nothing until `mark-ready` is called (FR-014b)
- [ ] T032 [US1] TDD: `DispatchReconciliationTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — a `PrintJob` left in the transient dispatch-in-flight state (`DispatchedAtUtc` set, unconfirmed) from a simulated prior process lifetime reverts to `Queued`/`Unassigned` on boot and re-enters ordinary matching; every already-`Idle` printer gets a dispatch attempt queued immediately on boot (constitution Durable Recovery guardrail) (depends on T028, T030)
- [ ] T033 [US1] TDD (disambiguation function only; rest test-after): `JobTelemetrySyncTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — fixture-replay `PAUSE`/`RUNNING`/`FINISH`/`FAILED` gcode events against a dispatched job (mirrors `FeedSlotTelemetrySyncHandler`'s own test shape); a terminal state with `CancelRequestedAtUtc` set always resolves `Cancelled` regardless of the specific terminal state reported; a terminal state with `CancelRequestedAtUtc` unset resolves `Failed` (research.md R12's documented fallback); start/pause/resume observed from raw gcode state drive the matching `Mark*` call exactly once
- [ ] T034 [P] [US1] Test-after: `RemotePrintJobControlEndpointTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — pause/resume/cancel on a live-integrated (Simulator) `Printing` job returns 202 with `status` still `"Printing"` in the response, then transitions once the simulator's telemetry confirms, delivered via `FarmHub` (quickstart Story 8 steps 1–3); the same three actions on a manual-brand job apply immediately, 200 (FR-024b, quickstart Story 8 step 5); a stop performed directly at the printer (no cancel call, `CancelRequestedAtUtc` stays null) converges to the fixture's simulated outcome (quickstart Story 8 step 4)
- [ ] T035 [P] [US1] Test-after: `ShortfallAndRequeueTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — cancelling a `Queued` job on a finite requirement surfaces it in `jobsNeedingRequeue`; the run cannot reach `Complete` (via `close`'s 409 gate) while unrequeued; `requeue` creates exactly one new job, same requirement, ordinary creation rules, clears the original from `jobsNeedingRequeue`; requeueing an already-requeued job 409s (`PrintJobAlreadyRequeuedException`); requeueing a backfill-line job 409s (`PrintJobNotEligibleForRequeueException`, FR-025d) (quickstart Story 9)
- [ ] T036 [P] [US1] Test-after: `RunCancelAndFilamentTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — cancelling a run with one `Printing` and two `Queued` jobs cancels only the two queued ones, leaves the printing job untouched, and that job still records its eventual outcome/filament usage (FR-017/FR-024d, quickstart Story 7); `fail` on a manual-brand job with a partial filament-consumption body decrements the matched `Spool.RemainingWeightGrams` by exactly the reported grams, not the full estimate (FR-013a, quickstart Story 6); a fresh organization with 0 printers still creates a run and queues jobs, none flagged as blocking creation (FR-018, quickstart Story 5)
- [ ] T037 [P] [US1] Test-after: `OpenFarm.Application.Tests/Production/*CommandHandlerTests.cs` — orchestration-only coverage for every command handler built below (each calls only a connector method/domain method, no state-machine logic of its own to TDD)

### Implementation for User Story 1

- [X] T038 [US1] `ProductionDispatchMatcher.cs` in `api/src/OpenFarm.Domain/Production/` — pure `SelectNextJob(printerNozzleDiameter, printerLoadedMaterials, IReadOnlyList<DispatchCandidate> candidates)`, Unassigned-only matching + topmost-`QueuePosition` selection, to pass T028 (depends on T028)
- [X] T039 [P] [US1] `DispatchCandidate.cs` internal record in `api/src/OpenFarm.Infrastructure/Production/` (data-model.md — `JobId`, `QueuePosition`, `RequiredNozzleDiameterMm`, filament requirement list)
- [X] T040 [US1] Amend `IPrinterConnector.cs` in `api/src/OpenFarm.Application/Printers/` — add `StartJobAsync(details, fileContent, jobName, ct)`, `PauseJobAsync(details, ct)`, `ResumeJobAsync(details, ct)`, `StopJobAsync(details, ct)` (research.md R5, all four together per the spec's reversed stop-command rule)
- [X] T041 [US1] Amend `SimulatorPrinterConnector.cs` in `api/src/OpenFarm.Infrastructure/Printers/Simulator/` — `StartJobAsync` begins a fake `Printing → Finish` telemetry sequence through the existing `ConnectAsync` stream; `PauseJobAsync`/`ResumeJobAsync` flip the sequence's internal paused flag; `StopJobAsync` makes the next tick emit a terminal state — all no real network call, needed for T030/T032/T033/T034's integration tests (depends on T040)
- [X] T042 [P] [US1] Amend `BambuLanConnector.cs` in `api/src/OpenFarm.Infrastructure/Printers/Bambu/` — `StartJobAsync` as an FTPS upload (port 990, add the `FluentFTP` NuGet package reference scoped to `OpenFarm.Infrastructure.csproj` only) followed by an MQTT print-start publish; `PauseJobAsync`/`ResumeJobAsync`/`StopJobAsync` each a single MQTT publish per the vendored `third_party/printers/bambulab/` reference; real hardware verification only, never a CI gate (research.md R5) (depends on T040)
- [X] T043 [US1] `ProductionDispatchQueryService.cs` in `api/src/OpenFarm.Infrastructure/Production/` — resolves a printer's nozzle diameter via `IProfileBundleParser.ExtractMachineDisplayFields` and currently-loaded material/color via `Spool`/`FeedSlot`/`FeedUnit` join (falling back to `Printer.AmsUnits[].Trays[]` when no tracked `Spool` is assigned), then queries `PrintJob` rows `WHERE Status='Queued' AND AssignmentMode='Unassigned'` joined to `PartRequirement`→`Part`, building the `DispatchCandidate` list T038 consumes, indexed on `(OrganizationId, Status, AssignmentMode, QueuePosition)` (research.md R4 steps 1–3) (depends on T039)
- [X] T044 [US1] `ProductionDispatchBackgroundService.cs` in `api/src/OpenFarm.Infrastructure/Production/` — bounded `Channel<Guid>` of printer ids (capacity 100, `FullMode.Wait`, matching `PartProcessingBackgroundService`'s convention), consumer calls T043 + T038, then `Dispatch()`s the selected job and `IPrinterConnector.StartJobAsync` (or, for a manual-brand printer, only sets `DispatchedAtUtc`, `Status` stays `Queued`, per FR-012); startup reconciliation per T032's spec: revert any dispatch-in-flight job to `Queued`/`Unassigned`, then queue a dispatch attempt for every already-`Idle` printer; implements `IProductionDispatcher.RequestDispatchAttempt`, to pass T032 (depends on T032, T041, T042, T043)
- [X] T045 [US1] Wire dispatch trigger points: `MarkPrinterReadyCommandHandler` (Phase 5) calls `IProductionDispatcher.RequestDispatchAttempt` immediately after `printer.TransitionTo(PrinterStatus.Idle)` succeeds; `PrinterConnectionBackgroundService.HandleEventAsync` calls it whenever `printer.ObserveStatus(gcodeState)` returns `true` and the printer's new status is `Idle` (research.md R4) (depends on T044)
- [X] T046 [US1] Register `ProductionDispatchBackgroundService` as `AddSingleton` + `AddHostedService` + `AddSingleton<IProductionDispatcher>` (the existing three-way `IPartProcessingDispatcher` registration pattern) in `api/src/OpenFarm.Api/Program.cs` (depends on T044)
- [X] T047 [US1] `JobTelemetrySync.cs` in `api/src/OpenFarm.Infrastructure/Production/` implementing `IJobTelemetrySync` — looks up the `PrintJob` with `DispatchedPrinterId` equal to the reporting printer and non-terminal status, compares raw `gcodeState` to `PrintJob.Status`, drives exactly one `Mark*` transition when they disagree; terminal-state resolution reads `CancelRequestedAtUtc` (`Cancelled` if set, `Failed` otherwise — the disambiguation function isolated for T033's TDD portion), to pass T033 (depends on T033)
- [X] T048 [US1] Amend `PrinterConnectionBackgroundService.HandleEventAsync` — inject `IJobTelemetrySync`, call it alongside the existing `IFeedSlotTelemetrySync` call on every event (research.md R12's third fan-out target) (depends on T047)
- [X] T049 [P] [US1] Register `IJobTelemetrySync`→`JobTelemetrySync` and `IProductionEventPublisher`→`FarmHubProductionEventPublisher` (T058) as scoped services in `api/src/OpenFarm.Api/Program.cs` (depends on T047)
- [ ] T050 [US1] `CreateProductionRunCommand.cs`/`Handler.cs`/`Validator.cs` in `api/src/OpenFarm.Application/Production/` — validator enforces `lines.Count >= 1`, each line exactly one target kind, `quantity >= 1` or null, `Pinned` requires `pinnedPrinterId`; handler resolves each line's target (an Assembly line calls `IAssemblyGraphQueryService.ExplodeAsync` and keeps only `PrintedParts`, FR-004a; a Part line synthesizes a single-entry list in-memory, research.md R1), creates one `PartRequirement` per distinct (line, part) pair (FR-004), eagerly creates `ceil(quantity/UnitsPerPrint)` `Queued` jobs per finite requirement stamping `FileRevisionHash`/`Number` from the current `Part` (FR-005b, R2), creates zero jobs for an unlimited line (R2), request DTO implements `IActivityLoggable` (FR-020), to pass T030 (depends on T009, T010, T012, T030)
- [ ] T051 [P] [US1] `GetProductionRunsQuery.cs`/`Handler.cs` — paginated list; `displayLabel` = `Name` if set else derived from contents (FR-001a, e.g. `"Bracket ×20"`); `jobSummary` counts per status (depends on T050)
- [ ] T052 [P] [US1] `GetProductionRunDetailQuery.cs`/`Handler.cs` — `RunDetailView` (T017), `PartRequirementSummary.JobsNeedingRequeue` = `Status IN (Cancelled,Failed) AND RequeuedAsJobId IS NULL` (research.md R13) (depends on T050)
- [ ] T053 [P] [US1] `CloseProductionRunCommand.cs`/`Handler.cs` — throws unless `Status == Complete` (FR-016c)
- [ ] T054 [US1] `CancelProductionRunCommand.cs`/`Handler.cs` — FR-017/FR-024d: cancels every `Queued`, undispatched job belonging to the run; never sends any command to a job already `Printing`; that job still records its eventual outcome (depends on T010, T050)
- [ ] T055 [P] [US1] `MarkPrintJobStartedCommand.cs`/`Handler.cs` — manual-brand path only (FR-012); 409 if the job's printer has live integration or the job isn't dispatched-but-not-started
- [ ] T056 [US1] `PausePrintJobCommand.cs`/`ResumePrintJobCommand.cs`/`Handler.cs` — for a live-integrated printer, call `IPrinterConnector.PauseJobAsync`/`ResumeJobAsync` and return without touching `Status` (FR-024a); for a manual-brand job, call `PrintJob.MarkPaused()`/`MarkResumed()` directly (FR-024b); 409 (`JobNotEligibleForRemoteControlException`) if the job isn't `Printing`/`Paused` as required (depends on T040, T047)
- [ ] T057 [US1] `CancelPrintJobCommand.cs`/`Handler.cs` — `Queued` job: `MarkCancelled()` immediately; `Printing` on a live-integrated printer: `IPrinterConnector.StopJobAsync` + `PrintJob.RequestCancel()`, `Status` untouched; `Printing` on a manual-brand printer: `MarkCancelled()` directly; 409 on an already-terminal job (FR-024) (depends on T040, T047, T056)
- [ ] T058 [US1] `FarmHubProductionEventPublisher.cs` in `api/src/OpenFarm.Api/Hubs/` implementing `IProductionEventPublisher` — pushes every run/job status change and dispatch assignment through the existing `FarmHub` → TanStack Query cache path, mirroring `PrinterEventPublisher`/`PartEventPublisher`'s shape; called from T044 (dispatch assignment), T047 (every telemetry-confirmed transition, including `Paused`), and every command handler above for its own System/operator-driven writes (depends on T015)
- [ ] T059 [P] [US1] `RequeuePrintJobCommand.cs`/`Handler.cs` — FR-025a: only valid on `Cancelled`/`Failed` with `RequeuedAsJobId == null` and a finite `PartRequirement`; creates exactly one new job for the same requirement via T050's ordinary creation rules, appends to queue end (`max(QueuePosition)+1024`), sets `MarkRequeuedAs` on the original; 409 (`PrintJobAlreadyRequeuedException`/`PrintJobNotEligibleForRequeueException`) otherwise (research.md R13) (depends on T010, T050)
- [ ] T060 [P] [US1] `CompletePrintJobCommand.cs`/`FailPrintJobCommand.cs`/`Handler.cs` — accepts the filament-consumption body (FR-013/013a — recorded proportional to what was actually consumed even on failure), calls `Spool.RecordUsage` per matched line, calls `PrintJob.MarkCompleted()`/`MarkFailed()` (manual-brand path only — a live job's completion is auto-detected via T047) and `printer.TransitionTo(AwaitingClear)` in the same transaction (FR-014a); 400 if a consumption line's `(materialType, colorHex)` pair doesn't match one already on the job (depends on T011, T047)
- [ ] T061 [P] [US1] `GetQueueJobsQuery.cs`/`Handler.cs` — `QueueJobRow` (T017), flat `ORDER BY QueuePosition`, paginated, `view=queue`/`view=backfill` (`isBlocked` always `false` until US2's Pin exists — the field is present now so US2 only has to fill in its logic, not add the column) (depends on T017, T050)
- [ ] T062 [US1] `CreateProductionRunEndpoint.cs` (`POST /api/v1/production-runs`, `[Permission("production-runs:create")]`, 201/400/404), `ListProductionRunsEndpoint.cs` (`GET /api/v1/production-runs`, `[Permission("production-runs:view")]`), `GetProductionRunEndpoint.cs` (`GET /api/v1/production-runs/{id}`, `[Permission("production-runs:view")]`, 404), `CloseProductionRunEndpoint.cs` (`POST .../{id}/close`, `[Permission("production-runs:close")]`, 200/409), `CancelProductionRunEndpoint.cs` (`POST .../{id}/cancel`, `[Permission("production-runs:cancel")]`, 200/409) in `api/src/OpenFarm.Api/Endpoints/ProductionRuns/` (depends on T050-T054)
- [ ] T063 [US1] `ListQueueJobsEndpoint.cs` (`GET /api/v1/print-jobs?view=queue`, `[Permission("print-jobs:view")]`), `GetPrintJobEndpoint.cs` (`GET /api/v1/print-jobs/{id}`, `[Permission("print-jobs:view")]`, 404), `MarkPrintJobStartedEndpoint.cs` (`POST .../{id}/mark-started`, `[Permission("print-jobs:update-status")]`, 200/409), `PausePrintJobEndpoint.cs`/`ResumePrintJobEndpoint.cs` (`[Permission("print-jobs:remote-control")]`, 202/200/409), `CancelPrintJobEndpoint.cs` (`[Permission("print-jobs:cancel")]`, 200/202/409), `RequeuePrintJobEndpoint.cs` (`POST .../{id}/requeue`, `[Permission("print-jobs:requeue")]`, 201/409), `CompletePrintJobEndpoint.cs`/`FailPrintJobEndpoint.cs` (`[Permission("print-jobs:update-status")]`, 200/400) in `api/src/OpenFarm.Api/Endpoints/PrintJobs/` (depends on T055-T061)
- [ ] T064 [US1] Register every T050-T061 command/query handler type in `api/src/OpenFarm.Api/Program.cs` (depends on T062, T063)
- [ ] T065 [US1] Rebuild `api/` to regenerate `api/openapi.json` and run `bunx orval` in `web/` to produce `src/lib/api/generated` hooks for every endpoint built so far (depends on T064)
- [ ] T066 [US1] `web/src/features/production-runs/components/RunLineEditor.tsx` — target-kind picker (Part/Assembly), quantity (blank = unlimited), assignment mode, pinned-printer picker (shown only when Pinned) (depends on T065)
- [ ] T067 [US1] `web/src/features/production-runs/NewProductionRunPage.tsx` — Page-first (`docs/UI_CONVENTIONS.md`), react-hook-form + zod, one or more `RunLineEditor` rows, single `POST /production-runs` call (depends on T066)
- [ ] T068 [P] [US1] `web/src/features/production-runs/ProductionRunsPage.tsx` — `DataTable` + pagination wired to `GET /production-runs`, row click navigates to detail, "New run" navigates to `/production-runs/new` (depends on T065)
- [ ] T069 [US1] `web/src/features/production-runs/components/ShortfallBanner.tsx` — renders `PartRequirementSummary.jobsNeedingRequeue` with a one-click Requeue action (research.md R13) (depends on T065)
- [ ] T070 [US1] `web/src/features/production-runs/ProductionRunDetailPage.tsx` — lines/requirements/jobs, expand-in-place (mirrors `AssemblyDetailPage`'s pattern), `ShortfallBanner` (T069) per requirement with an unrequeued shortfall, "Mark ready"'s named-run confirmation prompt is a frontend gate in front of the same `close`/`MarkComplete` flow — no confirmation for an unnamed run (FR-016b, data-model.md's explicit note that this is never a second server-side flow) (depends on T069)
- [ ] T071 [US1] `web/src/features/production-runs/components/QueueJobRow.tsx` — one row: status, part/run labels, plate quantity, `isBlocked` flag (always false until US2) (depends on T065)
- [ ] T072 [US1] `web/src/features/production-runs/QueuePage.tsx` — flat queue list wired to `GET /print-jobs?view=queue`, rendering T071's rows (non-virtualized for now — TanStack Virtual lands in US2 alongside reordering, which is the first feature that needs a stable sliceable list) (depends on T071)
- [ ] T073 [US1] `web/src/features/production-runs/components/JobRemoteControls.tsx` — Pause/Resume/Cancel buttons for a `Printing`/`Paused` job; deliberately no optimistic UI — a click shows a "requested, awaiting confirmation" state until the `FarmHub` push actually changes the job's status (research.md R12, FR-024a) (depends on T065)
- [ ] T074 [US1] Wire `JobRemoteControls` (T073) into `QueueJobRow`/`ProductionRunDetailPage`'s job rows; wire the SignalR `FarmHub` client to invalidate/update the relevant TanStack Query cache entries on production-run/job events (mirrors the existing printer/part event wiring) (depends on T070, T072, T073)
- [ ] T075 [US1] Add `/production-runs`, `/production-runs/new`, `/production-runs/:id` routes (elements T067/T068/T070) to `web/src/app/routes.tsx`; **replace** the existing `/jobs` route's element with `QueuePage` (T072) in place — same path, same `nav-config.ts` `NavItem` (`jobs`, `title: 'Jobs'`, `url: '/jobs'`), no new nav entry or URL for the queue itself, so the existing "Jobs" nav slot starts pointing at the real page instead of the mock (depends on T074)
- [ ] T075a [US1] Retire the pre-existing jobs mockup now superseded by T075: delete `web/src/features/jobs/` (`JobsPage.tsx`, `JobsPage.test.tsx`, `components/JobsTable.tsx`, `data/mockJobs.ts`, `data/mockJobsStore.ts`) and `web/src/components/print-queue-dialog/` (`PrintQueueDialog.tsx`, `PrintQueueDialog.test.tsx` — currently unwired to any page, and its model of picking one specific printer plus a Low/Normal/High priority toggle doesn't match this feature's design: dispatch defaults to `Unassigned` auto-matching, and FR-008a rules out any priority-based scheduling). If a "queue this part" quick action from the Parts feature is still wanted, it belongs as a fresh, small dialog built against the real `POST /production-runs` in a later pass — not a rewire of this one, since its underlying mental model, not just its data source, is wrong (depends on T075)
- [ ] T076 [P] [US1] Vitest+RTL tests for `ProductionRunsPage`/`NewProductionRunPage`/`ProductionRunDetailPage`/`QueuePage`/`RunLineEditor`/`ShortfallBanner`/`JobRemoteControls` in `web/src/features/production-runs/**/*.test.tsx` — cover the named-run confirmation gate, shortfall banner rendering, and remote-control's non-optimistic pending state (depends on T075a)

**Checkpoint**: User Story 1 is fully functional and independently testable — run quickstart.md Stories 1,
5, 6, 7, 8, 9, and the Activity log check.

---

## Phase 4: User Story 2 - Control what prints next and pin a job to a specific printer (Priority: P2)

**Goal**: Manual queue reordering (fractional `QueuePosition`), pinning a job to one printer (never
reassigned, visibly blocked when that printer is unavailable), and a least-capable tie-break among
multiple qualifying idle printers.

**Independent Test**: quickstart.md Story 2 — reorder two Unassigned jobs matching one idle printer,
confirm the now-topmost dispatches first; pin a job to an offline printer, confirm it stays queued and
flagged `isBlocked`, never picked up by a different matching printer; with two idle printers differing
only in AMS tray count, confirm the fewer-tray one is chosen.

### Tests for User Story 2

- [ ] T077 [P] [US2] Test-after: extend `ProductionDispatchMatcherTests.cs` (T028) — two qualifying idle printers with differing `AmsUnits` tray counts: the fewer-tray one is selected (research.md R11); a full tie falls back to `Printer.Id` ordering; a `Pinned` job is only ever considered against its own `PinnedPrinterId`, never matched against a different printer even if it would otherwise qualify (FR-007c)
- [ ] T078 [P] [US2] Test-after: `QueueOrderingAndPinningTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — reordering the lower of two matching jobs to the top makes it dispatch first; pinning a job to an offline/maintenance/`AwaitingClear` printer keeps it `Queued` with `isBlocked:true` in `GET /print-jobs?view=queue`, and it is never dispatched to a different matching idle printer; `reorder`/`pin`/`unpin` on a non-`Queued` job 409s (`PrintJobAlreadyDispatchedException`)

### Implementation for User Story 2

- [ ] T079 [US2] Extend `ProductionDispatchMatcher.SelectNextJob` (T038) — sort multiple qualifying Unassigned candidates by `Printer.AmsUnits.Sum(u => u.Trays.Count)` ascending, `Printer.Id` as the deterministic tie-break (FR-007b, research.md R11); add the Pinned-job branch: if no Unassigned match, check `Pinned` jobs `WHERE PinnedPrinterId = P.Id AND Status = 'Queued'`, topmost by `QueuePosition`, never matched against any other printer (FR-007c, research.md R4 step 4), to pass T077 (depends on T028, T077)
- [ ] T080 [US2] Extend `GetQueueJobsQuery.cs`/`Handler.cs` (T061) — compute real `isBlocked` (`AssignmentMode == Pinned` and the target printer is not `Idle`) (FR-007c) (depends on T061, T079)
- [ ] T081 [P] [US2] `ReorderPrintJobCommand.cs`/`Handler.cs`/`Validator.cs` in `api/src/OpenFarm.Application/Production/` — new `QueuePosition` = midpoint between `afterJobId`'s position and its current successor's (or `±1024` at either end), research.md R3; 404 on missing job/`afterJobId`, 409 if the job is no longer `Queued`
- [ ] T082 [P] [US2] `PinPrintJobCommand.cs`/`UnpinPrintJobCommand.cs`/`Handler.cs` — `Pin` requires `printerId` (400 otherwise, `PinnedJobRequiresPrinterException`); both 404 on missing job/printer, 409 if not `Queued`
- [ ] T083 [US2] `ReorderPrintJobEndpoint.cs` (`POST /api/v1/print-jobs/{id}/reorder`, `[Permission("print-jobs:reorder")]`, 200/404/409), `PinPrintJobEndpoint.cs`/`UnpinPrintJobEndpoint.cs` (`[Permission("print-jobs:pin")]`, 200/400/404/409) in `api/src/OpenFarm.Api/Endpoints/PrintJobs/` (depends on T081, T082)
- [ ] T084 [US2] Register `ReorderPrintJobCommand`/`PinPrintJobCommand`/`UnpinPrintJobCommand` handler types in `api/src/OpenFarm.Api/Program.cs`; rebuild `api/` and run `bunx orval` in `web/` to add the reorder/pin/unpin hooks (depends on T083)
- [ ] T085 [US2] Convert `QueuePage.tsx` (T072) to a `TanStack Virtual`-backed list (this codebase's first real consumer, constitution Principle V, `CLAUDE.md`'s own earmark) with drag-to-reorder calling T084's reorder hook (depends on T084)
- [ ] T086 [US2] Add pin/unpin controls to `QueueJobRow.tsx` (T071) — a printer picker for pin, an unpin action, and a visibly distinct "blocked on {printer}" indicator when `isBlocked:true` (FR-007c) (depends on T085)
- [ ] T087 [P] [US2] Vitest+RTL tests for `QueuePage`'s virtualized reorder interaction and `QueueJobRow`'s pin/unpin/blocked-indicator states in `web/src/features/production-runs/**/*.test.tsx`

**Checkpoint**: User Stories 1 AND 2 both work independently — run quickstart.md Story 2.

---

## Phase 5: User Story 3 - Keep idle printers busy with an evergreen backfill line (Priority: P3)

**Goal**: An unlimited RunLine that lazily produces one plate at a time whenever an idle printer has no
finite job to take, respecting a per-printer occupancy cap (default 1) and an optional cumulative-unit
ceiling, never queued ahead as a batch, and always losing to a finite job on the same printer.

**Independent Test**: quickstart.md Story 3 — a finite job always wins over a matching backfill line;
exhausting the finite line's jobs then dispatching produces exactly one lazily-created backfill job; a cap
of 1 across two idle matching printers only fills one; a cumulative ceiling stops further dispatch once
reached; progress displays a cumulative count only, never a percentage.

### Tests for User Story 3

- [ ] T088 [P] [US3] Test-after: extend `ProductionDispatchMatcherTests.cs`/new `BackfillTierTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — a finite job and a matching backfill line both eligible for one idle printer: the finite job is always assigned (FR-006b); only a backfill line matching: exactly one new job is lazily created and dispatched, never a batch (FR-006a); `backfillPrinterCap: 1` with two idle matching printers: only one receives a job; a `backfillCumulativeUnitCeiling` reached: no further dispatch for that line until raised/edited (FR-006d)

### Implementation for User Story 3

- [ ] T089 [US3] Extend `ProductionDispatchQueryService.cs` (T043) — resolve backfill-eligible `RunLine`s (`Quantity IS NULL`) matching printer P's nozzle/material, computing current occupancy (`COUNT(DISTINCT PrinterId)` over the line's `Printing`/dispatched-but-unconfirmed/Pinned-and-queued jobs) and cumulative units (`SUM(Part.UnitsPerPrint)` over the line's `Completed` jobs) per research.md R4's formulas (depends on T043)
- [ ] T090 [US3] Extend `ProductionDispatchMatcher.SelectNextJob` (T079) — only consulted once no finite (Unassigned or Pinned) job can be assigned to P (FR-006b); skips a line whose occupancy already meets its `BackfillPrinterCap` or whose cumulative units already meet its `BackfillCumulativeUnitCeiling`, to pass T088 (depends on T079, T088, T089)
- [ ] T091 [US3] Extend `ProductionDispatchBackgroundService.cs` (T044) — when T090 selects a backfill line rather than an existing job, lazily create exactly one `PrintJob` for it (same eager-creation rules as T050, one plate, `Snapshot`/`FollowLatest` per the line's mode) before dispatching (FR-006a, research.md R2) (depends on T044, T090)
- [ ] T092 [P] [US3] `SetRunLineBackfillLimitsCommand.cs`/`Handler.cs` in `api/src/OpenFarm.Application/Production/` — FR-006c/d, 409 if the line is not unlimited
- [ ] T093 [US3] `SetRunLineBackfillLimitsEndpoint.cs` (`PUT /api/v1/production-runs/{runId}/lines/{lineId}/backfill-limits`, `[Permission("production-runs:edit")]`, 200/409) in `api/src/OpenFarm.Api/Endpoints/ProductionRuns/`; register the handler in `Program.cs`; rebuild `api/` and run `bunx orval` in `web/` (depends on T092)
- [ ] T094 [US3] `web/src/features/production-runs/components/BackfillLimitsPanel.tsx` — printer-cap and cumulative-ceiling editor (blank = no ceiling, spec Assumptions' off-by-default), wired into `ProductionRunDetailPage.tsx`'s line view for an unlimited line, rendering its progress as a cumulative produced count only, never a completion percentage (FR-006e) (depends on T093)
- [ ] T095 [P] [US3] Vitest+RTL tests for `BackfillLimitsPanel` (cumulative-count-only rendering, cap/ceiling form validation) in `web/src/features/production-runs/components/BackfillLimitsPanel.test.tsx`

**Checkpoint**: User Stories 1, 2, AND 3 all work independently — run quickstart.md Story 3.

---

## Phase 6: User Story 4 - Choose whether a line prints exactly what was queued or always the latest file (Priority: P3)

**Goal**: An explicit, editable-after-queueing Snapshot/Follow-latest toggle per line; a Follow-latest
plate resolves the Part's current file revision at its own dispatch time (never retroactively altering an
already-printing plate); every job records exactly which revision it printed; a run spanning more than one
revision is surfaced honestly.

**Independent Test**: quickstart.md Story 4 — a Snapshot line's jobs keep their original revision after
the Part's file is later replaced; a Follow-latest line's already-printing job is unaffected by a
mid-print replace while the next dispatched plate picks up the new revision; defaults (Snapshot for
finite, Follow-latest for unlimited) hold when unspecified; a run that printed under two revisions is
flagged as spanning multiple revisions.

### Tests for User Story 4

- [ ] T096 [P] [US4] Test-after: `RevisionModeTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Production/` — a Snapshot line's jobs (already created or created later) keep the revision pinned at queue time after `replace-file` (Acceptance Scenario 1); a Follow-latest line's already-`Printing` job's `fileRevisionHash` is unchanged by a mid-print replace, while the next plate dispatched afterward records the new hash (Acceptance Scenario 2); an unspecified `revisionMode` defaults per FR-009a (Acceptance Scenario 3); a line that printed under two revisions is surfaced as spanning multiple revisions (Acceptance Scenario 4); `revision-mode` changed mid-run never alters a currently-printing job's own recorded revision (Acceptance Scenario 5)

### Implementation for User Story 4

- [ ] T097 [P] [US4] `SetRunLineRevisionModeCommand.cs`/`Handler.cs` in `api/src/OpenFarm.Application/Production/` — FR-009b, affects only plates dispatched after the call
- [ ] T098 [US4] Extend the dispatch path (`ProductionDispatchBackgroundService.cs`/T044's `Dispatch()` call site) — when the owning `RunLine.RevisionMode == FollowLatest`, re-read `Part.RevisionHash`/`RevisionNumber` immediately before calling `StartJobAsync` and pass them to `PrintJob.Dispatch`'s revision-overwrite overload; under `Snapshot`, call the non-overwriting overload (research.md R6), to pass T096 (depends on T044, T096, T097)
- [ ] T099 [P] [US4] Extend `RunLineSummary` (T017/`GetProductionRunDetailQuery`, T052) with a computed `SpansMultipleRevisions` (`DISTINCT FileRevisionHash` count over the line's jobs `> 1`) — never a stored/persisted field (FR-009e) (depends on T052)
- [ ] T100 [US4] `SetRunLineRevisionModeEndpoint.cs` (`PUT /api/v1/production-runs/{runId}/lines/{lineId}/revision-mode`, `[Permission("production-runs:edit")]`, 200) in `api/src/OpenFarm.Api/Endpoints/ProductionRuns/`; register the handler in `Program.cs`; rebuild `api/` and run `bunx orval` in `web/` (depends on T097)
- [ ] T101 [US4] Add the Snapshot/Follow-latest toggle to `RunLineEditor.tsx` (T066) and a read-only mode indicator + "spans N revisions" badge (from T099) to `ProductionRunDetailPage.tsx`'s line view (depends on T099, T100)
- [ ] T102 [P] [US4] Vitest+RTL tests for `RunLineEditor`'s revision-mode toggle and the spans-multiple-revisions badge in `web/src/features/production-runs/**/*.test.tsx`

**Checkpoint**: All four user stories independently functional — run quickstart.md Stories 1–9 end-to-end.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Activity-log coverage (FR-020), formatting/lint gates, and full end-to-end validation across
all four stories.

- [ ] T103 [P] `api/tests/OpenFarm.Api.IntegrationTests/ActivityLog/ProductionRunQueueActivityLogTests.cs` — FR-020: run creation, run/job status transitions (including pause/resume/cancel *requests* and their telemetry-confirmed outcomes), dispatch assignment (`ActorType.System`), cancellation, revision-mode changes, and requeue actions all recorded with actor (operator or `System`) and timestamp
- [ ] T104 [P] `dotnet format api/OpenFarm.slnx --verify-no-changes` (fix any formatting drift from Phases 2–6)
- [ ] T105 [P] `cd web && bun run lint && bun run format && bunx tsc -b` (fix any drift from Phases 3–6)
- [ ] T106 Run `dotnet test api/OpenFarm.slnx` and `cd web && bun run test` full suites, confirm green
- [ ] T107 Execute quickstart.md Stories 1–9 end-to-end against the local stack (`infra/`), using `SimulatorPrinterConnector` for every live-integration scenario, on a fresh organization (SC-005's 1,000+ job queue browsability, SC-006's finite-never-waits-behind-backfill timing, and every other Success Criterion) — confirm every step's expected result

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — run first.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks every user story.**
- **User Story 1 (Phase 3)**: Depends on Foundational only. No dependency on US2–US4. The largest phase —
  it builds the dispatcher, the printer-connector command surface, and telemetry sync that US2–US4 all
  extend rather than rebuild.
- **User Story 2 (Phase 4)**: Depends on Foundational + US1 (extends `ProductionDispatchMatcher.cs` T038,
  `GetQueueJobsQuery.cs` T061, `QueuePage.tsx` T072, `QueueJobRow.tsx` T071 — all US1 files, touched
  sequentially, not in parallel with US1's own work on them).
- **User Story 3 (Phase 5)**: Depends on Foundational + US1 + US2 (extends the same `ProductionDispatchMatcher.cs`/`ProductionDispatchQueryService.cs`/`ProductionDispatchBackgroundService.cs` chain US2 already extended — sequential on those three files across US1→US2→US3).
- **User Story 4 (Phase 6)**: Depends on Foundational + US1 (extends US1's dispatch-time `Dispatch()` call
  site and `RunLineEditor.tsx`/`ProductionRunDetailPage.tsx`); independent of US2/US3's own extensions
  (touches different lines of the shared dispatch files, but still sequence after US1 lands).
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Within Each User Story

- Tests before implementation where TDD is scoped (T028→T038, T030 depends on T028, T032 depends on
  T030, T033→T047 for US1; T077→T079 for US2; T088→T090 for US3). Test-after tasks are written
  alongside/after their implementation.
- Domain/Application before Infrastructure before Api before web, within each story.
- Backend endpoints before their Orval regeneration task, before the frontend components that consume them.

### Parallel Opportunities

- T002 (enums) unblocks T003/T006 in parallel; T013–T017 (Application interfaces/records) run in parallel
  — different files.
- T019 (repositories) and T020 (EF configs) both depend on the entities but not each other — parallel.
- Within US1: T028 (matcher TDD) and T029 (CRUD endpoint tests) are independent starting points; T040–T042
  (connector interface + two implementations) run T041/T042 in parallel once T040 lands; T051–T061's
  various command/query files are almost all `[P]` — different files, all depending only on Foundational +
  the specific prior task named.
- T076 (US1 frontend tests), T087 (US2), T095 (US3), T102 (US4) are each independently parallelizable
  within their own story once that story's implementation lands.
- T104 and T105 in parallel (backend vs. frontend tooling).

---

## Parallel Example: Foundational Domain layer

```bash
Task: "RunLineSpec.cs / JobFilamentRequirementSpec.cs in api/src/OpenFarm.Domain/Production/"
Task: "JobFilamentConsumption.cs entity in api/src/OpenFarm.Domain/Production/JobFilamentConsumption.cs"
```

## Parallel Example: User Story 1 command surface

```bash
Task: "GetProductionRunsQuery.cs / Handler.cs"
Task: "GetProductionRunDetailQuery.cs / Handler.cs"
Task: "CloseProductionRunCommand.cs / Handler.cs"
Task: "MarkPrintJobStartedCommand.cs / Handler.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (blocks everything).
3. Complete Phase 3: User Story 1 — the full queue-to-produced loop: explosion, eager job creation,
   auto-dispatch, live push + telemetry-confirmed start/finish/pause/resume/cancel, `AwaitingClear`
   gating, shortfall + requeue, run cancel without force-stopping a printing job.
4. **STOP and VALIDATE**: run quickstart.md Stories 1, 5, 6, 7, 8, 9.
5. Deploy/demo if ready — this is spec.md's own framing of this phase's core value ("the entire reason
   this phase exists").

### Incremental Delivery

1. Setup + Foundational → entities/persistence ready, no user-facing capability yet.
2. Add US1 → the full produce loop works end-to-end → demoable MVP.
3. Add US2 → operator control over order and machine pinning.
4. Add US3 → unattended backfill keeps idle printers busy.
5. Add US4 → per-line Snapshot/Follow-latest revision correctness.
6. Polish (Phase 7) → activity-log coverage, full lint/test pass, quickstart validation.

### Parallel Team Strategy

With multiple developers/agents: complete Setup + Foundational together, then US1 alone (it is the
critical path every later story extends — `ProductionDispatchMatcher.cs`, `ProductionDispatchQueryService.cs`,
`ProductionDispatchBackgroundService.cs`, `GetQueueJobsQuery.cs`, `QueuePage.tsx`/`QueueJobRow.tsx`, and
the dispatch-time `Dispatch()` call site are all touched again by US2/US3/US4). Once US1 lands, US2 and
US4 can proceed in parallel (different lines of the shared dispatch files, different frontend components);
US3 should follow US2 since it extends the same matcher/query-service/background-service chain US2 just
changed.

---

## Notes

- [P] tasks touch different files with no unmet dependency.
- [Story] labels map every Phase 3–6 task to spec.md's US1–US4 for traceability; FR-013a/017/018/024/024d/025
  (no dedicated spec.md user story) are scoped to US1 per this file's header note.
- TDD tasks (T004, T028, T030, T032, T033's disambiguation portion, T038, T077's extension, T079, T088,
  T090) must be written and observed failing before their paired implementation task lands.
- Commit after each task or logical group; run `dotnet format`/`bun run lint` before committing
  (CI-enforced).
- `ProductionDispatchMatcher.cs`, `ProductionDispatchQueryService.cs`, and
  `ProductionDispatchBackgroundService.cs` are each touched by three phases (US1 creates them; US2 adds
  the tie-break/Pinned branch; US3 adds the backfill branch) — sequential across those three phases, not
  parallel, despite living in different story sections.
- `GetQueueJobsQuery.cs`/`Handler.cs` is touched by US1 (T061, creates it) and US2 (T080, fills in real
  `isBlocked`) — sequential.
- `QueuePage.tsx` is touched by US1 (T072, plain list) and US2 (T085, converts to TanStack Virtual) —
  sequential.
- `RunLineEditor.tsx` is touched by US1 (T066, creates it) and US4 (T101, adds the revision-mode toggle) —
  sequential.
- `ProductionRunDetailPage.tsx` is touched by US1 (T070, creates it), US3 (T094, adds `BackfillLimitsPanel`),
  and US4 (T101, adds the revision badge) — sequential across those three.
- The dispatch-time `Dispatch()` call site inside `ProductionDispatchBackgroundService.cs` is touched by
  US1 (T044, creates the call) and US4 (T098, makes it Follow-latest-aware) — sequential.
- Avoid: vague tasks, same-file conflicts run in parallel, cross-story dependencies that break independence
  beyond the shared files explicitly called out above.
