# Automatic filament deduction

Job filament use is deducted automatically; operators never enter it. Manual spool usage
(`POST /api/v1/spools/{id}/usage`) remains only for adjustments (test extrusions, purge, drift).

## Flow

1. **Dispatch** (`ProductionDispatchBackgroundService`): the file about to be sent is read
   (`JobFilamentUsageReader`: plate 1's `slice_info.config` `used_g`, else the G-code footer's
   `filament used [g]`). `JobFilamentUsageMatcher` assigns each filament's grams to a job line by
   material and colour. `ProductionProfileReadiness.ResolveAsync` picks the loaded Using spool per
   line. Both are frozen on the line (`SpoolId`, `EstimatedGrams`) in the dispatch save, so a spool
   swapped mid-print does not change what the job draws from.
2. **Terminal transition**: `JobTelemetrySync` (live printers) or the manual Complete/Fail handlers
   call `JobFilamentAutoDeduction` in the same save as the status change.
   - Completed: 100% of `EstimatedGrams`.
   - Failed / Cancelled: last observed `layer_num / total_layer_num`, else `mc_percent`.
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
used everything through L-1 plus half of L. Start-up purge before layer 1 is ignored. On the Orca
Benchy fixture the curve total is within 1% of the slicer's `filament used [mm]`.

## Known limits
- Spool binding follows the feed mapping in `docs/BAMBU_LAN_PRINTING.md`: on an AMS printer only the AMS or only the external
  spool is a usable feed at a time, so a spool on the other one is never bound.
