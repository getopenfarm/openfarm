# Audit log

The global `/audit-log` table answers who changed a farm record, what changed,
and how the request arrived. `/audit-log/:id` provides the recorded field changes
and related-request navigation. It requires the explicit `audit-log:list` grant.
Migration renames existing user and group grants. There is no old activity-log
endpoint or redirect.

Machine-reported states and alerts remain in printer Recent activity, read through
`GET /api/v1/printers/{id}/activity` with `printers:get-detail`. There is no global
machine-events page. Printer history also includes related production and
maintenance changes. Stats continues to use its existing spans and print records.

## Capture

`OpenFarmDbContext` captures allowlisted business fields before saving. The business
change and audit row participate in the same EF transaction, including an existing
outer transaction. Bulk business writes must use tracked changes or stage an
explicit audit entry inside their transaction. `IActivityLogWriter.StageAsync`
provides semantic event names without committing before a repository save.
Post-processors publish operational notifications; they do not capture mutations.

The HTTP/MCP context records the actor, channel, operation and correlation ID.
Background work uses System; part processing retains its initiating user separately.
Names are snapshots. Missing historical names and diffs are not reconstructed.
Secret protection applies at the domain entry boundary, including nested JSON.
Telemetry, transient progress and unchanged saves do not create business diffs.
External printer commands record acceptance; database transactions cannot undo a
command already accepted by a physical printer.

Application persistence rejects updates or removal of existing audit entries.
This does not provide cryptographic integrity or protection from database
administrators. No automatic retention deletion is configured.

## Queries

The existing event store has explicit Audit and MachineObservation categories.
Ordered composite indexes support category, entity, actor, channel and credential
queries with newest timestamp first and ascending ID ties. Correlation IDs have a
separate index. Related printer IDs are stored in a UUID array with a GIN index;
this preserves printer associations after records are removed.

A future fleet-wide machine history can query the stored MachineObservation
category and printer associations without changing capture. Server reads filter
before counting and pagination. Audit filters and dates are shareable URL state;
dates represent the viewer's local day boundaries converted to UTC. The start
is inclusive and the end is exclusive, preserving sub-millisecond timestamps.

## Audit presentation

The list follows Akaun's Everything / Tokens and apps workflow, grouped Channel,
Token or app, Person, Type/action, and Date filters. Filters remain URL state;
the views retain independent pagination. OpenFarm's shared DataTable owns the
frame, Fields control, row treatment and numbered pagination. Browser rows have
no channel chip; token/app rows use the primary-color cue. Update summaries show
before/after values, while create/remove rows use the action badge.

The detail view groups changes, How it arrived, the affected record, and related
request entries. Creates/removals have one Value column; updates show Before and
After or added/removed set values. Empty related-request cards are omitted and
long lists expand in place. Browser visual checks remain with the user.
