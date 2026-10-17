# Automatic filament deduction

Job filament use is deducted automatically; operators never enter it. Manual spool usage
(`POST /api/v1/spools/{id}/usage`) remains only for adjustments (test extrusions, purge, drift).

## Flow

1. **Dispatch** (`ProductionDispatchBackgroundService`): the file about to be sent is read
   (`JobFilamentUsageReader`: plate 1's `slice_info.config` `used_g`, else the G-code footer's
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
   `SpoolUsageRecord` with source `Job`, and triggers auto-continuation when a spool hits zero.
4. A line with no bound spool, no estimate, no progress, or a spool no longer Using is skipped, never
   thrown, and logged as `PrintJobFilamentDeductionSkipped` on the job. Correct with manual spool usage.

## Usage curve

At dispatch `GCodeFilamentCurveReader` reads plate 1's G-code: net extrusion (`G0-G3` E, retract and
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

## Known limits
- Spool binding follows the feed mapping in `docs/BAMBU_LAN_PRINTING.md`: on an AMS printer only the AMS or only the external
  spool is a usable feed at a time, so a spool on the other one is never bound.

## Open issues (check back later)

Found while making the estimate more exact. Neither is a bug today; both matter once the product grows.

### 1. No plate selection — dispatch and deduction both assume plate 1
- `BambuPrintFile.PlatePath` (`Infrastructure/Printers/Bambu/BambuPrintFile.cs:10`) always sends `Metadata/plate_1.gcode` and
  rejects the file if it is missing. `CliSlicerEngine.ReadEmbeddedPlateGCodeAsync` (`:207`) also reads only `plate_1` after
  slicing ("first (only, today) plate").
- `JobFilamentUsageReader` (`PlateGCode`, `ReadSliceInfo`) reads plate 1 too, so deduction matches what is printed.
- Single-plate sliced files and sliced plain 3mf: fine.
- Multi-plate sliced `.gcode.3mf`: the upload parser reads every plate, but only plate 1 is dispatched. Not traced how a
  multi-plate part becomes a job — verify before relying on it.
- **If plate selection is added:** change `BambuPrintFile`, `CliSlicerEngine`, and `JobFilamentUsageReader.Read` (take a plate
  number: `plate_<n>.gcode` and the `slice_info.config` `<plate>` with `index = n`) together.

### 2. Manual Fail/Cancel deducts nothing
- `FailPrintJobCommandHandler.cs:42` and `CompletePrintJobCommandHandler.cs:47` pass `new JobProgress(job.Status)` with no layer
  or percent, so every line is skipped and logged as `PrintJobFilamentDeductionSkipped`. Manual Complete deducts in full.
  This applies to any printer brand, not just manual-brand printers.
- Bambu (P1S) normal failures and cancels go through `JobTelemetrySync` with `layer_num` / `mc_percent`, so they are fine.
  It hits: manual-brand printers (no telemetry), and a Bambu job an operator fails by hand.
- **Not verified:** what happens if OpenFarm or the printer is offline when the print ends, so no terminal event arrives.
- **Fix options:** ask for "stopped at layer N / percent" on the Fail/Cancel dialog and feed it into `JobProgress`
  (the curve lookup then works unchanged); or keep manual spool usage (`POST /api/v1/spools/{id}/usage`) as the fallback.
