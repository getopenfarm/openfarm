# Spec 014 implementation audit

Date: 2026-10-04. Workflow: repository-local `.claude/skills/speckit-implement/SKILL.md`.

## Finding

Spec 014 was **not fully implemented or verified** at the start of this audit. Implementation
checkboxes were checked, but production integration test tasks were still unchecked and had no
corresponding test files. Several functional requirements also had gaps. The requirement-writing
checklist passes all 16 items; that checklist is distinct from implementation acceptance.

## Implemented corrections

- Dispatch validates pinned jobs' nozzle/material requirements and observes queue ordering across
  matching pinned and unassigned jobs. It refuses unreachable live printers or printers with an
  active assignment. Queue rows flag unmatched jobs. Queue mutations wake idle printers; spool loads,
  printer profile edits, telemetry refreshes and backfill limit changes also trigger attempts.
- Dispatch signals retain deduplicated pending printer IDs even when the bounded channel fills.
  Startup reconciliation reverts interrupted assignments and wakes already-idle printers.
- Terminal jobs hold printers in AwaitingClear even if subsequent telemetry reports Idle. The operator
  must mark ready. Live status transitions come from telemetry; cancel intent resolves both FINISH and
  FAILED to Cancelled. Manual jobs update printer state on start and terminal outcomes.
- Named runs stay Active until the final printer's Mark ready action explicitly confirms completion.
  Unnamed finite runs complete automatically. Pure unlimited runs stay Active. Backfill remains available
  on mixed Complete runs until Closed/Cancelled, obeys pinning/caps, and counts Paused occupancy and
  immutable completed plate output.
- FollowLatest records the current revision before resolving dispatch bytes. Snapshot resolves retained
  files even when the Part's latest format changes. Manual retrieval has an authenticated job-file API.
  Legacy owner-keyed uploads retain aliases for old Snapshot references, and new run creation computes
  real SHA-256 hashes for legacy Parts without incrementing their revision number.
- Content-addressed uploads publish atomically, avoiding overwrite failures while a worker reads
  identical content. Part replacement explicitly tracks new EF children.
- Units per print is exposed and operator-editable. New sliced files suggest declared/distinct G-code
  object counts or uniform sliced-3MF plate object counts where parseable, otherwise default to one.
- Job filament requirements are editable before dispatch. Terminal usage persists multiple spool lines
  per material/color and decrements only additional grams, making repeated recording idempotent.
  The job detail page exposes manual lifecycle actions, file retrieval, requirement editing and usage.
- Bulk run cancellation logs each cancelled job; automatic and confirmed completion record the correct
  system/operator actor. Revision/backfill edits validate inputs and the owning route run.
- Queue browsing supports paginated virtualized rows and a separate backfill view. Generated clients
  have been regenerated from the running API contract.

## Validation evidence

- Full backend suite: 776 tests across API integration, Application, Domain, Infrastructure and slicer
  provisioning tests. API integration uses fresh PostgreSQL 18 Testcontainers; production scenarios
  include the real hosted dispatcher and Simulator connection/telemetry pipeline.
- Frontend suite: 178 tests across 55 files, including named completion confirmation, 1,001-job page
  navigation, job filament requirements and per-material spool splitting.
- Backend Release compilation, normal NuGet restore, dotnet formatting, frontend lint, TypeScript
  compilation and production build are checked. Existing frontend lint warnings and the bundle-size
  advisory remain non-blocking.
- Test fixture catalog selection is now portable across macOS/Linux. Repeated part upload tests cover
  immutable content reuse. The MQTT timeout fixture accepts either clean EOF or the macOS TLS reset
  when the client closes. Testcontainers' vulnerable transitive SSH.NET is overridden with 2026.0.0.

## Quickstart scenario coverage

| Scenario | Automated evidence |
|---|---|
| 1: create/explode/dispatch/finish/clear/named completion | ProductionRunCrudEndpointTests, RunCreationExplosionSnapshotTests, AwaitingClearGatingTests, LiveProductionWorkflowTests |
| 2: ordering, pinning, matching | QueueOrderingAndPinningTests, ProductionDispatchMatcherTests, revision dispatch ordering |
| 3: lazy backfill, finite priority, caps, ceiling | BackfillTierTests and matcher tests |
| 4: Snapshot/FollowLatest and revision labels | RevisionModeTests, ProductionContractTests |
| 5: zero printers | ProductionRunCrudEndpointTests |
| 6: partial/multiple-spool usage on failure | RunCancelAndFilamentTests |
| 7: run cancellation preserves printing | RunCancelAndFilamentTests, LiveProductionWorkflowTests |
| 8: controls and telemetry confirmation | RemotePrintJobControlEndpointTests, JobTelemetrySyncTests, LiveProductionWorkflowTests |
| 9: explicit one-time shortfall requeue | ShortfallAndRequeueTests |
| Audit actors and timestamps | ProductionRunQueueActivityLogTests |

## Remaining acceptance task

T107 is deliberately unchecked: the complete human/browser quickstart pass against the local stack
has not been performed. A temporary isolated PostgreSQL 18 + Kestrel API + Vite stack started successfully
and created a fresh 1,001-job run, a backfill run and a named run awaiting completion confirmation.
Computer-use could not open a browser (`iab` unavailable; inventory returned no browsers/apps and a
native-pipe startup error). The temporary preview was stopped and removed after that check.

Automated integration and UI coverage establish the implemented behavior; they do not substitute for
claiming every manual quickstart step and success criterion was observed in a real browser. Physical
Bambu FTPS/MQTT operation remains the feature plan's hardware-only verification, outside CI.
