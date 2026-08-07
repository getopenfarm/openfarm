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
4. `POST /api/v1/print-jobs/{id}/complete` (or let the simulator's own telemetry sequence auto-detect
   completion) → job `status: "Completed"`; `GET /api/v1/printers/{id}` shows `status: "AwaitingClear"`,
   never `"Idle"` (Acceptance Scenario 4, FR-014a). Confirm a second dispatch attempt against this printer
   assigns nothing until `POST /api/v1/printers/{id}/mark-ready` is called (FR-014b).
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

1. Dispatch a job whose Part has one filament requirement (PETG/black). `POST /api/v1/print-jobs/{id}/fail`
   `{ "filamentConsumption": [{ "materialType": "PETG", "detectedColorHex": "#000000", "spoolId": "<spool>",
   "gramsConsumed": 30 }] }` (simulating a print that died partway through) → `200`, `status: "Failed"`,
   the matched `Spool`'s `RemainingWeightGrams` decremented by 30 (Edge Cases, FR-013a) — not by the full
   estimate, and not skipped because the job didn't succeed.

## Story 7 — Cancelling a run never sends a remote stop command

1. Create a run with 3 jobs; dispatch one (now `Printing`), leave two `Queued`.
2. `POST /api/v1/production-runs/{id}/cancel` → the two `Queued` jobs become `Cancelled`; the `Printing`
   job is untouched (still `Printing`) (FR-017).
3. Complete the still-printing job normally (`POST /api/v1/print-jobs/{id}/complete`) → succeeds, records
   its outcome and filament usage despite its run already being `Cancelled` (Edge Cases).

## Activity log check (FR-020)

After Stories 1–4, `GET` the activity log filtered to the created runs/jobs → entries exist for run
creation, dispatch assignment, status transitions, cancellation, and revision-mode changes, each
attributed to the acting operator (or `System` for dispatcher-driven assignment) with a timestamp.
