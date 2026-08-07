# Implementation Plan: Phase 7 — Production runs, jobs & print queue

**Branch**: `014-production-run-queue` | **Date**: 2026-08-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/014-production-run-queue/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

The first feature that turns Phase 6's demand definitions (and Phase 4's catalog Parts) into actual
printer instructions. An operator queues a `ProductionRun` made of one or more `RunLine`s (a Part or an
Assembly, finite quantity or unlimited/backfill); every Assembly line's BOM is exploded once, via Phase
6's existing `IAssemblyGraphQueryService.ExplodeAsync`, and snapshotted into `PartRequirement` rows
(FR-003/FR-004) that never re-read the live BOM afterward. Finite requirements eagerly create whole-plate
`PrintJob`s (`ceil(quantity ÷ Part.UnitsPerPrint)` — a new field this phase adds to `Part`, FR-005a);
unlimited lines create jobs lazily, one at a time, only when a matching idle printer would otherwise sit
idle (FR-006, the backfill tier). A new `ProductionDispatchBackgroundService` — shaped identically to the
two existing background services this codebase already has (`PrinterConnectionBackgroundService`,
`PartProcessingBackgroundService`: bounded channel, narrow dispatcher interface, startup reconciliation of
orphaned in-flight work per the constitution's Durable Recovery guardrail) — matches `Unassigned` jobs to
idle printers on nozzle diameter and currently loaded material/color (printer tags don't exist yet in this
codebase, so that criterion is deferred, `research.md` R10), prefers `Pinned` jobs for their fixed target
printer only, and falls back to backfill only once no finite job can be assigned. Two genuinely new
integration points make this possible: `IPrinterConnector` gains its first outbound command
(`StartJobAsync` — an FTPS file push + MQTT print-start for Bambu, a no-op for manual-brand printers whose
operator marks a job started themselves, `research.md` R5), and `Part`'s file storage becomes
content-addressed by a new `RevisionHash`/`RevisionNumber` pair so a `Snapshot`-mode job's pinned revision
survives a later file replace that a `FollowLatest` line elsewhere in the farm might trigger (`research.md`
R6 — a real behavior change to Phase 4's previously-always-destructive `ReplaceFile`, not purely additive).
Full design reasoning for all of the above lives in `research.md`; entity/table shapes in `data-model.md`;
the wire contract in `contracts/production-run-queue-api.md`.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`api/`); TypeScript 5 (`strict: true`) / React 19 (`web/`) —
unchanged from prior phases.

**Primary Dependencies**: FastEndpoints, EF Core 10 + Npgsql, FluentValidation (backend, all pre-existing);
React Router v7, TanStack Query, **TanStack Virtual** (already an installed but so-far-unused `web/`
dependency — this feature is its first real consumer, per `CLAUDE.md`'s own note and constitution
Principle V), react-hook-form + zod, shadcn/ui, Orval-generated client (frontend). No new NuGet/npm
package for the queue/dispatch mechanics themselves. `IPrinterConnector.StartJobAsync`'s Bambu
implementation (`research.md` R5) needs an FTPS client — `System.Net` has no built-in FTPS support, so an
FTPS-capable library (e.g. `FluentFTP`, which already supports implicit/explicit FTPS) is a new
`Infrastructure`-only dependency, confined entirely to `BambuLanConnector`'s new method — no `Application`/
`Domain` reference, consistent with `IPrinterConnector` staying the swappable boundary (constitution
Principle I/IV).

**Storage**: PostgreSQL via `OpenFarmDbContext`. Two migrations (`data-model.md`): (1)
`AddPartRevisionAndUnitsPerPrint` — three additive columns on the existing `Parts` table
(`UnitsPerPrint`/`RevisionHash`/`RevisionNumber`) plus a one-time data-backfill step computing
`RevisionHash` for every existing row from its current stored file; (2) `AddProductionRunsAndJobs` — five
new tables (`ProductionRuns`, `RunLines`, `PartRequirements`, `PrintJobs`, `JobFilamentConsumptions`).
`IPartFileStorage`'s path convention changes from owner-GUID-keyed to content-hash-keyed
(`research.md` R6); `Part.ReplaceFile`'s current unconditional old-file deletion is removed.

**Testing**: xUnit + FluentAssertions, TDD, for `PrintJobStatusTransitions` (mirrors
`PrinterStatusTransitions`'s own TDD precedent — a deterministic state machine, constitution Principle II)
and `ProductionDispatchMatcher.SelectNextJob` (`data-model.md`'s `DispatchCandidate`-based pure matching
function — deterministic, injected-nothing, the dispatcher's actual selection logic isolated from its I/O
shell, `research.md` R4). xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql, test-first, for the
BOM-explosion-to-`PartRequirement` snapshot path (reuses `013-assembly-bom`'s own explosion fixtures) and
for `ProductionDispatchBackgroundService`'s reconciliation-on-startup behavior specifically (the
constitution guardrail this feature must satisfy, same TDD posture `PartProcessingBackgroundService`'s own
reconciliation pass was held to). Test-after for orchestration-only command/query handlers, EF
configuration, repositories, `BambuLanConnector.StartJobAsync`'s real FTPS/MQTT integration (manual
verification against physical hardware only, never a CI gate — same standing rule as every other
`BambuLanConnector` method), and every React component/page. Vitest + React Testing Library, test-after,
for `web/src/features/production-runs/`.

**Target Platform**: Linux server (API host) + browser SPA — unchanged; no new platform surface.

**Project Type**: Web application (existing `api/` + `web/` monorepo) — no new project/service.

**Performance Goals**: SC-005 — a queue of 1,000+ jobs remains browsable; met by (a) `QueueJobRow`'s flat,
indexed, `ORDER BY QueuePosition` query rather than tree assembly for the dispatch-order lens, and (b)
`web/`'s first real use of TanStack Virtual so the DOM only ever renders the visible slice regardless of
total row count. SC-006 — a finite job never waits behind backfill longer than one plate's duration; met
by R4's dispatch-order precedence (finite always checked before backfill) plus backfill's inherent
one-plate-at-a-time, re-evaluated-at-every-bed-clear shape (no batch of backfill jobs can ever queue ahead
of a newly-arrived finite one). No new perf-sensitive path beyond the dispatcher itself, which fires once
per printer-reaches-Idle event (bounded by fleet size, not queue size) rather than polling.

**Constraints**: FR-003/`research.md` R8 — a `PartRequirement`, once created, MUST NOT be capable of
re-reading the source BOM; enforced by the entity carrying no reference to `Assembly`/`BomLine` at all
beyond its owning `RunLineId`. FR-009b/R6 — a `RevisionMode` change or a Follow-latest file replacement
MUST NEVER alter a currently-printing job's own recorded revision; `Dispatch`'s revision-updating path is
the only place a job's `FileRevisionHash` can change after creation, and it is never called again once a
job leaves `Queued`. FR-014a/b — every job-closing transition MUST move its printer to `AwaitingClear`, and
an `AwaitingClear` printer MUST be invisible to dispatch (both finite and backfill) until "Mark ready" —
enforced by the Application-layer command handler for every one of `PrintJob`'s terminal transitions
calling `printer.TransitionTo(AwaitingClear)` in the same transaction, and by the dispatcher's own
candidate query filtering on `Printer.Status == Idle`. Constitution's Durable Recovery guardrail —
`ProductionDispatchBackgroundService` MUST reconcile any job left in a transient dispatch-in-flight state
at startup and MUST re-queue a dispatch attempt for every already-`Idle` printer on boot (`research.md`
R4), exactly as `PartProcessingBackgroundService`/`PrinterConnectionBackgroundService` already do for
their own resources.

**Scale/Scope**: Single self-hosted organization scope, unchanged. SC-005's 1,000+ job queue is this
feature's own upper bound for the flat dispatch-order lens; the dispatcher's per-signal matching query
(`research.md` R4) is expected to stay cheap at this scale via the `(OrganizationId, Status,
AssignmentMode, QueuePosition)` index — no materialized per-printer candidate view is built now (nothing
in this feature's realistic scale — one farm's own queue, not a shared marketplace — shows a need for one
yet, consistent with constitution Principle IV).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture, Enforced by the Compiler and CI** — PASS. `ProductionRun`, `RunLine`,
  `PartRequirement`, `PrintJob`, `JobFilamentConsumption`, `PrintJobStatusTransitions`,
  `ProductionDispatchMatcher`, and every domain exception live in `Domain/Production/` —
  framework-agnostic, no FastEndpoints/EF references, no dependency on `Infrastructure`.
  `Application/Production/` defines repository/dispatcher contracts (`IProductionRunRepository`,
  `IPrintJobRepository`, `IProductionDispatcher`) and every command/query handler;
  `Infrastructure/Production/` implements all three, including `ProductionDispatchBackgroundService` and
  the raw dispatch-matching query, following the exact `Infrastructure`-implements-`Application`-contracts
  shape `013-assembly-bom` and `007-part-library` both already establish. `IPrinterConnector.StartJobAsync`
  is defined in `Application/Printers/` (extending the existing interface, not a new one) and implemented
  in `Infrastructure/Printers/Bambu` and `Infrastructure/Printers/Simulator` — the FTPS dependency
  (`FluentFTP`) never crosses into `Application`/`Domain`. `Api` endpoints stay thin FastEndpoints REPR
  adapters.
- **II. Test Discipline Where It Pays, Not Uniformly** — PASS. TDD is scoped to exactly the deterministic,
  well-specified pieces the constitution names: `PrintJobStatusTransitions` (a Domain state machine, same
  category as `PrinterStatus`'s own TDD'd transitions) and `ProductionDispatchMatcher.SelectNextJob` (a
  pure selection function, isolated from I/O specifically so it's unit-testable). The BOM-explosion-to-
  `PartRequirement` snapshot path is TDD'd via `WebApplicationFactory` + Testcontainers (the closest
  available red-green equivalent, reusing `013-assembly-bom`'s own fixture assemblies) since it exercises
  real Postgres-backed explosion, not a pure C# algorithm. `ProductionDispatchBackgroundService`'s startup
  reconciliation pass is TDD'd for the same reason `PartProcessingBackgroundService`'s own reconciliation
  was — it's the literal mechanism the constitution's Durable Recovery guardrail exists to prevent
  regressing. Test-after for orchestration-only handlers, EF configuration, repositories,
  `BambuLanConnector.StartJobAsync`'s real FTPS/MQTT body (manual verification against hardware only,
  never CI — unchanged standing rule for this connector), and every React component/page.
- **III. Consistent User Experience Across the Fleet** — PASS. The queue's real-time surface (job status
  changes as the dispatcher assigns/completes jobs) MUST push through the existing `FarmHub` →
  TanStack Query cache path, exactly like `PrinterStatusChanged`/`PartProcessingJobStatusChanged` already
  do — a new `IProductionEventPublisher`/`FarmHubProductionEventPublisher` pair follows the identical
  established shape (`PrinterEventPublisher`/`PartEventPublisher`), not a parallel polling mechanism. Forms
  use react-hook-form + zod; primitives from `src/components/ui/`; the run-creation flow follows the
  Page-first convention (`docs/UI_CONVENTIONS.md`) — queueing a run is exactly the kind of
  multi-field, multi-line flow the convention reserves for a routed page, not a modal. Domain state-machine
  semantics (`PrintJobStatus`, `Printer.AwaitingClear`) are presented identically everywhere they surface —
  the queue list, the run detail view, and the printer detail page all read the same underlying status,
  never a page-local derived label. Every domain exception gets one `DomainExceptionHandler` mapping.
- **IV. Build for Expansion, Not Speculation** — PASS. `IPrinterConnector` is extended (`StartJobAsync`)
  rather than duplicated into a parallel dispatch-specific interface, so a future brand (Phase 15) or the
  local-agent relay (Phase 23) implements one enlarged contract. `ProductionDispatchBackgroundService`
  follows exactly the shape two prior background services already established, rather than inventing a
  third pattern. Conversely, this phase deliberately does **not** build: per-unit outcome tracking
  (good/defect/failed — FR-023, Phase 8's job), any stock/fulfillment ledger (FR-022, permanently out of
  scope), cross-run part pooling (FR-021, permanently out of scope), or a materialized dispatch-candidate
  view (no evidence of need yet at this feature's real scale). `organization_id`-scoping is applied to
  every new table from its first migration, via the existing Finbuckle `IsMultiTenant()` pattern.
- **V. Performance and Real-Time Responsiveness by Construction** — PASS. The queue is this codebase's
  first real `TanStack Virtual` consumer (already an installed, unused dependency — `CLAUDE.md` explicitly
  earmarks this feature for it), satisfying the "unbounded job queue" requirement by construction rather
  than as a later optimization. BOM explosion for Assembly `RunLine`s reuses Phase 6's existing
  recursive-CTE-backed `ExplodeAsync` rather than a second traversal implementation. Dispatch is
  event-triggered (a printer reaching Idle) exactly once per event, never polling. A third
  `BackgroundService` (`ProductionDispatchBackgroundService`) is architected for standalone-worker-host
  extraction the same way the existing two already are — no direct dependency on being co-located with the
  API host beyond what `PrinterConnectionBackgroundService` already assumes.
- **Durable Recovery of In-Flight Background Work** — PASS, by direct design. `PrintJob`'s
  dispatch-in-flight window (`DispatchedAtUtc` set, not yet confirmed `Printing`) is exactly the kind of
  non-terminal state the guardrail targets; `ProductionDispatchBackgroundService.ReconcileOrphanedWorkAsync`
  runs before the consumer loop starts, mirroring `PartProcessingBackgroundService`'s own
  `ReconcileOrphanedWorkAsync` byte-for-byte in shape. Every operation that can leave a `PrintJob`
  non-terminal (the dispatch attempt itself, `StartJobAsync`'s FTPS/MQTT call) is wrapped so any
  unmodeled exception routes to a terminal transition (`MarkFailed`) rather than a narrow
  `catch`-and-silently-continue — the same "catch every non-cancellation exception around the whole
  operation" rule the guardrail states explicitly. Genuine host-shutdown cancellation
  (`OperationCanceledException` from the hosted service's own stopping token) is left for the next
  startup's reconciliation pass to clean up, never mislabeled as a per-job failure.
- **Security & Multi-Tenancy Isolation** — PASS. All five new tables use `IsMultiTenant()` exclusively, no
  hand-rolled tenant filter — including the dispatcher's own candidate query, which is always scoped to
  one organization's printer/job set (dispatch never runs cross-tenant by construction, since it's
  triggered per-printer and every printer already carries its own `OrganizationId`). Every mutating
  endpoint carries a real `[Permission("production-runs:...")]`/`[Permission("print-jobs:...")]` attribute
  following the established naming convention. Every mutating endpoint's Request DTO implements
  `IActivityLoggable` (FR-020); dispatcher-driven assignment, which never runs inside an HTTP request, logs
  directly via `IActivityLogWriter` with `ActorType.System`, the same pattern
  `PrinterConnectionBackgroundService`/`PartProcessingBackgroundService` already use for their own
  system-driven writes. Auth is the existing real JWT flow throughout.

No violations requiring justification — **Complexity Tracking below records genuinely new infrastructure
this feature introduces** (the third background service, the outbound printer command, content-addressed
storage, the new FTPS dependency) for visibility, not as deviations, the same framing `013-assembly-bom`
and `007-part-library` both used for their own new pieces.

**Post-Phase-1-design re-check** (per `research.md`/`data-model.md`/`contracts/`): confirms all of the
above with one addition worth flagging explicitly — `research.md` R6's removal of `Part.ReplaceFile`'s
previously-unconditional old-file deletion is a genuine behavior change to already-shipped Phase 4 code,
not new-feature-only work; `tasks.md` MUST include updating `ReplacePartFileCommandHandler` (or wherever
the `IPartFileStorage.DeleteAsync` call on the pre-replace path currently lives) as an explicit task, not
something incidentally touched while building the new tables. No other new violation identified.

## Project Structure

### Documentation (this feature)

```text
specs/014-production-run-queue/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
│   └── production-run-queue-api.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
api/src/OpenFarm.Domain/Production/
  ProductionRun.cs / RunLine.cs / PartRequirement.cs / PrintJob.cs / JobFilamentConsumption.cs
  ProductionRunStatus.cs / RunLineTargetKind.cs / RevisionMode.cs / PrintJobStatus.cs / AssignmentMode.cs
  PrintJobStatusTransitions.cs          # TDD target — mirrors PrinterStatusTransitions
  ProductionDispatchMatcher.cs           # TDD target — pure SelectNextJob(candidate list) (research.md R4)
  RunLineSpec.cs / JobFilamentRequirementSpec.cs   # factory-input records
  ProductionRunNotFoundException.cs / RunLineNotFoundException.cs / PrintJobNotFoundException.cs
  ProductionRunMustHaveAtLeastOneLineException.cs / RunLineTargetNotFoundException.cs
  InvalidPrintJobStatusTransitionException.cs / InvalidRunCompletionException.cs
  PinnedJobRequiresPrinterException.cs / PrintJobAlreadyDispatchedException.cs
  ProductionRunConcurrencyConflictException.cs / FilamentConsumptionSumMismatchException.cs

api/src/OpenFarm.Domain/Parts/
  Part.cs                                # amended: UnitsPerPrint, RevisionHash, RevisionNumber (data-model.md)

api/src/OpenFarm.Application/Production/
  IProductionRunRepository.cs / IPartRequirementRepository.cs / IPrintJobRepository.cs
  IProductionDispatcher.cs               # RequestDispatchAttempt(printerId) — research.md R4
  IProductionEventPublisher.cs           # mirrors IPrinterEventPublisher's shape
  CreateProductionRunCommand.cs / Handler / Validator    # calls IAssemblyGraphQueryService.ExplodeAsync (research.md R1)
  CloseProductionRunCommand.cs / Handler
  CancelProductionRunCommand.cs / Handler
  SetRunLineRevisionModeCommand.cs / Handler
  SetRunLineBackfillLimitsCommand.cs / Handler
  ReorderPrintJobCommand.cs / Handler / Validator         # fractional QueuePosition math (research.md R3)
  PinPrintJobCommand.cs / UnpinPrintJobCommand.cs / Handler
  MarkPrintJobStartedCommand.cs / PausePrintJobCommand.cs / ResumePrintJobCommand.cs
  CompletePrintJobCommand.cs / FailPrintJobCommand.cs / Handler   # filament consumption body (research.md R7)
  CancelPrintJobCommand.cs / Handler
  GetProductionRunsQuery.cs / GetProductionRunDetailQuery.cs / Handler
  GetQueueJobsQuery.cs / Handler                          # QueueJobRow (data-model.md)

api/src/OpenFarm.Application/Printers/
  IPrinterConnector.cs                   # amended: + StartJobAsync (research.md R5)

api/src/OpenFarm.Application/Parts/
  IPartFileStorage.cs                    # amended: content-addressed save path (research.md R6)

api/src/OpenFarm.Infrastructure/Production/
  ProductionRunRepository.cs / PartRequirementRepository.cs / PrintJobRepository.cs
  ProductionDispatchBackgroundService.cs  # third BackgroundService — bounded Channel<Guid>, reconciliation
                                            # on startup (Durable Recovery guardrail), calls
                                            # ProductionDispatchMatcher + IPrinterConnector.StartJobAsync
  ProductionDispatchQueryService.cs        # builds DispatchCandidate lists per printer (research.md R4)
  Persistence/Configurations/ProductionRunConfiguration.cs / RunLineConfiguration.cs /
    PartRequirementConfiguration.cs / PrintJobConfiguration.cs / JobFilamentConsumptionConfiguration.cs
  Persistence/Migrations/<timestamp>_AddPartRevisionAndUnitsPerPrint.cs
  Persistence/Migrations/<timestamp>_AddProductionRunsAndJobs.cs

api/src/OpenFarm.Infrastructure/Printers/Bambu/
  BambuLanConnector.cs                   # amended: StartJobAsync — FluentFTP upload (port 990) + MQTT start
api/src/OpenFarm.Infrastructure/Printers/Simulator/
  SimulatorPrinterConnector.cs           # amended: StartJobAsync — begins fake Printing->Finish telemetry

api/src/OpenFarm.Infrastructure/Parts/
  LocalDiskPartFileStorage.cs            # amended: content-addressed path, no eager delete on replace
  ReplacePartFileCommandHandler.cs (or equivalent)   # amended: stop calling DeleteAsync on old path

api/src/OpenFarm.Api/
  Endpoints/ProductionRuns/
    CreateProductionRunEndpoint.cs / ListProductionRunsEndpoint.cs / GetProductionRunEndpoint.cs
    CloseProductionRunEndpoint.cs / CancelProductionRunEndpoint.cs
    SetRunLineRevisionModeEndpoint.cs / SetRunLineBackfillLimitsEndpoint.cs
  Endpoints/PrintJobs/
    ListQueueJobsEndpoint.cs / GetPrintJobEndpoint.cs / ReorderPrintJobEndpoint.cs
    PinPrintJobEndpoint.cs / UnpinPrintJobEndpoint.cs / MarkPrintJobStartedEndpoint.cs
    PausePrintJobEndpoint.cs / ResumePrintJobEndpoint.cs
    CompletePrintJobEndpoint.cs / FailPrintJobEndpoint.cs / CancelPrintJobEndpoint.cs
  Hubs/FarmHubProductionEventPublisher.cs   # implements IProductionEventPublisher
  ExceptionHandling/DomainExceptionHandler.cs   # + this feature's exception -> status mappings

api/tests/
  OpenFarm.Domain.Tests/Production/PrintJobStatusTransitionsTests.cs         # TDD
  OpenFarm.Domain.Tests/Production/ProductionDispatchMatcherTests.cs         # TDD
  OpenFarm.Application.Tests/Production/*CommandHandlerTests.cs             # test-after, orchestration only
  OpenFarm.Api.IntegrationTests/Production/
    RunCreationExplosionSnapshotTests.cs      # TDD against real Testcontainers Postgres (reuses 013's fixtures)
    DispatchReconciliationTests.cs             # TDD — Durable Recovery guardrail, orphaned-job recovery on boot
    QueueOrderingAndPinningTests.cs / BackfillTierTests.cs / RevisionModeTests.cs / AwaitingClearGatingTests.cs   # test-after

web/src/features/production-runs/
  ProductionRunsPage.tsx                 # list (DataTable + pagination)
  ProductionRunDetailPage.tsx             # lines/requirements/jobs, expand-in-place (mirrors AssemblyDetailPage's R7 pattern)
  QueuePage.tsx                           # the flat, TanStack-Virtual-backed dispatch-order queue (constitution Principle V)
  NewProductionRunPage.tsx                 # Page-first (docs/UI_CONVENTIONS.md), react-hook-form + zod
  components/RunLineEditor.tsx             # target picker (Part/Assembly), quantity, revision mode, assignment mode
  components/QueueJobRow.tsx               # one virtualized row — status, isBlocked flag, pin/reorder actions
  components/BackfillLimitsPanel.tsx       # printer cap / cumulative ceiling editor
  *.test.tsx                                # test-after, Vitest + RTL
```

**Structure Decision**: Existing four-project Clean Architecture backend + single-SPA frontend, extended in
place — no new top-level project, following the same "extend, don't parallel-build" precedent every prior
phase has used. `Production/` is a new feature folder at every backend layer (there is no existing
"run/job-shaped" code to extend), while `Printers`/`Parts` receive targeted, additive amendments to
existing files (`IPrinterConnector`, `IPartFileStorage`, `Part`) rather than new parallel files, since the
changes there are extensions of already-established contracts, not new concepts.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. The pieces below are recorded for visibility, not because any is a
deviation — the same framing `013-assembly-bom` and `007-part-library` both used for their own new pieces:

| Decision | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| A third `BackgroundService` (`ProductionDispatchBackgroundService`) issuing outbound printer commands — this codebase's first background service that *writes* to a connector rather than only reading its telemetry | Matching queued jobs to idle printers and starting prints has to run continuously and react to printer-idle events across the whole fleet; nothing in the existing request/response API surface has anywhere to hook this, and it must survive process restarts per the constitution's Durable Recovery guardrail | Doing dispatch synchronously inside the "Mark ready" HTTP request (i.e., matching happens as a side effect of the endpoint call) was considered and rejected — it would conflate an operator's UI action with the dispatcher's job, couple the endpoint's response time to a printer's FTPS upload, and give no place for the required startup reconciliation pass to live |
| A new outbound `IPrinterConnector.StartJobAsync` method + a new `FluentFTP` dependency scoped to `Infrastructure.Printers.Bambu` | Bambu's file-push mechanism is FTPS on port 990 (`docs/FEATURE_PLAN.md`'s own Phase 1 note, confirmed as this phase's responsibility); no existing dependency in this codebase speaks FTPS | Reusing `BambuLanConnector`'s existing MQTT connection alone was considered and rejected — MQTT carries commands/telemetry, not bulk file transfer; the protocol genuinely needs a second channel, matching how the real Bambu ecosystem (and the vendored `third_party/printers/bambulab/` reference) documents it |
| Content-addressed `Part` file storage + a non-destructive `ReplaceFile` (`RevisionHash`/`RevisionNumber`, `research.md` R6) — a real behavior change to already-shipped Phase 4 code | FR-009c/d requires every job to record which exact revision it printed, and a Snapshot-mode job's pinned revision must survive a later file replacement triggered by an unrelated Follow-latest line — impossible under the current unconditional-delete-on-replace behavior | Keeping replace destructive and instead copying the file's bytes onto every job at creation time was considered and rejected — it would duplicate potentially large files (3MF/G-code) once per job rather than once per distinct revision, exactly the "N jobs against one revision costing N× the bytes" problem `docs/FEATURE_PLAN.md`'s own Phase 7 section calls out as the reason for content-addressing in the first place |
| A fractional-index `QueuePosition` (`double precision`) rather than integer positions or a linked list | FR-008 requires manual reordering to be a normal, frequent action, and SC-005 requires the queue to stay fast as it grows into the thousands — a fractional index makes reorder a single-row `UPDATE` independent of queue size | Integer positions requiring a renumber of every row after the moved one (or a full-table shift) were rejected as an O(n) write on every drag-and-drop; a linked list was rejected because every ordered read becomes a traversal, which fights TanStack Virtual's need for a directly-sliceable, sorted collection |
