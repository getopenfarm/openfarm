# Stats

`GET /api/v1/reports/stats?from&to&timeZone` feeds the Stats page: the farm over a range of local days, against the same span
just before it. **Output comes from prints; time comes from printer activity.** A print still open adds no hours.

## Range

- `from` inclusive, `to` exclusive, at most 366 days. The page sends the browser's local midnights as UTC instants.
- `timeZone` is an IANA name; Postgres buckets days in it too, so a Windows zone ID is a 400. A 23 or 25 hour day stays one day.
- A print counts on the day it **ended**, and only if it **started**: a queued job cancelled before starting is not a print.

## Figures

| Figure | Definition |
|---|---|
| Prints | Completed + Failed + Cancelled |
| Success | Completed ÷ prints; a cancelled print counts against it |
| Filament, Waste | Grams on Completed lines; on Failed + Cancelled lines (the materials report's own definitions) |
| Waste % | Waste ÷ (used + waste) |
| Time lost | How long failed and cancelled prints ran |
| Units made | Σ `PlateQuantity` of Completed jobs. Dispatched jobs only; every unit on a plate counts as good until outcome lines exist |
| Utilization | Printing hours ÷ observed hours. Observed = every activity except Unknown, Offline included |
| Printer time | Hours Printing, Awaiting clear, Paused, Idle, Offline. *No data* = Unknown + time no stretch covers (before tracking began) |
| Plate clear | Median Awaiting-clear stretch ending in the range |
| Queue wait | Job `CreatedAtUtc → StartedAtUtc`, median and 95th percentile, for jobs started in the range |
| Print time ÷ estimate | Median of actual minutes ÷ the part snapshot's `EstimatedPrintTimeMinutes`, completed jobs. Pauses count |
| Failure codes | Codes in `Prints.StopAlerts` of Failed prints, top 5 |

Money (spend, *Partial*, written off) comes only from the materials report.

Every figure is summed in SQL (`StatsReadModel`). At a farm's few thousand prints a year that is milliseconds, so there are
no rollups: they would have to follow the viewer's time zone and be rebuilt whenever grams or outcomes change after the fact.
Add an index on `Prints.EndedAtUtc` past ~100k prints, and an hourly rollup only if a request passes ~300 ms.

## Printer activity

`Printer.Activity` since `ActivitySinceUtc` is the open stretch; each change appends a closed `PrinterActivitySpan`.

- Derived, in order: Awaiting clear (OpenFarm's own state, kept while the printer is unreachable) → Offline → Paused (reported
  PAUSE) → Printing → Idle.
- Offline starts when the printer was last heard, not when the 30 s sweep noticed.
- On startup each printer heard before the restart records Unknown from its last message: OpenFarm saw nothing then.
- Times earlier than the open stretch are clamped; empty stretches are dropped.

## Print ends the printer did not report live

- **Back to IDLE** with a print still open (reboot, power loss): the print ends Completed if the last progress reached 99 % or
  the last layer, else Failed, and the printer waits for Mark ready. The U1 connector applies the same rule within one connection.
- **Ended during a silence** (offline, or OpenFarm down): a completed print ends at last contact + its remaining minutes, any
  other at last contact.
- **Mark ready sticks**: Awaiting clear is entered only when the reported state changes to FINISH or FAILED. Printers re-send
  their last state on every connect (Bambu `pushall`, Moonraker's first snapshot).
- **Removing a printer** fails its open print, deducting filament to the last progress.
- **Printer-started prints** are recorded on first sight. A file that can't be read back leaves one line of unknown grams,
  which deduction skips as `NoEstimate`, so the print reaches *Action required*.

## Known limits

- A spurious IDLE mid-print would end the print; none is known on Bambu or Moonraker.
- Bambu LAN keeps no job history: a print that starts and ends while OpenFarm is down is never seen, and a P1S print joined
  mid-run starts at first sight (only X1 models report a start time).
- A read-back file that arrives after a short print ended is ignored.
- A printer already offline at shutdown stays Offline through OpenFarm's downtime.
- For the U1, Moonraker's `server.history.list` could give the true outcome and end; not used yet.
