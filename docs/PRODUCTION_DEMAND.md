# Production demand and Runs

Production demand owns scheduling independently of a user-facing Production Run.
A demand targets one Part or Assembly, captures its requirements, and optionally
belongs to a Run. Each Print Job remains one physical plate attempt; new finite unit/set demand can select a partial plate. Runs group
finite demand for order/batch progress and retain completion, write-off, closing,
and reopening behavior. Direct jobs require no Run or sign-off.

## Quantities and planning

The server's `ProductionPlanner` supplies both previews and job materialization.
Parts default to **Plates**, with an explicit **Unit target** option everywhere.
Assemblies use **Sets**: explode the full batch, aggregate each Part, then split
by that Part's capacity, selecting only the required complete units on the final plate. Never multiply the plates needed for one set
by the number of sets. The UI displays required units, plates, planned units,
and per-job target counts. Assembly component counts follow the requested set
count; component plate overrides are rejected.

`POST /api/v1/production-demands/preview` returns a version fingerprint.
`POST /api/v1/production-demands` validates that version and accepts a submission
ID, explicit quantity basis, optional plate overrides, and optional Run tracking.
Submission IDs bind to their request and make transport retries idempotent.
Existing Run routes remain compatible: an omitted Part basis means units;
an omitted Assembly basis means sets. New Run forms submit explicit bases,
preview versions, and a submission ID. Source locking and the existing BOM
structural lock guard creation; all database changes commit before dispatch or
hub notifications. Estimates and printer-specific production settings remain
live dependencies rather than an immutable production environment.

Finite jobs snapshot their production inputs when queued and retain them for
retry, download, and catalog removal. Backfill follows current Part inputs
until dispatch. An unsent backfill job refreshes its output-unit count together
with its production snapshot; dispatched snapshots remain immutable.

## Continuous backfill

Queue lists persistent backfill entries even when they have no jobs. Entries
can be enabled, paused, configured, refreshed, or removed. Pausing blocks new
dispatches and retries. Removal cancels unsent jobs and retains running prints
and history. Configuration sets a concurrency cap (default one) and optional
printer pin. Finite work always precedes compatible backfill.

An Assembly backfill entry saves its exploded units-per-set recipe. Balance is
completed plus reserved/in-flight units divided by each component's recipe
ratio. Only components below the next complete-set coverage milestone are
eligible; among them the least-covered compatible component leads. This keeps
a blocked component from causing unlimited surplus elsewhere. Failed or
cancelled jobs do not count toward output. A short database claim rechecks
occupancy and coverage before creating a lazy job; slicing happens afterward.

Refreshing adopts the current BOM, retires old requirements, cancels unsent
jobs, and starts a new balancing period. Old requirements and jobs retain their
original recipe attribution. An already running print continues in its old
period. Future Part file revisions do not require a recipe refresh.

## Compatibility and migration

All existing Runs, IDs, snapshots, queue positions, retry chains, and statuses
remain intact. Existing unlimited Run associations continue to work; the new
Run UI creates finite demand, with continuous production managed from Queue.

The physical `RunLines` table and `PartRequirement.RunLineId` are retained as
legacy storage names. They now refer to the standalone `ProductionDemand`
entity; neither requires a Run. Queue responses expose `demandId` and
`demandMode`, retaining `runLineId` for older clients.

Migration initializes finite/backfill mode and legacy quantity bases. Existing
Assembly backfill ratios are initialized from the current BOM for its existing
captured component requirements; unavailable historical ratios fall back to
one. Refresh explicitly to adopt the current full recipe. No historical BOM
snapshot existed from which to recover older ratios. Downgrade is rejected
when standalone demand or multiple recipe generations would lose information.

Exact finite quantities, complete-unit grouping, supported printer transports,
output accounting, and recovery are documented in [Object skipping](OBJECT_SKIPPING.md).
Backfill retains whole-plate scheduling.

Automated verification covers planning, Run-independent dispatch, retry and
snapshot retention, concurrent submission, backfill lifecycle/balancing,
recipe refresh, and migration with existing data. Responsive layout, themes,
zoom, focus appearance, and overlay stacking require user visual verification.
