# Feature Specification: Phase 7 — Production runs, jobs & print queue

**Feature Branch**: `014-production-run-queue`

**Created**: 2026-08-07

**Status**: Draft

**Input**: User description: "Phase 7"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Queue a part or assembly and watch it get produced end-to-end (Priority: P1)

As a print farm operator, I want to select something from the catalog — a single part or a whole
assembly — tell the system how many I need, and have it turn that into real print jobs that get sent to
a matching idle printer, so I get from "I need 20 of these" to plates actually printing without manually
working out plate counts or picking machines myself.

**Why this priority**: This is the entire reason this phase exists — Phase 6 (Assembly/BOM) describes
what a product is made of but has no consumer that turns that demand into instructions for a printer,
and Phase 1's live fleet view has nothing to do yet beyond watching status. This is the first "make it
do something" moment since Phase 1's own live-monitoring demo. It is independently valuable and
testable as a standalone MVP: an operator can queue one part and see a job dispatch and complete with
none of Stories 2–4 built yet.

**Independent Test**: Can be fully tested by queueing a single catalog part at a known quantity,
confirming the correct number of whole-plate jobs appear in the queue immediately, confirming one
dispatches automatically to a matching idle printer, and confirming the printer lands in AwaitingClear
once the job finishes.

**Acceptance Scenarios**:

1. **Given** a part with `units_per_print` = 4 exists in the catalog, **When** an operator queues 20
   units of it, **Then** the system creates `ceil(20÷4)` = 5 whole-plate print jobs, all visible in the
   queue immediately in Unassigned mode by default.
2. **Given** an assembly containing nested sub-assemblies and quantities is queued at quantity 3, **When**
   the run is created, **Then** the system explodes its BOM once, at creation time, using Phase 6's
   explosion function, and creates one PartRequirement per distinct (run line, part) pair, with quantity
   summed across every BOM path within that line.
3. **Given** a queued Unassigned job whose required nozzle diameter, material, and color are known,
   **When** a printer becomes Idle and its tags, nozzle diameter, and loaded material/color match,
   **Then** the dispatcher assigns and starts that job automatically, with no operator action needed to
   pick a printer.
4. **Given** a job reaches Completed, Failed, or Cancelled, **When** that status is reached, **Then** its
   printer moves to AwaitingClear rather than Idle, and the queue offers that printer no further job
   until the operator uses "Mark ready" (Phase 5).
5. **Given** a single part is queued directly rather than through a named run, **When** it appears in the
   queue, **Then** it is grouped under an unnamed run labeled from its contents (e.g. "Bracket ×20")
   rather than a blank or placeholder name, and opening it reaches the identical run detail view a named
   run has — lines, requirements, jobs, and progress all present.
6. **Given** the last job of a *named* run finishes and the operator hits "Mark ready," **When** that
   action is taken, **Then** the operator is asked to confirm the run is genuinely done before it flips
   to Complete; the identical action on an *unnamed* run's last job flips it to Complete with no
   confirmation step.
7. **Given** a part's file requires slicing (never pre-sliced) and it is dispatched to a job, **When**
   the job is sent to its target printer, **Then** Phase 4's in-app slicing capability runs on demand
   targeted at that job's exact destination printer, and the result is scoped to the job only — never
   saved as the part's stored file.
8. **Given** a connected Bambu printer, **When** a job is dispatched to it, **Then** the system pushes
   the matched sliced file directly and can auto-detect job start/finish from live status; **given** a
   printer whose brand has no live integration yet, **When** a job is dispatched to it, **Then** the
   operator retrieves the sliced file themselves (e.g. via USB/SD) and marks the job in progress
   manually.

---

### User Story 2 - Control what prints next and pin a job to a specific printer (Priority: P2)

As a print farm operator, I want to arrange the queue in the order I want things to print, and pin a
specific job to one specific machine when I have a reason to (an approved configuration, filament
already loaded), so the farm follows my judgment rather than an opaque algorithm.

**Why this priority**: Builds directly on Story 1's dispatch loop — once jobs exist and can dispatch,
controlling their order and machine choice is the next most common daily interaction on a real farm.
Independently testable and valuable without backfill (Story 3) or revision handling (Story 4) existing
yet.

**Independent Test**: Can be fully tested by reordering two unassigned queued jobs that both match one
idle printer and confirming the now-topmost one dispatches first, and separately by pinning a job to an
offline printer and confirming it stays queued and visibly flagged as blocked rather than being picked
up by a different matching printer.

**Acceptance Scenarios**:

1. **Given** two Unassigned jobs both match one idle printer, **When** the operator moves the lower one
   above the other, **Then** the printer is offered the now-topmost job next.
2. **Given** a job pinned to printer X, **When** printer X is offline, in maintenance, or AwaitingClear,
   **Then** the job stays queued, is visibly flagged as blocked on that specific machine, and is never
   auto-matched to a different printer.
3. **Given** several idle printers all qualify for an Unassigned job, **When** the dispatcher assigns it,
   **Then** it prefers the least-capable qualifying printer, leaving more capable machines free for jobs
   that need them.
4. **Given** the queue is arranged top-to-bottom with no due-date or priority field influencing order,
   **When** a printer becomes idle, **Then** the dispatcher takes the topmost job in queue order that
   printer is eligible for, skipping over ones it isn't, without ever reordering the queue itself.

---

### User Story 3 - Keep idle printers busy with an evergreen backfill line (Priority: P3)

As a print farm operator, I want to designate a part as an unlimited standing job that idle printers fall
back to whenever no real order needs them, so machines don't sit idle between real jobs and I don't have
to keep manually re-queuing the same evergreen part.

**Why this priority**: Enables genuinely unattended operation — a meaningful farm-scale capability — but
it is a refinement layered on top of Story 1's dispatch mechanics and isn't needed for the first
demoable produce loop.

**Independent Test**: Can be fully tested by queueing one finite job and one unlimited backfill line
that both match the same idle printer, confirming the finite job always wins dispatch, and confirming
the backfill line only ever produces one plate at a time, re-evaluated at the next bed clear.

**Acceptance Scenarios**:

1. **Given** an idle printer matches both a finite queued job and a backfill line, **When** the
   dispatcher runs, **Then** the finite job is assigned first, every time.
2. **Given** only a backfill line matches an idle printer, **When** its bed clears, **Then** exactly one
   new backfill plate is dispatched — never queued ahead of time as a batch.
3. **Given** a backfill line configured with the default cap of 1 concurrently-occupied printer, **When**
   two idle printers could both take it, **Then** only one is assigned; the other remains available for
   other work.
4. **Given** a backfill line with an operator-set cumulative-unit ceiling, **When** that ceiling is
   reached, **Then** the line stops dispatching further plates until the operator raises it or edits the
   line; a line with no ceiling set never stops on its own.
5. **Given** an unlimited line has no target quantity, **When** its progress is displayed, **Then** only
   a cumulative produced count is shown — never a completion percentage.

---

### User Story 4 - Choose whether a line prints exactly what was queued or always the latest file (Priority: P3)

As a print farm operator, I want to explicitly choose, per line, whether every plate prints the exact
file revision I queued or always resolves the part's current file at the moment each plate is sent to a
printer, so I can pin an approved revision for a customer order while still letting an evergreen part
I'm actively iterating on always print my latest fix.

**Why this priority**: Important for correctness and traceability once a part's file can change mid-run,
but it's a refinement on the dispatch mechanics from Stories 1–2 rather than a blocker to the first
working queue.

**Independent Test**: Can be fully tested by queueing a finite line under Snapshot, replacing the part's
file, and confirming every plate on that line — already created or created later — still prints the
originally-pinned revision; and separately by queueing under Follow-latest, replacing the file after one
plate has started, and confirming the running plate is unaffected while the next plate dispatched
afterward uses the new revision.

**Acceptance Scenarios**:

1. **Given** a line queued under Snapshot, **When** the part's file is replaced afterward, **Then**
   every plate on that line, whether already created or created later, still prints the revision pinned
   at queue time.
2. **Given** a line queued under Follow-latest, **When** the part's file is replaced while a plate is
   already printing, **Then** the running plate is unaffected, and the next plate dispatched afterward
   resolves and prints the new revision.
3. **Given** a finite line is queued with no explicit choice made, **When** it's created, **Then** it
   defaults to Snapshot; **given** an unlimited (backfill) line is queued with no explicit choice,
   **Then** it defaults to Follow-latest.
4. **Given** a Follow-latest finite line has had its file replaced partway through, **When** the run's
   jobs are reviewed, **Then** each job records the exact revision it printed, and the run is surfaced
   as spanning more than one revision rather than presenting a false single-revision picture.
5. **Given** a queued line's Snapshot/Follow-latest toggle, **When** the operator changes it after
   queueing, **Then** the change affects only the next plate dispatched — a currently-printing plate is
   never altered mid-print.
6. **Given** a new plate is about to dispatch under either mode, **When** its target printer and loaded
   spool are checked, **Then** the plate's actual nozzle diameter and material requirement are
   re-validated against them, and a mismatch (e.g. after a revision change) is flagged rather than
   silently sent to print.

---

### Edge Cases

- **What happens when a whole-plate job produces more units than the requirement needed** (e.g. a
  requirement of 5 filled by an 8-up plate)? The full 8 is recorded, the requirement is satisfied and
  capped at 100%, and the extra 3 are never tracked anywhere — OpenFarm's job is to produce, not to
  manage inventory.
- **What happens when a pinned job's target printer goes offline, into maintenance, or AwaitingClear?**
  The job never falls back to auto-matching; it waits, visibly flagged as blocked on that specific
  machine, for as long as that machine is unavailable.
- **What happens when two different run lines need the same part?** Each produces its own separate
  PartRequirement and its own job set; parts are never pooled across runs or lines (see Assumptions /
  Out of scope) — this is the accepted cost of per-line requirement grain.
- **What happens when the operator cancels or deletes a run that has jobs already printing?** Queued,
  undispatched jobs are cancelled outright. Anything physically printing keeps printing — OpenFarm never
  sends a remote stop command — and still records its eventual outcome and filament usage once it
  finishes, so yield and cost history stay complete.
- **What happens when a queued job's part doesn't match any registered printer's nozzle diameter or
  loaded material at all?** The run and job are still created — creating a run requires only that the
  referenced parts already exist, since Phase 4 guarantees every catalog part is fully valid by
  construction. The mismatch is flagged clearly in the queue rather than blocking creation, and the job
  simply waits until a match exists.
- **What happens when a job dispatches to a printer whose brand isn't live-integrated (non-Bambu)?** No
  automatic file push or status detection occurs; the operator retrieves the sliced file themselves and
  marks the job in progress manually.
- **What happens when a named run's last job finishes versus an unnamed run's last job finishing?** A
  named run asks the operator to confirm completion at "Mark ready"; an unnamed (quick-print) run skips
  that confirmation entirely.
- **What happens when a Follow-latest line's file changes mid-run?** Plates dispatched before and after
  the change legitimately print different revisions within the same requirement. This is surfaced
  honestly ("this run spans 2 revisions") rather than hidden, because every job records the exact
  revision it printed.
- **What happens when an unlimited backfill line and a real finite job both currently match the same
  idle printer?** The finite job always wins; backfill is only ever consulted once no finite job can be
  assigned to that printer right now.
- **What happens to a printer sitting in AwaitingClear?** It is offered no job of any kind — including
  backfill — until the operator performs Phase 5's "Mark ready" action, the sole path back to Idle.
- **What happens to a job's filament usage if the print fails partway through?** The filament actually
  consumed (e.g. roughly 70% of the estimate for a print that died at 70%) is still recorded, attributed
  per spool/profile, rather than recorded only for jobs that finish successfully.
- **What happens when an exploded Assembly's BOM includes non-printed components (Phase 6)?** No
  PartRequirement, job, or dispatch is ever created for them — they carry no stock and gate nothing, so
  this phase produces requirements only for the printed parts the explosion reports.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow an operator to create a ProductionRun by queueing one or more Parts
  and/or Assemblies from the catalog, each with a quantity, in a single action; everything selected and
  queued together in one action MUST become one run, and items queued in separate actions MUST become
  separate runs.
  - **FR-001a**: A run created without an explicit name MUST be usable identically to a named run in
    every respect (lines, requirements, jobs, progress, detail view) — it MUST differ only in how it is
    labeled in list views, from its contents rather than a blank or placeholder name.
- **FR-002**: System MUST support a RunLine that targets either a Part or an Assembly, never both, plus
  a requested quantity that is either finite or unlimited.
- **FR-003**: System MUST explode any Assembly RunLine's BOM, using Phase 6's explosion function, exactly
  once at run-creation time, and MUST snapshot the resulting exploded quantities — an edit to the
  Assembly's BOM after run creation MUST NOT alter an already-created run's requirements or jobs.
- **FR-004**: System MUST create exactly one PartRequirement per distinct (RunLine, Part) pair, with
  quantity equal to the sum of that part's quantities across every BOM path within that line (matching
  Phase 6's aggregation) — never one row per BOM path, and never one row per whole run.
  - **FR-004a**: System MUST NOT create a PartRequirement, job, or any dispatch record for a non-printed
    component reported by an Assembly's explosion — printed-part demand only is modeled in this phase.
- **FR-005**: System MUST create PrintJobs in whole plates per finite PartRequirement, computed as
  `ceil(required quantity ÷ units_per_print)` for that part.
  - **FR-005a**: System MUST add a `units_per_print` field to the Part entity (Phase 4), defaulting to
    1, auto-suggested from a sliced file's per-plate object count where parseable, and always
    operator-overridable.
  - **FR-005b**: For finite PartRequirements, System MUST create all of that requirement's plate-jobs
    eagerly at run creation, so the full job count is visible and browsable immediately.
- **FR-006**: System MUST support an unlimited quantity on a RunLine, representing a standing backfill
  line that keeps producing rather than completing once a target count is hit.
  - **FR-006a**: An unlimited RunLine's jobs MUST be created lazily, one plate at a time, only at the
    moment a printer is about to be given that job — never queued ahead of time as a batch.
  - **FR-006b**: The dispatcher MUST consider a backfill line for a given idle printer only after
    confirming no finite job in the ordinary queue can currently be assigned to that printer.
  - **FR-006c**: System MUST support a configurable cap, per backfill line, on how many printers it may
    occupy simultaneously, defaulting to 1.
  - **FR-006d**: System MUST support an optional cumulative-unit ceiling per backfill line; once
    reached, the line MUST stop dispatching further plates until the ceiling is raised or the line is
    edited.
  - **FR-006e**: System MUST display an unlimited line's progress as a cumulative produced count only —
    never a completion percentage.
- **FR-007**: System MUST support two assignment modes, chosen per job: Unassigned (the default) and
  Pinned to one specific printer.
  - **FR-007a**: An Unassigned job MUST be matched at dispatch time — never at job creation — against
    candidate idle printers on tags, nozzle diameter, currently loaded material and color, and
    availability.
  - **FR-007b**: Among multiple qualifying idle printers, the dispatcher MUST prefer the least-capable
    qualifying one, leaving more versatile machines free for jobs that need them.
  - **FR-007c**: A Pinned job MUST NOT ever be reassigned to a different printer, including when its
    target printer is offline, in maintenance, or AwaitingClear; in every such case it MUST remain
    queued and MUST be visibly flagged as blocked on that specific printer.
- **FR-008**: System MUST let the operator manually reorder the queue, and the dispatcher MUST always
  offer an idle printer the topmost job in queue order that printer is eligible for.
  - **FR-008a**: Queue order MUST NOT be influenced by a run's due date, priority, or any automatic
    scheduling signal in this phase — ordering is manual only.
- **FR-009**: System MUST let the operator explicitly choose, per RunLine, between Snapshot (the part's
  file revision is pinned at queue time) and Follow-latest (each plate resolves the part's current
  revision at its own dispatch time).
  - **FR-009a**: Finite lines MUST default to Snapshot; unlimited lines MUST default to Follow-latest;
    either mode MAY be chosen explicitly regardless of quantity type.
  - **FR-009b**: The toggle MUST remain editable after queueing, and any change MUST affect only plates
    dispatched afterward — never a plate already printing.
  - **FR-009c**: Every job MUST record the exact file revision it printed. System MUST add a
    content-hash-based revision identifier plus an incrementing revision counter to the Part entity,
    since Phase 4 makes file replacement destructive with nothing today distinguishing one revision from
    the next.
  - **FR-009d**: The file store MUST be content-addressed by revision hash, so multiple jobs referencing
    the same revision share the stored bytes rather than each holding an independent copy.
  - **FR-009e**: A run whose jobs collectively printed more than one revision under Follow-latest MUST be
    surfaced as spanning multiple revisions, rather than presenting a single, potentially false,
    revision.
- **FR-010**: Before each plate dispatches, System MUST re-validate that plate's required nozzle
  diameter and material against its target printer and currently loaded spool, and MUST flag a mismatch
  rather than silently sending it to print.
- **FR-011**: System MUST resolve a job's sliceable output at dispatch time based on the part's file
  origin: an already-sliced file MUST be sent as stored; a file requiring slicing MUST be run through
  Phase 4's reusable in-app slicing capability on demand, targeted at the job's actual destination
  printer, with the result scoped to that job only and never persisted as the part's stored file.
- **FR-012**: For a printer with live integration (Phase 1), System MUST push the resolved sliced file
  directly and MUST be able to auto-detect job start and finish using the same status-observation
  machinery Phase 1 built; for a printer without live integration, System MUST let the operator manually
  mark a job as started/in-progress after retrieving the file themselves.
- **FR-013**: System MUST record a job's filament consumption as a breakdown per spool/filament-profile —
  never a single total — reflecting that one job may draw from more than one spool (e.g. a multi-color
  AMS job).
  - **FR-013a**: Filament consumption MUST be recorded for a job that failed partway through,
    proportional to what was actually consumed before failure, not omitted because the job didn't finish
    successfully.
  - **FR-013b**: A job's filament requirement, like a part's (Phase 4), MUST be a collection of one or
    more (material, color) entries, defaulted from the part and operator-overridable per job.
- **FR-014**: System MUST drive every job through the status workflow queued → printing → paused →
  failed → completed → cancelled, allowing only valid transitions within that set.
  - **FR-014a**: Reaching completed, failed, or cancelled MUST move the job's printer to AwaitingClear
    (Phase 1's state machine), never directly to Idle.
  - **FR-014b**: A printer in AwaitingClear MUST NOT be offered any job — finite or backfill — until the
    operator performs Phase 5's "Mark ready" action, the sole path back to Idle.
- **FR-015**: System MUST record actual print duration per job (start and end), alongside its estimate,
  for any job whose printer has live status integration.
- **FR-016**: System MUST drive every ProductionRun through the status workflow active → complete →
  closed, plus a separate cancelled state reachable from active.
  - **FR-016a**: A run MUST reach complete only once every finite PartRequirement belonging to it is
    satisfied; an unlimited line never completes and never blocks its run from completing.
  - **FR-016b**: When the last job of a *named* run finishes, marking that printer ready MUST prompt the
    operator to confirm the run is genuinely complete before the run's status changes; an *unnamed* run
    MUST complete with no confirmation step.
  - **FR-016c**: Reaching complete MUST NOT be terminal — a completed run's requirements MUST remain
    revisable by later phases (Phase 8's outcome edits can reopen it). Reaching the separate closed state
    is what stops further reopening, and MUST be a distinct, explicit action available to the operator
    (see Assumptions for this phase's default trigger).
- **FR-017**: Cancelling or deleting a run MUST cancel every queued, undispatched job belonging to it,
  and MUST NOT send any command to a printer already printing one of its jobs; a job that was already
  printing when its run was cancelled MUST still be allowed to record its eventual outcome and filament
  usage.
- **FR-018**: Creating a ProductionRun MUST require nothing beyond the referenced Parts/Assemblies
  already existing in the catalog — System MUST NOT gate run or job creation on any printer, live
  connection, or spool being registered.
  - **FR-018a**: A queued job whose part's requirements don't currently match any registered printer or
    loaded spool MUST still be created and queued, flagged clearly, rather than blocking run creation.
- **FR-019**: System MUST support unlimited queue depth for the job queue while remaining fast to browse
  as it grows.
- **FR-020**: System MUST record run creation, run/job status transitions, queueing actions, dispatch
  assignment, cancellation, and Snapshot/Follow-latest toggle changes in the activity log, including who
  acted and when.
- **FR-021**: System MUST NOT pool identical part demand across separate runs or lines — each RunLine's
  requirement and jobs are produced independently even when another line elsewhere needs the identical
  part.
- **FR-022**: System MUST NOT track any post-print fulfillment state (assembled/boxed/shipped) or any
  stock-on-hand ledger for produced units in this phase.
- **FR-023**: System MUST NOT compute or expose per-unit outcome lines (good/defect/failed), yield, or
  blended progress percentages in this phase — those belong to Phase 8 (Progress, outcomes & yield
  tracking); this phase's job status is limited to the coarse workflow in FR-014.

### Key Entities *(include if feature involves data)*

- **ProductionRun**: The demand container. Key attributes: optional name, due date (display metadata
  only, never a scheduling input), status (active/complete/closed/cancelled), notes. A run created with
  no name is grouped and fully functional identically to a named one, differing only in list-view
  labeling (FR-001a). Owns one or more RunLines.
- **RunLine**: One demanded thing on a run — a Part or an Assembly (never both), plus a requested
  quantity that is either finite or unlimited (backfill). Carries the Snapshot/Follow-latest choice for
  everything produced under it.
- **PartRequirement**: The exploded leaf demand — one row per distinct (RunLine, Part) pair, with
  quantity summed across every BOM path that part appears on within that line. Snapshotted at run
  creation from Phase 6's explosion; never recomputed if the source Assembly's BOM changes afterward.
- **PrintJob**: The execution unit — flat and atomic, unlike the hierarchical demand above it. Carries
  one file revision, one target plate quantity, an assignment mode (Unassigned or Pinned) and, once
  dispatched, one printer; a status workflow (queued/printing/paused/failed/completed/cancelled); actual
  start/end duration; and a filament-consumption breakdown per spool/profile.
- **Backfill Line**: The business concept for an unlimited-quantity RunLine — not a separate entity type,
  but a distinct dispatch tier the dispatcher only consults when no finite job can be assigned to a given
  idle printer. Carries a per-printer occupancy cap (default 1) and an optional cumulative-unit ceiling.
- **Part (amended)**: Phase 4's entity gains two fields this phase needs: `units_per_print` (default 1,
  auto-suggested, operator-overridable) and a revision identifier (content hash + incrementing counter)
  so a job can record exactly which file state it printed.
- **File Revision**: The content-addressed identity of a Part's stored file at a point in time. Multiple
  jobs referencing the same revision share the underlying stored bytes rather than each holding an
  independent copy.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can go from selecting a part or assembly and a quantity to seeing the full
  whole-plate job count in the queue in a single action, with zero manual plate-count arithmetic on
  their part.
- **SC-002**: 100% of jobs that reach completed, failed, or cancelled leave their printer in
  AwaitingClear rather than Idle, verified across every status transition in the workflow.
- **SC-003**: For every scenario where an idle printer matches at least one queued Unassigned job,
  dispatch happens with no operator action required to select a printer.
- **SC-004**: 100% of Pinned jobs whose target printer becomes unavailable remain queued and visibly
  flagged as blocked, and 0 are ever silently reassigned to a different printer.
- **SC-005**: A queue of 1,000+ jobs remains browsable — loads and scrolls — without perceptible
  slowdown to the operator.
- **SC-006**: A finite queued job never waits behind a backfill line for longer than one plate's print
  duration on a printer that could serve both.
- **SC-007**: 100% of jobs record which exact file revision they printed, so a completed job's output
  remains traceable even after its part's file has since changed.
- **SC-008**: A run built from a multi-level nested assembly at a given quantity produces aggregated job
  counts matching a hand computation using Phase 6's explosion function, verified across several
  reference assemblies.
- **SC-009**: A named run requires operator confirmation before reaching Complete in 100% of cases; an
  unnamed run requires it in 0% of cases.
- **SC-010**: Filament usage is recorded for 100% of jobs that consumed any filament, including ones
  that failed partway through — never only for fully successful jobs.

## Assumptions

- **Closing a run to its terminal "closed" state is an explicit, operator-triggered action in this
  phase, not an automatic idle-period timeout.** The source material leaves this as an open question
  (manual, automatic after a configurable window, or both), but the reopening behavior "closed" is meant
  to guard against — a completed run reopening from a late-logged defect — doesn't exist until Phase 8
  ships its outcome-editing loop. Building only the manual trigger now, and leaving an automatic timeout
  as a later refinement once Phase 8 gives it something real to guard against, follows the project's own
  MVP-first principle rather than speculatively building a timer nobody can trigger yet.
- **A backfill line's optional cumulative-unit ceiling is off (unset) by default**, requiring the
  operator to opt in explicitly rather than silently capping production. The source material states the
  cap-per-printer default (1) but leaves the ceiling's on/off default open; opt-in is the safer default
  since an unexpected ceiling would look like a farm that stopped producing for no visible reason.
- **A ProductionRun's `priority` field is display metadata only, exactly like its `due date`** — neither
  field feeds the dispatcher or reorders the queue in this phase, since FR-008a is explicit that queue
  order is manual-only with no automatic scheduling signal. `priority` is carried on the entity so it can
  be surfaced to the operator (and potentially consumed by real optimization in Phase 12), not because
  this phase acts on it.
- **The "least-capable qualifying printer" tie-break (FR-007b) uses whatever coarse capability signal is
  already available** (e.g. a linked Machine Profile's declared fields) and falls back to a stable,
  arbitrary secondary rule when no such signal exists — it is explicitly a cheap heuristic, not real
  optimization, matching the source material's own framing that whole-queue optimization is Phase 12's
  job.
- **Non-Bambu (non-live-integrated) job status changes are manual UI actions** performed by the
  operator — mark started, mark completed/failed/cancelled — reusing the same status workflow (FR-014)
  a live-integrated job goes through automatically. No separate workflow exists for manually-operated
  printers, only a different trigger for the same transitions.
- **Queueing, reordering, pinning, and cancelling a run reuse the existing permission catalog (Phase 5)
  rather than introducing a new permission** — these are ordinary state-mutating operator actions, no
  different in kind from the CRUD and status actions already gated by existing roles.
- **A run's PartRequirements are snapshotted once at creation and never re-exploded**, even if the
  operator edits the source Assembly's BOM afterward (FR-003). This mirrors Phase 6's own explicit
  deferral ("snapshotting a BOM explosion at run creation is Phase 7's responsibility") and is what keeps
  an in-flight run's demand stable while the catalog keeps evolving underneath it.
- **`units_per_print` and the file-revision identifier are the only two amendments made to the Part
  entity in this phase** — both called out explicitly in the source material as small, additive fields
  rather than a reopening of Phase 4's part-creation rules (no manual-entry fallback, one file per part,
  etc.), which remain otherwise unchanged.
- **Cross-run part pooling and any stock-on-hand ledger remain permanently out of scope**, not merely
  deferred — the source material treats this as a standing project-level decision (see Out of scope in
  the feature plan), not something this phase should re-litigate or leave a door open for.
- **Job-level outcome granularity (good/defect/failed counts), yield, and blended progress percentages
  are entirely Phase 8's responsibility.** This phase's job status is the coarse workflow in FR-014 only;
  a job reaching "completed" here says nothing yet about how many of its units were actually good.
