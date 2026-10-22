# Job filament matching and diagnostics

A selected catalog filament colour variant is the effective production identity.
An active tracked spool of that exact variant satisfies the requirement even when
the uploaded file reports a different colour. The file's detected colour remains
visible as metadata. Without a catalog choice, existing material and colour
requirements apply (hexadecimal colour comparison is case-insensitive).
Selecting a different material from a known file requirement is rejected.

Production still needs an available printer, confirmed nozzle telemetry, an active
loaded spool, and compatible machine and filament profile assignments. Files that
need slicing also require process and build-plate settings. Reloaders contribute
only their selected feeding slot; standby and archived slots do not qualify.

Snapshot jobs store their filament choices when created. Follow-latest jobs resolve
the current part's choices while queued and freeze them for dispatch. Explicit
job-level edits take precedence. A part edit cannot alter running jobs or recorded
consumption. Legacy Snapshot jobs retain their recorded material/colour values;
historical catalog choices are not guessed from the current part. The additive
JobFilamentIdentity migration preserves older explicit overrides recorded in the
activity log.

## Enough filament

A printer is only a candidate when its loaded filament covers the job. For each requirement the job needs its estimate
from the part's file (per colour, or the part's total for a one-colour part) plus 10%. What a spool covers is its own
remaining weight plus what continuation would feed after it: every loaded spool of the same colour in the same AMS, or a
reloader's loaded slots in slot order up to the first other colour. An external spool covers only itself. Two
requirements drawing on the same spools add up.

- A printer that falls short is skipped: an unassigned job goes to one that has enough, and a pinned job waits. Its
  diagnostics show a `filament-stock` check, e.g. **Needs 132 g White PLA (120 g + 10%) · P01 has 50 g**, with a link to
  the spool. Load more, or correct the spool's weight; a corrected weight on a loaded spool asks its printer to look at
  the queue again.
- Among spools of the same filament, the emptiest that holds the job alone is used, then the emptiest that holds it with
  continuation, so part-used spools get used up without running out mid-print. Without an estimate the lowest slot is used,
  as before, and nothing is checked: an unknown need never blocks.
- The check runs inside `ProductionProfileReadiness`, the one path behind matching, dispatch and diagnostics, after the
  profiles resolve. The margin is a fixed 10%; an AMS is assumed to switch to its backup spools, as the inventory
  bookkeeping already does.

## Operator workflow

Use the same Production filament selector when adding a part, reviewing a file
replacement, editing a saved part, or overriding a queued job. Existing direct
profile constraints are shown separately and can be cleared. Printer-specific
filament profiles are resolved from the catalog filament's fleet assignments.

Click **Job diagnosis** on a job's detail page, or the status link in its queue
row (for example **No matching printer**). The modal
shows effective requirements and their source, followed by each printer's checks,
required/actual values and configuration links. The queue row also shows the
blocking reason, and **Last attempt failed: …** when a dispatch attempt stopped
after the job had passed the checks. Queue ordering and printer
preference are shown separately from compatibility. Refresh reevaluates current
data; opening diagnostics does not request dispatch.

## API

`GET /api/v1/print-jobs/{id}/matching-diagnostics` requires `print-jobs:view`
and uses the normal repositories. It returns evaluation time, overall outcome,
effective filament requirements, job checks, and per-printer results. Checks have
stable codes and Passed/Failed/NotEvaluated status. Configuration links are
separate from explanatory messages.

Job filament-requirement updates accept optional `narrowFilamentVariantId` or
`narrowFilamentProfileId` (mutually exclusive) per requirement; job detail returns
these constraints alongside the existing material, colour and consumption fields.

The migration is applied by the API's normal startup migration flow. Deploy the
backend and regenerated web client together. Existing data needs no manual rewrite.
