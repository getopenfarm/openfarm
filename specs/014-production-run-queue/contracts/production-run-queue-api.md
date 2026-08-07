# Contract: Production runs, jobs & print queue REPR endpoints (`OpenFarm.Api`)

REPR pattern, one FastEndpoints class per endpoint, `[Permission("...")]` per the existing placeholder-
policy precedent, all errors as RFC 7807 `ProblemDetails`. See `data-model.md` for entity shapes and
`research.md` for the reasoning behind the dispatcher, revision handling, and queue-ordering mechanisms.

**There is no "dispatch" or "assign printer" endpoint.** Matching an `Unassigned` job to an idle printer,
and lazily creating a backfill job, are exclusively performed by `ProductionDispatchBackgroundService`
(`research.md` R4) in response to a printer reaching Idle — never a direct API call. The only
operator-facing controls over assignment are **Pin**/**Unpin** (constrain or release which printer a job
may go to) and **queue reordering** (control which eligible job the dispatcher considers first). This
mirrors the existing precedent that nothing in this codebase lets an operator manually push a printer
command — `Printer`'s own endpoints (Phase 1/5) never included a "start print" action either, precisely
because that capability didn't exist until this feature.

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

### `POST /api/v1/print-jobs/{id}/pause` / `POST /api/v1/print-jobs/{id}/resume`
`[Permission("print-jobs:update-status")]` — manual-brand path only; a live-integrated printer's
pause/resume is auto-detected from telemetry, same as start. **Response 409** if attempted on a
live-integrated job (redundant with auto-detection) or from an illegal status.

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
`[Permission("print-jobs:cancel")]` — FR-017, an individual undispatched job (distinct from cancelling a
whole run). **Response 200**: `status: "Cancelled"`. **Response 409** — job already printing (cancel the
run instead, or let it finish — OpenFarm never remote-stops a physical print).
