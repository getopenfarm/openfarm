# Quickstart & Validation: Production runs, jobs & print queue

Runnable scenarios that prove the feature end-to-end. References `contracts/production-run-queue-api.md`
and `data-model.md` rather than restating them. Commands assume the repo conventions in `CLAUDE.md`.

## Prerequisites

- .NET 10 SDK, Bun, Docker (Testcontainers / local stack); `ConnectionStrings__Default` set.
- At least one catalog `Part` with a known `unitsPerPrint` (Phase 4) and, for Stories 1–4, a `Printer`
  registered with a linked `MachineProfile` (nozzle diameter resolvable) — use the `SimulatorPrinterConnector`
  (`Brand: "System"`, `ModelCode: "Simulation"`) so no physical hardware is required.
- One `Assembly` (Phase 6) referencing at least two Parts, for Story 1's nested-demand scenario.

## Story 1 — Queue a part or assembly and watch it get produced end-to-end

1. `POST /api/v1/production-runs` with a single Part line, `{ "targetKind": "Part", "targetId": "<Bracket>",
   "quantity": 20 }`, where Bracket's `unitsPerPrint = 4` → `201`. Confirm the response's requirement for
   Bracket shows 5 jobs (`ceil(20÷4)`), all `status: "Queued"`, `assignmentMode: "Unassigned"` (Acceptance
   Scenario 1).
2. `POST /api/v1/production-runs` with an Assembly line at quantity 3, where the assembly's BOM (Phase 6)
   nests a sub-assembly → `201`; confirm one `PartRequirement` per distinct part with quantity matching a
   hand-computed explosion at quantity 3 (Acceptance Scenario 2).
3. With a `SimulatorPrinterConnector`-backed printer already `Idle` and its linked `MachineProfile`'s
   nozzle diameter matching Bracket's, wait for (or trigger, in a test, via) a dispatch signal — confirm
   exactly one of the 5 queued jobs moves to `status: "Printing"`, `dispatchedPrinterId` set, with no
   operator action taken (Acceptance Scenario 3, `research.md` R4).
4. Let the simulator's own telemetry sequence run to completion (`IJobTelemetrySync` observes the `FINISH`
   gcode state and calls `MarkCompleted()` — `research.md` R12; no `/complete` call needed for a
   live-integrated job) → job `status: "Completed"`; `GET /api/v1/printers/{id}` shows `status:
   "AwaitingClear"`, never `"Idle"` (Acceptance Scenario 4, FR-014a). Confirm a second dispatch attempt
   against this printer assigns nothing until `POST /api/v1/printers/{id}/mark-ready` is called (FR-014b).
5. `GET /api/v1/production-runs` for the run created in step 1 (no `name` supplied) → `displayLabel:
   "Bracket ×20"`, not blank (Acceptance Scenario 5). `GET /api/v1/production-runs/{id}` returns the same
   full detail shape a named run would.
6. Create a **named** run with a single small line, complete its one job, call
   `POST /api/v1/production-runs/{id}/close` before completion → `409` (must be `Complete` first);
   complete the job, then confirm the frontend gate prompts confirmation before calling `MarkComplete`
   (Acceptance Scenario 6) — an equivalent unnamed run's last job completing requires no such prompt.

## Story 2 — Control what prints next and pin a job to a specific printer

1. Two `Unassigned` jobs both match one idle printer (same nozzle diameter/material). `POST
   /api/v1/print-jobs/{lowerJobId}/reorder` `{ "afterJobId": null }` (move to top) → confirm the next
   dispatch to that printer picks the now-topmost job (Acceptance Scenario 1).
2. `POST /api/v1/print-jobs/{id}/pin` `{ "printerId": "<offlinePrinter>" }` → `200`. Trigger a dispatch
   attempt against a *different*, matching, idle printer → the pinned job is never touched;
   `GET /api/v1/print-jobs?view=queue` shows `isBlocked: true` for it (Acceptance Scenario 2, FR-007c).
3. With two idle printers both matching an `Unassigned` job — one with more AMS trays configured than the
   other — confirm the dispatcher assigns to the printer with fewer trays (`research.md` R11's tie-break)
   (Acceptance Scenario 3).

## Story 3 — Keep idle printers busy with an evergreen backfill line

1. `POST /api/v1/production-runs` with one finite line (`quantity: 5`) and, in a second run, one
   `quantity: null` backfill line targeting the same Part/printer profile, `backfillPrinterCap: 1`.
2. Trigger dispatch against a matching idle printer with both lines' demand pending → confirm the finite
   job is assigned, never the backfill line, every time this is repeated (Acceptance Scenario 1, FR-006b).
3. Exhaust the finite line's jobs, then trigger dispatch again → confirm exactly one backfill `PrintJob`
   is lazily created and dispatched (`research.md` R2), never a batch (Acceptance Scenario 2).
4. With `backfillPrinterCap: 1` and two idle matching printers, trigger dispatch against both → confirm
   only one receives a backfill job; the other remains available (Acceptance Scenario 3).
5. Set `backfillCumulativeUnitCeiling: 8` (via `PUT .../backfill-limits`) on a line whose Part has
   `unitsPerPrint: 4` — after 2 completed backfill jobs (8 units), confirm a further dispatch attempt
   creates no new job for that line (Acceptance Scenario 4).
6. `GET /api/v1/production-runs/{id}` for the backfill run → confirm no completion percentage is present,
   only a cumulative count (Acceptance Scenario 5, FR-006e).

## Story 4 — Choose whether a line prints exactly what was queued or always the latest file

1. Queue a finite line under the default (`Snapshot`, since it's finite — FR-009a). Note the created
   jobs' `fileRevisionHash`. `POST /api/v1/parts/{id}/replace-file` (Phase 4) with a new file. Confirm
   `GET /api/v1/print-jobs/{id}` for every job on that line — already created, none re-created — still
   shows the *original* `fileRevisionHash` (Acceptance Scenario 1).
2. Queue a second line under `FollowLatest`. Dispatch its first plate (now `Printing`). Replace the Part's
   file again. Confirm the already-`Printing` job's `fileRevisionHash` is unchanged, while the *next*
   plate dispatched afterward (backfill or a subsequent eager job) resolves and records the *new* hash
   (Acceptance Scenario 2, `research.md` R6).
3. Confirm a finite line queued with no explicit `revisionMode` defaults to `Snapshot`, and an unlimited
   line defaults to `FollowLatest` (Acceptance Scenario 3, FR-009a).
4. For the `FollowLatest` line from step 2, `GET /api/v1/production-runs/{id}` → the run/line is surfaced
   as spanning more than one revision (Acceptance Scenario 4, FR-009e).
5. `PUT /api/v1/production-runs/{runId}/lines/{lineId}/revision-mode` on an in-progress line → confirm a
   currently-`Printing` job's own `fileRevisionHash` never changes as a result (Acceptance Scenario 5).
6. Dispatch a plate whose Part's nozzle diameter no longer matches its target printer (simulate by editing
   the printer's linked `MachineProfile` after the job was created) → confirm the job is flagged rather
   than silently dispatched (Acceptance Scenario 6, FR-010).

## Story 5 — Run creation and queueing require no printer, connection, or spool

1. On a fresh organization with **0 printers registered**, `POST /api/v1/production-runs` with a valid
   Part line → `201`, jobs created, all `status: "Queued"`, none flagged (nothing to mismatch against yet)
   (FR-018).
2. Register a printer whose linked `MachineProfile`'s nozzle diameter does **not** match the queued Part →
   `GET /api/v1/print-jobs?view=queue` shows the job still `Queued`, flagged as a mismatch, never blocking
   the run's own existence (FR-018a).

## Story 6 — Filament consumption is recorded even when a job fails

1. Dispatch a job on a **manual-brand** (non-live) printer whose Part has one filament requirement
   (PETG/black). `POST /api/v1/print-jobs/{id}/fail`
   `{ "filamentConsumption": [{ "materialType": "PETG", "detectedColorHex": "#000000", "spoolId": "<spool>",
   "gramsConsumed": 30 }] }` (the operator reporting a print that died partway through — manual-brand jobs
   have no telemetry, so this endpoint applies the transition directly) → `200`, `status: "Failed"`,
   the matched `Spool`'s `RemainingWeightGrams` decremented by 30 (Edge Cases, FR-013a) — not by the full
   estimate, and not skipped because the job didn't succeed.

## Story 7 — Cancelling a *run* never force-stops a printing job as a side effect

1. Create a run with 3 jobs; dispatch one (now `Printing`), leave two `Queued`.
2. `POST /api/v1/production-runs/{id}/cancel` → the two `Queued` jobs become `Cancelled`; the `Printing`
   job is untouched (still `Printing`) — cancelling a *run* never sends a stop command to a printer as an
   automatic side effect (FR-017/FR-024d). Stopping that specific job remains possible as its own separate
   action — see Story 8.
3. Let the still-printing job run to completion normally → succeeds, records its outcome and filament
   usage despite its run already being `Cancelled` (Edge Cases).

## Story 8 — Remote pause/resume/cancel for one specific printing job, telemetry-confirmed

1. Dispatch a job to a `SimulatorPrinterConnector`-backed printer (now `Printing`). `POST
   /api/v1/print-jobs/{id}/pause` → `202`, response `status` still `"Printing"` (FR-024a — not asserted
   optimistically). Once the simulator's telemetry reflects the pause, `GET /api/v1/print-jobs/{id}` shows
   `status: "Paused"`, delivered to a subscribed client via the same `FarmHub` push every other job status
   change uses (spec Clarifications, 2026-08-07; `research.md` R12).
2. `POST /api/v1/print-jobs/{id}/resume` on that paused job → `202`, then `status: "Printing"` again once
   telemetry confirms.
3. `POST /api/v1/print-jobs/{id}/cancel` on a `Printing` job → `202`, `cancelRequestedAtUtc` set,
   `status` still `"Printing"` in the response. Once the simulator's telemetry reports a terminal state,
   `GET /api/v1/print-jobs/{id}` shows `status: "Cancelled"`, not `"Failed"` (FR-024a/c).
4. **Simulate a stop performed directly at the printer** (drive the simulator's own telemetry to a terminal
   state without ever calling the cancel endpoint first, i.e. `cancelRequestedAtUtc` stays `null`) → per
   the spec Assumptions' documented fallback, this resolves as `Failed` unless the simulator is configured
   to emit a distinguishable "user stopped" signal, in which case it resolves as `Cancelled` — confirm
   whichever the fixture's simulated signal implies, and that the two paths (cancel-through-OpenFarm vs.
   stop-at-the-printer) converge on the *same* `Cancelled` result whenever the "user stopped" signal is
   present (spec Clarifications, 2026-08-07).
5. On a **manual-brand** printer's `Printing` job, `POST /api/v1/print-jobs/{id}/pause` → `200` (not `202`)
   with `status: "Paused"` applied immediately, no connector call involved (FR-024b).

## Story 9 — A cancelled or failed job leaves its requirement visibly short, requiring an explicit requeue

1. Queue a **named** run, `quantity: 5` on a Part with `unitsPerPrint: 1` (5 eager jobs). Cancel one of the
   `Queued` jobs (`POST /api/v1/print-jobs/{id}/cancel`) → `GET /api/v1/production-runs/{id}` shows that
   requirement's `jobsNeedingRequeue` containing the cancelled job's id (Clarifications, 2026-08-07; FR-025).
2. Complete the remaining 4 jobs. `POST /api/v1/production-runs/{id}/close` → `409` (via the normal
   `Complete`-then-`Close` gate) — the run cannot reach `Complete` at all while the shortfall is
   un-requeued (FR-016a, FR-025).
3. `POST /api/v1/print-jobs/{cancelledJobId}/requeue` → `201`, one new job created for the same
   requirement, `assignmentMode`/`revisionMode` following the line's own rules exactly as any other job
   would (FR-025a). `GET /api/v1/production-runs/{id}` no longer shows that job in `jobsNeedingRequeue`.
4. Complete the requeued job → the requirement is now satisfied; the named run's last-job completion
   prompts the usual confirmation (Story 1, Acceptance Scenario 6) before reaching `Complete`.
5. `POST /api/v1/print-jobs/{cancelledJobId}/requeue` again (the same already-requeued job) → `409`
   (`PrintJobAlreadyRequeuedException`, FR-025a).
6. Repeat step 1 against an **unlimited/backfill** line's job instead → `jobsNeedingRequeue` never includes
   it, and `POST .../requeue` on it → `409` (`PrintJobNotEligibleForRequeueException`, FR-025d) — the
   dispatcher's own lazy creation already covers backfill, no requeue action applies there.

## Activity log check (FR-020)

After Stories 1–9, `GET` the activity log filtered to the created runs/jobs → entries exist for run
creation, dispatch assignment, status transitions (including pause/resume/cancel requests and their
telemetry-confirmed outcomes), cancellation, revision-mode changes, and requeue actions, each attributed
to the acting operator (or `System` for dispatcher-driven or telemetry-driven changes) with a timestamp.
