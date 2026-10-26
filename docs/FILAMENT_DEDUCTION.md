# Automatic filament deduction

Job filament use is deducted automatically; operators never enter it. Manual spool usage
(`POST /api/v1/spools/{id}/usage`) remains only for adjustments (test extrusions, purge, drift).

## Flow

1. **Dispatch** (`ProductionDispatchBackgroundService`): the file about to be sent is read
   (`JobFilamentUsageReader`: the dispatched plate's `slice_info.config` `used_g`, else the G-code footer's
   `filament used [g]`). `JobFilamentUsageMatcher` assigns each filament's grams to a job line by
   material and colour (by file order when overridden colours leave a multi-line job unmatched).
   Start-gcode purge is added to the slicer's grams (see Usage curve). `ProductionProfileReadiness.ResolveAsync` picks the loaded Using spool per
   line. Both are frozen on the line (`SpoolId`, `EstimatedGrams`) in the dispatch save, so a spool
   swapped mid-print does not change what the job draws from.
2. **Terminal transition**: `JobTelemetrySync` (live printers) or the manual Complete/Fail handlers
   call `JobFilamentAutoDeduction` in the same save as the status change.
   - Completed: 100% of `EstimatedGrams`.
   - Failed / Cancelled: the line's usage curve at the last observed `layer_num` and `mc_percent`; without a
     curve, `layer_num / total_layer_num`, else `mc_percent`.
   - Manual-brand printer: Completed deducts in full; Failed has no progress, so nothing is deducted.
3. Deduction applies only the difference from what the line already recorded, writes a
   `SpoolUsageRecord` with source `Job` (see Spool history), and triggers auto-continuation when a spool hits zero. The first
   deduction of a line also writes its price per kg and where it came from (see Realized cost in `docs/FILAMENT_PRICING.md`).
4. A line with no bound spool, no estimate, no progress, or a spool no longer Using is skipped, never
   thrown, and logged as `PrintJobFilamentDeductionSkipped` on the job. Correct with manual spool usage.

## Usage curve

At dispatch `GCodeFilamentCurveReader` reads the dispatched plate's G-code: net extrusion (`G0-G3` E, retract and
un-retract cancel) per tool per layer, with the layer taken from `; layer num/total_layer_count: N/T`
(the same N the printer reports as `layer_num`) or `;LAYER_CHANGE`. The tool is the last `T<n>`, so
colour-change flush counts to the new colour. Each line's tools are summed and normalised to a
cumulative share per layer (`FilamentUsageCurve`, stored on the line). Stopped on layer L, a line has
used everything through L-1 plus half of L.

- **Sub-layer position.** The reader also marks every point where the layer or the `M73 P<percent>`
  progress changes. A stop reported as layer L at percent P falls in that stretch of the print, so the
  curve returns the midpoint of the stretch (about 1% of print time wide). If the file has no matching
  mark, it uses half of L.
- **Purge counts.** Everything extruded before layer 1 (nozzle prime, load line) is the tool's prime. The
  slicer's `used_g` leaves it out, so `JobFilamentUsageReader` adds `prime_mm * used_g / curve_mm` to the
  grams, and the curve counts the prime as used from print start. A print that fails on layer 1 still
  deducts it.

On the Orca Benchy fixture the curve total is within 1% of the slicer's `filament used [mm]`.

## Prints started on the printer

A print started at the printer (Studio, Handy, touchscreen, SD card) is an `ExternalPrint`. It shares the `Prints` table
(`Source = External`) and the `PrintFilaments` lines with `PrintJob`, so it deducts through the same `JobFilamentAutoDeduction`
and shows on the Jobs page in its own "Printer-started prints" section (`GET /api/v1/print-jobs?view=external`).

- **Start:** `ExternalPrintSync` sees a RUNNING/PAUSE printer with no active `PrintJob`, reads the printed file back from the
  printer, and freezes per-tool grams and curve on the lines. Trays reported active are recorded as they appear.
  The read runs in the background, once per print, so the printer keeps reporting while it downloads (a P1S serves about
  150 KiB/s, so a 2 MB file takes 15 to 30 s); the print appears on the first telemetry event after the file arrives. A failure
  logs one warning with its duration and reason.
- **Which plate:** a file printed from one plate of a multi-plate project keeps that plate's number (`Metadata/plate_4.gcode`),
  and `gcode_file` does not always name it. The plate comes from the file: the one the printer names if the file has it, else
  the only plate with G-code, else (several plates, none named) the print is not tracked and a warning says so. Dispatch
  decides its plate once (`JobFileResolver.PlateToPrint`: plate 1, else the only sliced plate) and reads that plate.
- **Finding the file:** each candidate path is downloaded directly and a 550 means "not there". `FileExists` is avoided: it
  lists the folder and throws if any unrelated file has a name FluentFTP treats as path traversal (e.g. `....`).
- **Transitions:** `JobTelemetrySync` drives pause, resume, finish and fail for both kinds. Only an OpenFarm job can move
  Queued → Printing or be Cancelled; a stop at the printer arrives as FAILED.
- **Which spool is on a tray:** an AMS tray is the spool on that unit and slot. Tray 254 (the external path) is the spool in the
  external slot, unless an active Automatic Filament Reloader is declared to replace the external port: then it is the spool in
  that reloader's feeding slot, and with no feeding slot chosen it is unknown (the replaced external slot is not used). A tray
  first seen with no known spool takes one when it becomes known; a known spool is never replaced.
- **Spool binding:** one tool takes the first tray seen. Several tools are paired when the print ends (`ToolTrayMatcher`): each
  tool's material and colour from the file against the AMS trays the printer last reported, so it works for a print joined
  mid-print and whatever order the trays were used in. The AMS reports a colour a little off the slicer's, so the nearest tray
  of the same material wins if it is close and clearly closer than the next; a tie falls to a tray seen active, else the line
  stays unbound. Unbound lines skip deduction and log `ExternalPrintFilamentDeductionSkipped` (recover with `Assign spool`).
- **Not in the queue:** `dbContext.PrintJobs` is the `PrintJob` subtype, so dispatch, backfill ordering, run completion and
  requeue never see an external print.
- Deleting a printer keeps its external prints with `PrinterId = null`.

## Lines that were not deducted (bell)

When a closed print leaves a filament line unrecorded, automatic deduction stores why on the line (`SkippedReason`) and the
header bell lists the print under "Action required" (`GET /api/v1/notifications`, refreshed by the `NotificationsChanged`
hub event). Dispatched jobs and prints started on the printer are treated the same.

| Reason | Meaning | Action |
|---|---|---|
| `NoSpool` | No spool bound to the line | Assign spool |
| `SpoolNotLoaded` | The bound spool is not in state `Using` | Assign spool (another loaded spool) |
| `NoEstimate` | The slicer's grams could not be read | Dismiss; record usage on the spool by hand |
| `NoProgress` | A stopped print with no layer, percent or curve | Dismiss; record usage on the spool by hand |

- **Assign spool** (`POST /api/v1/prints/{id}/filament-lines/{lineId}/spool`) binds a loaded spool and re-runs the same
  delta-based deduction from the progress stored on the print when it closed (`StopLayer`, `StopTotalLayers`, `StopPercent`),
  so the result matches an automatic one. Only a loaded (`Using`) spool is accepted.
- **Dismiss** (`POST /api/v1/notifications/{printId}/dismiss`) sets `FilamentFollowUpDismissedAtUtc`; the spool is untouched.
- The reasons a spool cannot fix take priority when several apply, so an operator is never offered a spool choice that cannot work.
- Prints that closed before the reason was stored do not appear.

## Spool history

Every change to a spool's remaining weight writes one append-only `SpoolUsageRecord`; the spool page shows them newest first.

| Source | Written by | `JobId` | Reason |
|---|---|---|---|
| `Job` | `JobFilamentAutoDeduction`, one row per deduction | the `Print` id | none |
| `Manual` | `POST /api/v1/spools/{id}/usage` | whatever the client sent, unvalidated | none |
| `Correction` | `PUT /api/v1/spools/{id}` when the remaining weight changes | none | optional, up to 200 characters |
| `WriteOff` | finishing a spool (`PUT /api/v1/spools/{id}/unload`, `MarkFinished`) that still had grams left | none | optional, up to 200 characters |
| `Restore` | `PUT /api/v1/spools/{id}/restore` when the spool's latest entry is its `WriteOff` | none | none |

- `GramsConsumed` is grams taken off the spool, so it is negative when a correction or a restore puts filament back. `RemainingAfterGrams` is
  the weight once the entry applied; it is null on rows written before that column existed. A deduction larger than what was
  left keeps the full grams and records 0 g remaining (the spool's weight stops at zero and `HasUsageOverrun` is set).
- A weight edit and its `Correction` row commit in one save (`ISpoolUsageRecordRepository.Add` tracks the row without saving), so a
  stale row version is the spool's 409 and neither is written. Saving the same weight, or only the label, writes nothing.
- **Finishing and restoring.** A spool finishes from a slot or from storage. Whatever is left is written off: the spool goes to
  0 g with a `WriteOff` row, so the history still balances. Continuation finishes a spool at 0 g, which writes no row. Restore
  brings a finished spool back to storage (never into a slot) and puts back the grams of its `WriteOff`, but only when that
  write-off is still its latest entry: a weight corrected after finishing is kept. A spool finished before write-offs existed
  kept its grams, and restores with them.
- `RemainingWeightGrams` stays the stored source of truth. The history is an audit trail; nothing sums it.
- `GET /api/v1/spools` lists spools oldest registered first; `newestFirst=true` reverses it, which is how a filament's
  finished spools page (there is no finish time, so newest registered stands in for newest finished).
- Read it with `GET /api/v1/spools/{id}/history?page&pageSize` (default 50, max 100). Each entry is left-joined to its `Print`,
  so an entry keeps showing after its print is deleted, without a link. `GET /api/v1/spools/{id}` returns the one spool.
- **Every spool's history.** `GET /api/v1/spools/history?from&to&filamentId&filamentVariantId&source&page&pageSize` returns the
  same entries across spools, each naming its spool, material and color; a filament's *History* tab (`/filament/{id}?tab=history`)
  shows its spools' entries with `filamentId`. `from` is inclusive, `to` exclusive, and the page sends local calendar days as
  UTC instants. `source` is a name (`WriteOff`), never the enum's number.
  `GET /api/v1/spools/history.csv` takes the same filters and returns every match as a file. The CSV is RFC 4180 with CRLF
  rows and a UTF-8 BOM, numbers in invariant culture and times in UTC ISO 8601. Text starting with `=`, `+`, `-`, `@`, tab or
  CR gets a leading `'`, so a spreadsheet shows it instead of running it as a formula (`Endpoints/Csv.cs`).
- **Live refresh.** A print closing (from telemetry, or Complete/Fail on a manual printer) publishes `NotificationsChanged`, and
  so does every successful spool or filament request (`ActivityLogPostProcessor`). The web refreshes spools and Feed units on
  it. The `SpoolStateChanged` hub event exists but is never raised.

## Known limits
- Spool binding follows the feed mapping in `docs/BAMBU_LAN_PRINTING.md`: on an AMS printer only the AMS or only the external
  spool is a usable feed at a time, so a spool on the other one is never bound.

## Open issues (check back later)

Found while making the estimate more exact. Each matters once the product grows.

### 1. Legacy multi-plate parts still sum every plate
- A part prints one plate (`Part.PlateNumber`): its estimate, per-colour grams and production slice (`--slice N`) are that plate's,
  and dispatch sends, maps and deducts it (`JobFileContent.Plate`).
- A part confirmed before plates were chosen has no plate: it prints plate 1 (else the file's only sliced plate) but keeps the
  estimate summed over every plate until it is reprocessed, which asks for its plate.

### 2. Manual Fail deducts nothing
- `FailPrintJobCommandHandler.cs:42` and `CompletePrintJobCommandHandler.cs:47` pass `new JobProgress(job.Status)` with no layer
  or percent, so a manual Fail skips every line (logged as `PrintJobFilamentDeductionSkipped`). A manual Complete deducts in full.
- Both handlers reject a job on a live-integrated brand (`PrinterModelCatalog.SupportsLiveIntegration`: Bambu and System, every
  brand registrable today). They are reachable only for a job whose printer was deleted, or once a manual brand exists (Phase 15).
- Bambu (P1S) failures and cancels go through `JobTelemetrySync` with `layer_num` / `mc_percent`, so they are fine.
- **Not verified:** what happens if OpenFarm or the printer is offline when the print ends, so no terminal event arrives.
- **Fix options:** ask for "stopped at layer N / percent" on the Fail/Cancel dialog and feed it into `JobProgress`
  (the curve lookup then works unchanged); or keep manual spool usage (`POST /api/v1/spools/{id}/usage`) as the fallback.
