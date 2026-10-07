# Contract: Production runs, jobs & print queue REPR endpoints (`OpenFarm.Api`)

REPR pattern, one FastEndpoints class per endpoint, `[Permission("...")]` per the existing placeholder-
policy precedent, all errors as RFC 7807 `ProblemDetails`. See `data-model.md` for entity shapes and
`research.md` for the reasoning behind the dispatcher, revision handling, and queue-ordering mechanisms.

**There is no "dispatch" or "assign printer" endpoint.** Matching an `Unassigned` job to an idle printer,
and lazily creating a backfill job, are exclusively performed by `ProductionDispatchBackgroundService`
(`research.md` R4) in response to a printer reaching Idle — never a direct API call. The
operator-facing controls over *assignment* are **Pin**/**Unpin** (constrain or release which printer a job
may go to) and **queue reordering** (control which eligible job the dispatcher considers first).

**Pause/Resume/Cancel (for an already-printing job) are real commands, but never optimistic.** Once a job
is dispatched, the operator can pause, resume, or cancel it (FR-024, reversing the original "never
remote-control a printer" rule for this one, single-job, deliberate case — see spec Clarifications,
2026-08-07). Every one of these endpoints follows the same shape for a live-integrated printer: it sends a
command and returns **without** asserting the new status — the response's `status` field reflects reality
*before* the command, and the actual transition arrives later via `FarmHub`, once the printer's own
telemetry confirms it happened (`research.md` R12). A manual-brand job has no command to send, so its
endpoints apply the change immediately, trusting the operator's own report — the same split `mark-started`
already established. **Requeue** is the one remedy for a shortfall left behind by a cancelled/failed job
(FR-025) — not a printer command at all, just an ordinary new job creation.

## Production Runs

### `POST /api/v1/production-runs`
`[Permission("production-runs:create")]` — creates a run (FR-001), snapshots BOM explosion for every
Assembly line (FR-003), and eagerly creates every finite line's whole-plate jobs in the same request
(FR-005b).

**Request**:
```jsonc
{
  "name": "string | null",          // null => unnamed run (FR-001a)
  "dueDate": "2026-09-01 | null",
  "priority": "string | null",
  "notes": "string | null",
  "lines": [
    {
      "targetKind": "Part",          // | "Assembly"
      "targetId": "guid",
      "quantity": 20,                 // integer >= 1, or null for unlimited/backfill (FR-006)
      "revisionMode": "Snapshot | FollowLatest | null",   // null => default per FR-009a (Snapshot for finite, FollowLatest for unlimited)
      "assignmentMode": "Unassigned | Pinned",             // default Unassigned, applied to every job this line creates
      "pinnedPrinterId": "guid | null",                    // required iff assignmentMode == Pinned
      "backfillPrinterCap": 1,                              // meaningful only when quantity is null; default 1
      "backfillCumulativeUnitCeiling": "int | null"         // meaningful only when quantity is null; default null (no ceiling)
    }
  ]
}
```
`lines` MUST contain at least one entry. Everything selected in one call becomes one run (FR-001).

**Response 201**: the created run, same shape as `GET /api/v1/production-runs/{id}` below, including the
freshly-created job counts per requirement.

**Response 400** — empty `lines`; a line's `quantity` is present but `< 1`; `assignmentMode: "Pinned"`
with no `pinnedPrinterId`.
**Response 404** — a line's `targetId` doesn't resolve to an existing Part/Assembly in the caller's
organization.

### `GET /api/v1/production-runs`
`[Permission("production-runs:view")]` — paginated list, mirrors `GET /assemblies`'s envelope.

```jsonc
{
  "items": [
    {
      "id": "guid",
      "displayLabel": "string",      // Name if set, else derived from contents (FR-001a) — e.g. "Bracket ×20" or "3 parts — bracket, knob, base"
      "status": "Active",
      "dueDate": "2026-09-01 | null",
      "lineCount": 2,
      "jobSummary": { "queued": 3, "printing": 1, "completed": 1, "failed": 0, "cancelled": 0 }
    }
  ],
  "totalCount": 1
}
```

### `GET /api/v1/production-runs/{id}`
`[Permission("production-runs:view")]` — `RunDetailView` (`data-model.md`), one level of line/requirement
summary; individual jobs are fetched via the print-jobs endpoints below, filtered by requirement.

**Response 404** — no such run.

### `POST /api/v1/production-runs/{id}/close`
`[Permission("production-runs:close")]` — the explicit action reaching the terminal `Closed` state (spec
Assumptions — manual only in this phase). **Response 200**, updated run. **Response 409** — run is not
currently `Complete` (only a completed run can be closed).

### `POST /api/v1/production-runs/{id}/cancel`
`[Permission("production-runs:cancel")]` — FR-017. Cancels every queued, undispatched job belonging to the
run; a job already printing is left alone and still records its eventual outcome. **Response 200**,
updated run (status `Cancelled`). **Response 409** — run already `Closed`/`Cancelled`.

### `PUT /api/v1/production-runs/{runId}/lines/{lineId}/revision-mode`
`[Permission("production-runs:edit")]` — FR-009b. `{ "revisionMode": "Snapshot" | "FollowLatest" }` →
`200`. Affects only plates dispatched after this call — already-dispatched/printing jobs are untouched.

### `PUT /api/v1/production-runs/{runId}/lines/{lineId}/backfill-limits`
`[Permission("production-runs:edit")]` — FR-006c/d, only valid on a line with `quantity: null`.
`{ "printerCap": 1, "cumulativeUnitCeiling": null }` → `200`. **Response 409** — line is not unlimited.

## Print Jobs / Queue

### `GET /api/v1/print-jobs?view=queue&status=Queued&page=1&pageSize=100`
`[Permission("print-jobs:view")]` — the flat, `QueuePosition`-ordered "dispatch order" lens (FR-008,
`QueueJobRow`). Default `view=queue`; `view=backfill` returns the separate backfill-tier section
(rendered below the main queue, per spec's "by dispatch order" framing — never interleaved with real
demand).

```jsonc
{
  "items": [
    {
      "jobId": "guid", "queuePosition": 2048.0, "status": "Queued",
      "assignmentMode": "Unassigned", "pinnedPrinterId": null, "dispatchedPrinterId": null,
      "isBlocked": false,
      "partId": "guid", "partName": "Bracket", "plateQuantity": 4,
      "runLineId": "guid", "productionRunId": "guid", "runDisplayLabel": "Bracket ×20"
    }
  ],
  "totalCount": 5
}
```
`isBlocked: true` marks a `Pinned` job whose target printer is currently offline/in maintenance/
AwaitingClear (FR-007c) — surfaced to the operator rather than left ambiguous alongside an ordinary queued
job.

Cancelled jobs are excluded from both queue views and their `totalCount`. Removing an
undispatched queued job uses the individual cancel endpoint; its job detail and production-run
history remain available, including shortfall/requeue tracking. The matching field opens the
general job diagnosis dialog for blocked and eligible jobs; job detail also exposes diagnosis.

### `GET /api/v1/print-jobs/{id}`
`[Permission("print-jobs:view")]` — full job detail including `FilamentConsumption`,
`FileRevisionHash`/`Number`, timestamps. **Response 404** — not found.

### `POST /api/v1/print-jobs/{id}/reorder`
`[Permission("print-jobs:reorder")]` — FR-008, the fractional-index move (`research.md` R3).

**Request**: `{ "afterJobId": "guid | null" }` — place this job immediately after `afterJobId` in queue
order; `null` moves it to the very top. The server computes the new `QueuePosition` as the midpoint
between `afterJobId`'s position and the position of whatever job currently follows it (or an offset
below/above the queue's current min/max at either end).

**Response 200**: `{ "jobId": "guid", "queuePosition": 1536.0 }`.
**Response 404** — job or `afterJobId` not found. **Response 409** — job is no longer `Queued`
(`PrintJobAlreadyDispatchedException` — reordering a job already printing/completed has no meaning).

### `POST /api/v1/print-jobs/{id}/pin`
`[Permission("print-jobs:pin")]` — FR-007. `{ "printerId": "guid" }` → `200`, `assignmentMode: "Pinned"`.
**Response 400** — `printerId` missing. **Response 404** — job or printer not found. **Response 409** —
job is no longer `Queued`.

### `POST /api/v1/print-jobs/{id}/unpin`
`[Permission("print-jobs:pin")]` — reverts to `Unassigned`, eligible for ordinary dispatch matching again.
Same `404`/`409` cases as pin.

### `POST /api/v1/print-jobs/{id}/mark-started`
`[Permission("print-jobs:update-status")]` — FR-012's manual-brand path: the operator has retrieved the
file and started the print physically. **Response 200**, `status: "Printing"`, `startedAtUtc` set.
**Response 409** — job's printer has live integration (this action doesn't apply — auto-detected instead)
or job is not in a dispatched-but-not-started state.

### `POST /api/v1/print-jobs/{id}/pause`
`[Permission("print-jobs:remote-control")]` — FR-024, only valid on a `Printing` job. For a
live-integrated printer, this sends `IPrinterConnector.PauseJobAsync` and returns immediately — it does
**not** wait for or assume the pause happened; the job's `status` in the response body is still
`"Printing"`, and only flips to `"Paused"` once telemetry confirms it (`research.md` R12), which the
client observes through the normal `FarmHub` push, not this response. For a manual-brand job, there is no
command to send — the response directly reflects `status: "Paused"`, trusting the operator's own report
(FR-024b). **Response 202** (live-integrated — request accepted, not yet confirmed) or **200**
(manual-brand — applied immediately). **Response 409** — job is not `Printing`, or the connector call
itself failed to reach the printer (a real error, distinct from "sent but not yet confirmed").

### `POST /api/v1/print-jobs/{id}/resume`
`[Permission("print-jobs:remote-control")]` — the mirror of pause, only valid on a `Paused` job. Same
`202`/`200`/`409` shape.

### `POST /api/v1/print-jobs/{id}/requeue`
`[Permission("print-jobs:requeue")]` — FR-025a, only valid on a job that is `Cancelled` or `Failed`,
belongs to a *finite* requirement, and has not already been requeued. Creates one new whole-plate job for
the same requirement (ordinary job-creation rules — FR-005/FR-009 — apply exactly as they would for any
other job on that line) and links it back to the original.

**Response 201**: the newly created job (same shape as `GET /api/v1/print-jobs/{id}`).
**Response 409** — job is not `Cancelled`/`Failed`, already has a `requeuedAsJobId` set
(`PrintJobAlreadyRequeuedException`), or belongs to an unlimited/backfill requirement, which never needs
requeueing (FR-025d, `PrintJobNotEligibleForRequeueException`).

### `POST /api/v1/print-jobs/{id}/complete`
`[Permission("print-jobs:update-status")]` — FR-013, manual-brand path (a live-integrated job's completion
is auto-detected, but still requires this same filament-consumption body to be supplied by the operator
afterward if not derivable from telemetry — see `research.md` R7).

**Request**:
```jsonc
{ "filamentConsumption": [ { "materialType": "PETG", "detectedColorHex": "#000000", "spoolId": "guid | null", "gramsConsumed": 42.5 } ] }
```
**Response 200**: updated job, `status: "Completed"`, `endedAtUtc`/`actualDurationMinutes` set, printer
moved to `AwaitingClear` (FR-014a). **Response 400** — a consumption line's `(materialType,
detectedColorHex)` pair doesn't match one already on the job (FR-013b's defaulted set).

### `POST /api/v1/print-jobs/{id}/fail`
`[Permission("print-jobs:update-status")]` — same body shape as `complete` (FR-013a — filament consumed
before failure is still recorded). **Response 200**: `status: "Failed"`, printer moved to `AwaitingClear`.

### `POST /api/v1/print-jobs/{id}/cancel`
`[Permission("print-jobs:cancel")]` — cancels an individual job, `Queued` or `Printing` (FR-024, distinct
from cancelling a whole *run* — FR-017/FR-024d, which never reaches this endpoint automatically for a
printing job).
- If the job is `Queued` (not yet dispatched): applied immediately. **Response 200**, `status: "Cancelled"`.
- If the job is `Printing` on a live-integrated printer: sends `IPrinterConnector.StopJobAsync`, records
  `cancelRequestedAtUtc`, and returns without changing `status` yet — the same request/confirm split as
  pause/resume above. **Response 202**, `status` still `"Printing"`; the client observes the eventual
  `"Cancelled"` transition via `FarmHub` once telemetry confirms it (`research.md` R12).
- If the job is `Printing` on a manual-brand printer: applied immediately, trusting the operator's report.
  **Response 200**, `status: "Cancelled"`.

**Response 409** — job is already in a terminal state (`Completed`/`Failed`/`Cancelled`).


## Completion audit additions (2026-10-04)

- `PATCH /api/v1/parts/{id}/units-per-print` accepts `{ "unitsPerPrint": 4 }` (positive integer),
  using `parts:replace-file`. New sliced parts suggest a parseable object count; replacement preserves
  the existing operator default. Future jobs use the updated default; existing plate quantities stay fixed.
- `GET /api/v1/print-jobs/{id}/file` (`print-jobs:read`) downloads the job's retained revision.
  Sliceable files require a dispatched printer; stored G-code can be retrieved before assignment.
- `PUT /api/v1/print-jobs/{id}/filament-requirements` (`print-jobs:pin`) accepts a nonempty
  `requirements` collection of material/color entries before dispatch and wakes idle printers.
- `POST /api/v1/print-jobs/{id}/filament-consumption` (`print-jobs:update-status`) accepts the
  existing `filamentConsumption` breakdown after Completed/Failed/Cancelled. Multiple spools for the
  same material/color retain separate lines. Repeating totals is idempotent; increases decrement only
  the additional grams. Decreasing a previously recorded total returns 400.
- `POST /api/v1/printers/{id}/mark-ready` accepts `{ "confirmRunCompletion": false }` by default.
  When the last finite requirement of a named run is satisfied, the server returns 409 with title
  `Run completion confirmation required`. Retrying with true completes the run with operator attribution
  and clears the printer. Unnamed runs complete automatically with system attribution.
- Live-integrated jobs' Printing/Paused to Completed/Failed transitions belong to telemetry. The
  complete/fail endpoints reject attempts to override live status but may attach usage to an already
  matching terminal outcome. A requested stop followed by either FINISH or FAILED resolves Cancelled.
- Revision-mode and backfill-limit requests validate enum names and positive limits (400), and reject
  a line belonging to a different route run (404).
