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

## Operator workflow

Use the same Production filament selector when adding a part, reviewing a file
replacement, editing a saved part, or overriding a queued job. Existing direct
profile constraints are shown separately and can be cleared. Printer-specific
filament profiles are resolved from the catalog filament's fleet assignments.

Click **Why isn’t this printing?** on a queued job or its detail page. The modal
shows effective requirements and their source, followed by each printer's checks,
required/actual values and configuration links. Queue ordering and printer
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
