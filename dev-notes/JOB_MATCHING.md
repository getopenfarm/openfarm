# Job filament matching and diagnostics

Each filament requirement has at most one catalog choice, and it replaces the file's
detected colour:

- **One colour** (`narrowFilamentVariantId`): an active tracked spool of that exact
  variant satisfies the requirement, even when the file reports a different colour.
- **Any colour of one filament** (`narrowFilamentId`): a spool of any variant of that
  catalog filament satisfies it.
- **Any colour of the material** (`anyColor`): a spool of any brand and colour of the
  required material satisfies it.

  Shortage and forecast numbers leave both any-colour kinds out, like "any material"
  lines; dispatch still checks the loaded spool's grams.
- **No choice**: the file's material and colour apply, from any brand (hexadecimal
  colour comparison is case-insensitive).

The file's detected colour remains visible as metadata. The rule lives once in
`JobFilamentRequirementSpec.Accepts`. Every place that names a choice uses
`FilamentChoiceLabel` (web: `genericLabel`/`catalogLabel`): `Any PETG · #000000`,
`Any PETG · Any color`, `Aliz PETG Basic · Any color`, `Aliz PETG Basic · Black`. Selecting a different material from a known
file requirement is rejected.

A print's lines pick spools narrowest first (one colour, then any colour of a filament, then
any colour), each preferring a spool no other line took, so two any-colour lines print in two
spools and neither takes the one a fixed colour needs. Lines share a spool only when no other
fits (`ProductionProfileReadiness.ResolveAsync`). Re-slicing keeps a choice for the same
material and colour; an any-colour choice also survives a one-for-one colour swap of its
material (`Part.PreviousRequirementFor`).

Production still needs an available printer, confirmed nozzle telemetry, an active
loaded spool, and compatible machine and filament profile assignments. Each
required filament must be loaded on an extruder whose confirmed nozzle is the
part's nozzle; on a printer with several extruders the `extruder-nozzle` check
names the one that differs ("T2: 0.6 mm, job needs 0.4 mm."). Files that
need slicing also require process and build-plate settings. Reloaders contribute
only their selected feeding slot; standby and archived slots do not qualify.

Snapshot jobs store their filament choices when created. Follow-latest jobs resolve
the current part's choices while queued and freeze them for dispatch. Explicit
job-level edits take precedence. A part edit cannot alter running jobs or recorded
consumption. Legacy Snapshot jobs retain their recorded material/colour values;
historical catalog choices are not guessed from the current part. The additive
JobFilamentIdentity migration preserves older explicit overrides recorded in the
activity log.

## Which idle printer takes an unassigned job

When several idle printers qualify for the same unassigned job, the simplest one takes it: fewest AMS trays, then the lowest
id. The others leave it to that printer and wake it. That printer may take an earlier job of its own instead (pinned or not).
Once it holds a job, the printers that left the match to it are woken again, so the job does not wait for their next status
report. A manual printer sends no status reports.

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
- After slicing for the chosen printer, dispatch checks again with the file's exact grams. If they no longer fit, nothing is
  sent: the job keeps the reason and those grams, and the next attempt checks them before slicing again, so a printer
  that has enough takes it instead.
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

Part and job filament-requirement updates accept optional `narrowFilamentVariantId`,
`narrowFilamentId`, `narrowFilamentProfileId` or `anyColor` (at most one) per requirement; part
and job detail return these constraints alongside the existing material, colour and
consumption fields.

The migration is applied by the API's normal startup migration flow. Deploy the
backend and regenerated web client together. Existing data needs no manual rewrite.
