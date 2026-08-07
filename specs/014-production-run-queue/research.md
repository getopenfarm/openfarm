# Phase 0 Research: Production runs, jobs & print queue

No `[NEEDS CLARIFICATION]` markers remain in `spec.md`'s Technical Context — the genuinely open points
from `docs/FEATURE_PLAN.md` (run-closing trigger, backfill ceiling default, `priority` field usage,
capability tie-break) were resolved as documented defaults in the spec's own Assumptions section. This
document instead records the non-trivial *implementation-approach* decisions, grounded in a direct
codebase audit of Phases 0–6 (Printers, Parts, FilamentTracking, Assemblies) rather than the spec — this
feature has more integration surface with existing code than any prior phase, so getting the seams right
matters more here than usual.

## R1 — Unify Part-target and Assembly-target RunLines through one part-demand resolution step

**Decision**: A RunLine targeting an Assembly resolves its printed-part demand by calling the existing
`IAssemblyGraphQueryService.ExplodeAsync(organizationId, assemblyId, quantity, ct)` (`013-assembly-bom`)
and taking only `BomExplosionResult.PrintedParts` — `NonPrintedComponents` is discarded (FR-004a). A
RunLine targeting a Part directly synthesizes an equivalent single-entry
`IReadOnlyList<ExplodedPartQuantity>` — `[ new(partId, partName, quantity) ]` — with no CTE call at all,
since a Part has no BOM to explode. Both paths converge on the identical
`IReadOnlyList<ExplodedPartQuantity>` shape before `PartRequirement` rows are created, so requirement
creation (FR-004) is one uniform code path regardless of which kind of RunLine produced it.

**Rationale**: Phase 6's explosion function is already the constitution-mandated, CTE-backed,
performance-proven mechanism for this exact multiply-and-aggregate operation (`013-assembly-bom`
`research.md` R1) — reusing it directly rather than re-deriving BOM traversal is both less code and
avoids a second implementation of quantity aggregation that could drift from the first. For a direct Part
RunLine, the "explosion" is trivial by construction (one part, quantity 1:1), so a real DB round-trip
would be pure overhead; a plain in-memory synthesis keeps the fast path fast without a special case
leaking into the requirement-creation logic downstream.

**Alternative rejected**: A parallel `PartOnlyExplosionResult` type or an `IRunLineResolver` interface
hierarchy — unnecessary ceremony for what is a two-line guard clause (`if line.TargetKind == Assembly then
ExplodeAsync else synthesize`) inside one command handler.

## R2 — Whole-plate `PrintJob` creation: eager for finite lines, lazy for backfill

**Decision**: `Part` gains `UnitsPerPrint` (`int`, default 1, spec FR-005a — this feature's first
amendment to the Phase 4 entity). `PlatesNeeded = ceil(PartRequirement.Quantity / part.UnitsPerPrint)`.
For a **finite** `PartRequirement`, `CreateProductionRunCommandHandler` creates all `PlatesNeeded`
`PrintJob` rows synchronously, in the same transaction as the Run/RunLine/PartRequirement rows, every one
`Status = Queued` (FR-005b). For an **unlimited (backfill)** RunLine, zero `PrintJob` rows are created at
run-creation time — the dispatcher (R4) creates exactly one job, lazily, the moment a matching idle
printer is actually about to be offered work from that line (FR-006a).

**Rationale**: Directly implements FR-005/FR-005b/FR-006a. Eager creation for finite lines is what makes
FR-019/SC-005 ("a queue of 1,000+ jobs remains browsable") a real, testable claim rather than something
that only becomes true once every plate has been individually dispatched — the whole queue is real rows
from the moment the run is created. `PartRequirement.Quantity` (from R1) is `decimal` (BOM lines admit
fractional multipliers for non-printed components; printed-part quantities are always whole numbers by
construction — Phase 6 FR-004a — so the ceiling division has no real fractional edge case in practice,
asserted in Assumptions below).

## R3 — Queue ordering: a fractional `QueuePosition`, not row-insertion order or a linked list

**Decision**: `PrintJob.QueuePosition` is a `double precision` column. New eager jobs get consecutive
integer-valued positions (`1024, 2048, 3072, ...` — pre-spaced, not `1, 2, 3`) at creation. Reordering
(FR-008) updates **only the moved job's own row**, setting its `QueuePosition` to the arithmetic mean of
its new neighbors' positions (or `neighbor ± 1024` at either end of the queue) — a classic fractional-index
reorder. The dispatcher and every list/browse query simply `ORDER BY "QueuePosition"`.

**Rationale**: SC-005 requires the queue to "remain browsable... as it grows," and FR-008 requires manual
reordering to be a normal, frequent operator action — a full-table renumber on every drag-and-drop
reorder would make both worse as the queue grows, exactly the wrong direction. A fractional position makes
a reorder a single-row `UPDATE`, independent of queue size. Pre-spacing new jobs' initial positions leaves
headroom for many reorders before floating-point precision between two adjacent values becomes a practical
concern; if it ever does, a background renumber pass is a self-contained future fix that touches no other
part of this design (not built now — no evidence yet that it's needed, consistent with constitution
Principle IV's "no preemptive abstraction").

**Alternative rejected**: A doubly-linked list (`PreviousJobId`/`NextJobId`) — cheap single-item moves too,
but every ordered read (the queue list, the dispatcher's "topmost eligible job" scan) becomes a traversal
rather than an indexed `ORDER BY`, which fights directly against TanStack Virtual's need for a stable,
directly-sliceable, sorted collection (constitution Principle V).

## R4 — Dispatch trigger and matching: a new `ProductionDispatchBackgroundService`

**Decision**: A new `BackgroundService` (`Infrastructure.Production`), shaped identically to the two
existing ones audited in this codebase (`PrinterConnectionBackgroundService`,
`PartProcessingBackgroundService`): a bounded `Channel<Guid>` of printer ids (capacity matching the
existing 100/`FullMode.Wait` convention), consumed by a small worker pool, behind a narrow
`IProductionDispatcher` interface (`void RequestDispatchAttempt(Guid printerId)`) — the same
"concrete singleton registered three ways" pattern (`AddSingleton` + `AddHostedService` +
`AddSingleton<IProductionDispatcher>`) already used for `IPartProcessingDispatcher`.

**Trigger points** — every place a printer can reach `Idle`:
1. `MarkPrinterReadyCommandHandler` (Phase 5), immediately after `printer.TransitionTo(PrinterStatus.Idle)`
   succeeds.
2. `PrinterConnectionBackgroundService.HandleEventAsync`, whenever `printer.ObserveStatus(gcodeState)`
   returns `true` **and** the printer's new status is `Idle` (a printer can observe its way directly to
   Idle from live telemetry, not only via the manual Mark-ready transition — confirmed in
   `PrinterStatusTransitions.ObserveStatus`).

**Per-signal matching algorithm** (run inside a per-organization tenant scope, mirroring the existing
`EnsureTenantInfoResolvedAsync` idiom both existing background services already use):
1. Resolve printer P's nozzle diameter for *this one printer* via `P.MachineProfileId` →
   `IProfileBundleParser.ExtractMachineDisplayFields(profile.RawDataJson, ...).NozzleSizeMm` (the
   per-printer read `NozzleDiameterResolver` itself doesn't expose — that helper aggregates across the
   *whole fleet* for Phase 4's farm-wide default; this feature needs one printer's own value, so it reads
   the same `IProfileBundleParser` call directly rather than reusing `NozzleDiameterResolver` verbatim). A
   printer with no linked `MachineProfile` (or one with no parseable nozzle size) matches nothing and is
   skipped — mirrors Phase 4's own "not a real fleet member" language for the same condition.
2. Resolve P's currently loaded material(s)/color(s) by joining `Spool` (`FeedSlotId` set, `State ==
   Using`) → `FeedSlot` → `FeedUnit` where `FeedUnit.PrinterId == P.Id`, reading each matched spool's
   `FilamentVariantId` → material/color. (`Printer.AmsUnits[].Trays[].MaterialType/ColorHex` — the
   connector-reported truth — is the fallback when no operator-tracked `Spool` is assigned to a slot; see
   Assumptions.)
3. Query `PrintJob` rows `WHERE OrganizationId = @org AND Status = 'Queued' AND AssignmentMode =
   'Unassigned'`, joined to `PartRequirement` → `Part` for nozzle diameter and filament-requirement match,
   `ORDER BY QueuePosition`, take the first match (FR-008: topmost eligible, not topmost overall).
4. If none, query `Pinned` jobs `WHERE PinnedPrinterId = P.Id AND Status = 'Queued'`, take the topmost by
   `QueuePosition` (FR-007c — a pinned job is never subject to the nozzle/material match check against a
   *different* printer, only ever considered against its own pinned target).
5. If none, evaluate backfill (unlimited) RunLines eligible for P (same nozzle/material match, plus R2's
   lazy-creation + the occupancy-cap/ceiling checks below), create and dispatch one job (FR-006a/b).
6. If a job was found in step 3 or 4, or created in step 5, dispatch it (R5).

**Backfill occupancy cap / cumulative ceiling** (FR-006c/d): "how many printers a line currently occupies"
is computed as `COUNT(DISTINCT PrinterId)` over that RunLine's own `PrintJob` rows in any non-terminal,
printer-assigned state (`Printing`, or `Queued` with a printer already resolved via `AssignmentMode =
Pinned`/an already-dispatched-but-not-yet-started job — see R5's `DispatchedAtUtc` sub-state) — no
separate counter column, always derived from the jobs that already exist. "Cumulative units" toward an
optional ceiling is `SUM(Part.UnitsPerPrint)` over that RunLine's `Completed` jobs — a coarse count, not a
true good-unit count, since per-unit outcome tracking (good/defect/failed) doesn't exist until Phase 8
(FR-023's boundary); documented as a known approximation in Assumptions.

**Startup reconciliation** (constitution's Durable Recovery guardrail — both existing background services
already implement this exact shape): on boot, before consuming, (a) any `PrintJob` left in the transient
"dispatch in flight" state from a prior process lifetime (R5's `DispatchedAtUtc`-set-but-unconfirmed
window) is reverted to plain `Queued`/`Unassigned` so it re-enters ordinary matching rather than being
silently stuck; (b) every currently-`Idle` printer gets a dispatch attempt queued immediately, so a
process restart with idle printers and a non-empty queue self-heals without waiting for a live status
transition to happen to fire naturally.

**Rationale**: this is the single largest new piece of orchestration this feature introduces, and it has
direct, close prior art in this exact codebase (two background services doing the same
channel-plus-reconciliation shape for a different resource) — following that precedent exactly, rather
than inventing a new shape, is both less risky and satisfies the constitution's guardrail by construction
instead of by a separate audit later. The matching *algorithm* itself (steps 1–6, minus the DB
round-trips) is written as a pure, injected-nothing function over already-loaded candidate data
specifically so it's unit-testable (constitution Principle II) independently of the background service's
I/O shell — mirroring how `PartProcessingBackgroundService` keeps its own deterministic pieces
(`FilamentRequirementGrouper`, etc.) separable from its orchestration loop.

**Scale note (SC-005)**: matching filters via an indexed SQL `WHERE`/`JOIN` (on `Status`,
`AssignmentMode`, `PartRequirement.PartId` → `Part.NozzleDiameterMm`), not an in-memory scan of the whole
queue — the existing precedent (`AssemblyGraphQueryService`) already establishes that a heavy
traversal-shaped query belongs at the DB layer, not pulled into the application. No separate materialized
"candidate jobs per printer" view is introduced now — nothing in this feature's scale (a farm's own job
queue, not a shared marketplace) suggests today's straightforward indexed query needs it, and adding one
speculatively is exactly what constitution Principle IV rules out.

## R5 — Extending `IPrinterConnector` to push a file, start, pause, resume, and stop a print

**Decision**: `IPrinterConnector` gains four new methods, not one — start, pause, resume, and stop are all
outbound commands over the same connection, so they're added together rather than start now and the rest
deferred (spec Clarifications, 2026-08-07 reversed the original "OpenFarm never sends a stop command"
rule for the single-job case):

```csharp
Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, CancellationToken cancellationToken);
Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken);
Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken);
Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken);
```

`BambuLanConnector` implements `StartJobAsync` as an FTPS upload (port 990, per `docs/FEATURE_PLAN.md`'s
own Phase 1 risk note: "FTPS file transfer runs on port 990 and becomes relevant in Phase 7 ... not this
phase" — confirming this integration point was always intended to land here) followed by an MQTT
print-start command; `PauseJobAsync`/`ResumeJobAsync`/`StopJobAsync` are each a single MQTT publish (the
Bambu protocol's own pause/resume/stop print commands), per the vendored `third_party/printers/bambulab/`
reference (M0.0) — no FTPS involved for these three, only the initial file push needs it.
`SimulatorPrinterConnector` implements fake versions consistent with its existing role: `StartJobAsync`
begins emitting a `Printing → Finish` telemetry sequence through its already-existing `ConnectAsync`
stream; `PauseJobAsync`/`ResumeJobAsync` flip that sequence's own internal paused flag;
`StopJobAsync` makes the sequence emit a stopped/terminal state on its next tick — all with no real
network call.

For a printer whose brand has no live integration (FR-012/FR-024b's manual-brand branch), none of these
four methods is ever called — the dispatcher marks a job dispatched-but-not-started
(`DispatchedAtUtc` set, `Status` stays `Queued`) and lets the operator manually mark it
started/paused/resumed/cancelled through the API with no connector call behind any of those transitions,
mirroring the existing manual-start precedent uniformly across all four actions.

**Rationale**: `IPrinterConnector` is the constitution-named swappable boundary for printer connectivity
(Principle I/IV) — extending the existing interface, rather than introducing a parallel
`IPrinterJobDispatchClient`, keeps that single boundary intact so a future brand (Phase 15) or transport
(the local-agent relay, Phase 23) implements one enlarged contract, not four/five contracts that all need
implementing per brand. `VerifyAsync`/`ConnectAsync` (read-only, already used since Phase 1) are untouched;
all four new methods are additive.

**Command-vs-connection scope note (a genuine open verification risk, not resolved here)**: whether Bambu's
LAN MQTT broker accepts a second, short-lived client connection issuing a command while
`PrinterConnectionBackgroundService` already holds a long-lived streaming connection open for the same
printer, or whether these commands need to be published on that *same* client connection instead, is a
real protocol question that can only be settled against physical hardware — flagged here for `tasks.md`
and manual verification, the same "unofficial, unversioned MQTT access" risk already called out in
`docs/FEATURE_PLAN.md`'s own Risks/notes for Phase 1. The interface above is written the simpler,
stateless way (open, command, close, matching `VerifyAsync`'s own existing shape) as the starting
assumption; if hardware testing shows the broker rejects concurrent connections, the fix is
`BambuLanConnector` internally routing these four calls through whatever connection
`PrinterConnectionBackgroundService` already has open — an internal implementation change, not an
`IPrinterConnector` contract change.

## R6 — Content-addressed file revisions: `Part` gains a revision identity; replace stops deleting eagerly

**Decision**: `Part` gains two fields — `RevisionHash` (`string`, SHA-256 hex of the file's bytes,
computed once when the file is confirmed/replaced) and `RevisionNumber` (`int`, starts at 1, incremented
on every `ReplaceFile`) — this feature's second amendment to the Phase 4 entity (FR-009c). `Part`'s
storage path convention changes from the current owner-GUID-keyed scheme
(`{organizationId}/{ownerId}{extension}`, `LocalDiskPartFileStorage`) to a **content-addressed** one —
`{organizationId}/{revisionHash}{extension}` — so two uploads that happen to produce byte-identical
content naturally collide onto the same stored path rather than duplicating (FR-009d). Critically,
`Part.ReplaceFile`'s current behavior of unconditionally deleting the old stored file
(`IPartFileStorage.DeleteAsync` on the pre-replace `StoragePath`) is **removed**: the old revision's bytes
are left in place, addressable by their own hash, for as long as any `PrintJob` still references that
`RevisionHash` under Snapshot mode.

`PrintJob.FileRevisionHash`/`FileRevisionNumber` are copied from the owning Part at the moment that
matters for the line's chosen mode (FR-009): under **Snapshot**, at the moment the job is created (eager,
at run creation, or lazily for a backfill job); under **Follow-latest**, at the moment of actual dispatch
(R4/R5's dispatch step reads `Part.RevisionHash` immediately before calling `StartJobAsync`, never
earlier).

**Rationale**: this is a real, necessary behavior change to Phase 4's file-replacement semantics, not an
additive-only feature — flagged explicitly so it isn't mistaken for a design gap during implementation. Without it, FR-009's Snapshot mode is unimplementable the moment a Follow-latest-adjacent replace happens
elsewhere in the same farm: the old bytes a Snapshot job still needs would already be gone. Garbage-
collecting a revision's bytes once genuinely nothing references it (no Snapshot job, no Part row currently
pointing at it) is explicitly **not** built in this phase — `docs/FEATURE_PLAN.md`'s own words, "full
version history is not implied and stays out of scope," are read here as license to let retained-but-
unreferenced revision files accumulate on disk for now rather than build a reference-counting reaper; this
is recorded as a deferred cleanup concern in Assumptions, not silently ignored.

## R7 — Job filament-consumption breakdown reuses `PartFilamentRequirement`'s `(MaterialType,
DetectedColorHex)` shape

**Decision**: `PrintJob` owns a small collection, `JobFilamentConsumption` — one row per distinct
`(MaterialType, DetectedColorHex)` pair, `SpoolId` (nullable — set once a specific spool is actually
matched at dispatch), and `GramsConsumed` (nullable until the job closes). Defaulted at dispatch time by
copying `Part.FilamentRequirements`' distinct pairs (FR-013b) onto the job; `GramsConsumed` is filled in
when the job reaches a terminal state — from live telemetry for a connected printer, or operator-entered
for a manual-brand one — and is recorded even for a job that fails partway through, proportional to what
was actually consumed (FR-013a). Recording it decrements the matched `Spool.RecordUsage(gramsConsumed)`
(Phase 3), the same call the constitution's filament-weight-tracking math already governs.

**Rationale**: reuses Phase 4's already-established `(MaterialType, DetectedColorHex)` vocabulary rather
than inventing a second one, and routes the actual weight decrement through `Spool`'s existing, already
constitution-mandated TDD'd method rather than a parallel ad hoc update.

## R8 — Snapshot boundary: `PartRequirement` never re-reads the live BOM graph

**Decision**: A `PartRequirement` row, once created (R1), carries no reference back to the source
`Assembly`/`BomLine` graph beyond its owning `RunLineId` — it is pure data (`PartId`, `Quantity`), not a
pointer that gets re-resolved. This is what makes FR-003's "an edit to the Assembly's BOM afterward MUST
NOT alter an already-created run's requirements" true by construction rather than by a rule someone has to
remember to enforce: there is nothing left in the persisted shape that *could* re-read the BOM.

## R9 — Status/mode enums: `HasConversion<string>()`, matching the codebase's existing convention

**Decision**: `ProductionRun.Status`, `PrintJob.Status`, `PrintJob.AssignmentMode`, and `RunLine.RevisionMode`
(Snapshot/FollowLatest) are all mapped with `.HasConversion<string>()` — confirmed as the actual
established convention in this codebase (`PrinterConfiguration.cs`'s `Status` mapping, `PartConfiguration.cs`'s
`FileFormat`/`EstimateStatus`/`SlicerEngineOverride` mappings, `BomLineConfiguration`'s `TargetKind`), not a
native Postgres `CREATE TYPE ... AS ENUM`. The constitution's "`printers.status` and `jobs.status` are
Postgres enums with explicit state machines enforced in Domain" is satisfied by this string-backed
convention plus Domain-layer transition guards (mirroring `PrinterStatusTransitions`), exactly as
`PrinterStatus` itself already demonstrates — not by a literal native enum type, which nothing in this
codebase uses anywhere today.

## R10 — Printer tags: not modeled yet, matching narrowed accordingly

**Finding**: no `PrinterTag`/tag-on-printer concept exists anywhere in the current codebase — a direct
grep across `Printer.cs` and every feature spec through `013-assembly-bom` confirms it. `docs/FEATURE_PLAN.md`'s
Phase 5 ("Printer tags... location, capability, farm zone") describes intended future scope, not
already-shipped schema.

**Decision**: FR-007a's dispatcher matching in this phase is scoped to nozzle diameter and currently
loaded material/color only — the two criteria the codebase can actually evaluate today. Tag-based matching
is additive groundwork for whenever a future phase introduces printer tags: the matching algorithm (R4) is
written as a filter pipeline (nozzle → material/color → [future: tags]) specifically so a tag predicate
slots in as one more filter stage without restructuring the dispatcher, not as a hard-coded two-criteria
check.

## R11 — "Least-capable qualifying printer" tie-break: a coarse, available-today proxy

**Decision**: when more than one idle printer qualifies for the same job (FR-007b), the dispatcher orders
candidates by `Printer.AmsUnits.Sum(u => u.Trays.Count)` ascending (fewer total loaded material slots =
treated as the simpler/less versatile machine) and picks the first; a full tie falls back to `Printer.Id`
ordering for determinism.

**Rationale**: this is explicitly named in the spec's own Assumptions as "a cheap heuristic, not real
optimization" (`docs/FEATURE_PLAN.md`: "whole-queue optimization is Phase 12's job"), and total AMS/tray
count is the one capability signal already present on every printer row today (`Printer.AmsUnits`) without
depending on a linked `MachineProfile` being present or parseable — using it keeps the tie-break from
silently no-op'ing (falling through to the arbitrary `Printer.Id` case) for the common farm that hasn't
imported machine profiles for every printer yet.

## R12 — Remote pause/resume/cancel: command is a request, telemetry is the only writer of job status

**Finding (unchanged from the original design)**: `Printer`'s own status machine has no `Paused` member at
all — confirmed directly in `PrinterStatusTransitions.ObserveStatus`, which maps a Bambu `PAUSE` gcode
state to `PrinterStatus.Printing`, the same coarse status as ordinary running. `Printer` was deliberately
kept to three states (`Idle` / `Printing` / `AwaitingClear`) in Phase 1, and this feature doesn't change
that — a job can be `Paused` while its printer's own status stays `Printing`.

**Decision (revised — spec Clarifications, 2026-08-07)**: the original "telemetry-only, no command" version
of this decision is superseded. `PrintJob`'s pause/resume/cancel-while-printing transitions are now
reachable two ways, and the split matters:

1. **Command handlers never optimistically change job status.** `PausePrintJobCommandHandler`/
   `ResumePrintJobCommandHandler`/`CancelPrintJobCommandHandler`, for a job on a live-integrated printer,
   call the corresponding new `IPrinterConnector` method (R5) — `PauseJobAsync`/`ResumeJobAsync`/
   `StopJobAsync` — and, for cancel specifically, stamp `PrintJob.CancelRequestedAtUtc = now` (FR-024a/c).
   None of them call `PrintJob.MarkPaused()`/`MarkResumed()`/`MarkCancelled()` directly. A command that
   fails to send (connector throws, printer unreachable) surfaces as a normal error response — the job's
   status is simply untouched, exactly as if nothing had been clicked.
2. **`IJobTelemetrySync`** (`Application.Production`, implemented in `Infrastructure.Production` —
   renamed and generalized from the earlier `IJobPauseTelemetrySync`, now the single place every
   telemetry-observable `PrintJob` transition lives, not only pause) is the *only* caller of
   `PrintJob.MarkStarted()`/`MarkPaused()`/`MarkResumed()`/`MarkCompleted()`/`MarkFailed()`/
   `MarkCancelled()` for a live-integrated job. Invoked from `PrinterConnectionBackgroundService.
   HandleEventAsync`, alongside its existing `IFeedSlotTelemetrySync` call, on every event: it looks up
   whichever `PrintJob` currently has `DispatchedPrinterId` equal to this printer and a non-terminal
   status, compares the raw `gcodeState` against its current `PrintJob.Status`, and drives exactly one
   transition when the two disagree (started, paused, resumed, or a terminal state reached). This is what
   makes FR-024a's guarantee real: a command is a *request*, the printer's own reported reality is the
   *only* thing that ever writes `PrintJob.Status`, so a dropped or unacknowledged command simply never
   shows up as a status change — there's nothing to roll back because nothing was optimistically set.

**Cancelled-vs-Failed disambiguation (FR-024c)**: when `IJobTelemetrySync` observes a terminal state for a
job:
- If `PrintJob.CancelRequestedAtUtc` is set (OpenFarm itself sent the stop command), the terminal state is
  always resolved as `Cancelled` — OpenFarm knows its own intent regardless of exactly which terminal
  gcode state or error code the printer reports back.
- If it is *not* set — the case FR-024c also has to cover: **a stop performed directly at the printer**,
  bypassing OpenFarm entirely — resolution depends on whether the printer's own telemetry carries a signal
  that distinguishes a user-initiated stop from a genuine fault (spec Assumptions: this is a real,
  unresolved technical question pending verification against the vendored Bambu protocol reference and
  real hardware, not settled by this document). If no such signal exists, the documented fallback is
  `Failed` — an unexplained terminal state with no known stop request is never speculatively reclassified
  as `Cancelled`.

**Manual-brand path (FR-024b)**: none of the above applies — there is no connector, so no command and no
telemetry. `PausePrintJobCommandHandler`/etc. call the `PrintJob.Mark*()` methods **directly** for a
manual-brand job, trusting the operator's own report, exactly like the existing manual-start precedent
(FR-012).

**Rationale**: this single-writer split (command requests, telemetry alone confirms) is what "sync very
carefully" concretely means for this feature — without it, an operator clicking Pause and the printer
being paused via its own touchscreen at the same moment could race two independent writers into a
consistent-looking but wrong state. With it, there is exactly one place `PrintJob.Status` for a
live-integrated job ever changes, regardless of which of the two triggered it, which is also what makes a
stop performed directly at the printer converge to the same `Cancelled` state a stop requested through
OpenFarm does (spec Clarifications) — both paths end up observed by the same `IJobTelemetrySync` call, not
two different code paths that could disagree.

**Cross-reference to the `IFeedSlotTelemetrySync` precedent**: `PrinterConnectionBackgroundService` already
fans a single telemetry event out to more than one feature-specific sync interface (today: the printer's
own entity update, then `IFeedSlotTelemetrySync` for FilamentTracking). `IJobTelemetrySync` is a third
fan-out target of the same event, not a new mechanism — `HandleEventAsync` gains one more injected
dependency and one more call in its existing sequence, nothing structurally new.

## R13 — Shortfall & requeue: a per-job replacement link, not aggregate requirement math

**Decision**: a `PrintJob` that reaches `Cancelled` or `Failed` while belonging to a *finite*
`PartRequirement` gains a nullable `RequeuedAsJobId` (spec Clarifications, 2026-08-07 — FR-025). A new
operator action, `POST /api/v1/print-jobs/{id}/requeue`, is only valid on a job that is `Cancelled` or
`Failed` and has `RequeuedAsJobId == null`; it creates exactly **one** new `PrintJob` for the same
`PartRequirementId` (same creation rules as any other job on that line — R2's whole-plate math, R6's
Snapshot/Follow-latest revision resolution, the owning `RunLine`'s default assignment mode — this is not a
special code path, it reuses ordinary job creation), appends it to the end of the queue
(`QueuePosition` = current max + 1024, R3's own spacing convention), and sets the original job's
`RequeuedAsJobId` to the new job's id. Whether a requirement is "short" is never stored or computed as an
aggregate number — it's simply: does a `Cancelled`/`Failed` job for this requirement exist with
`RequeuedAsJobId == null`? Each one, independently, either has been requeued or hasn't.

**Rationale**: the spec's chosen answer (Clarifications, option B) requires exactly one thing: a shortfall
must never resolve itself silently, and the operator must take an explicit action to fix it. A one-to-one
"this specific lost job → its specific replacement" link satisfies that with no new math at all — no
running total to keep consistent, no risk of double-counting a lost job that's already been compensated
for, and the query for "does this requirement need attention" (`RunDetailView`/`PartRequirementSummary`,
`data-model.md`) is just "any job on this requirement in a terminal non-completing status with no
requeue link" — a plain filter, not a computed aggregate. This also composes cleanly with FR-016a's
existing "satisfied" definition (`Completed` jobs' combined output meets the target) without needing a
separate "shortfall" field on `PartRequirement` itself to keep in sync.

**Scope boundary (FR-025d)**: this applies only to finite requirements. An unlimited/backfill line's
cancelled or failed job needs no replacement link at all — R2's own lazy, one-plate-at-a-time creation
already produces the next backfill job whenever a printer needs one, so nothing here even runs for a job
whose owning `PartRequirement.Quantity` is `null`.

**Explicitly not built (spec Assumptions)**: reducing a `RunLine`'s target quantity as an alternative to
requeueing. Reconciling already-eagerly-created jobs against a lowered target is real, separate design
work (which of them get cancelled, and does that cancellation itself produce a shortfall needing another
decision) that the spec's own Assumptions section defers rather than folding into this phase.

## Summary of what's genuinely new (no prior art in this codebase)

1. **`ProductionDispatchBackgroundService`** (R4) — the third `BackgroundService` in this codebase,
   following the established bounded-channel + reconciliation shape but the first to *write* connector
   commands rather than only *read* connector-reported telemetry.
2. **`IPrinterConnector.StartJobAsync`/`PauseJobAsync`/`ResumeJobAsync`/`StopJobAsync`** (R5) — this
   codebase's first outbound printer commands of any kind (file push, print start, pause, resume, stop);
   everything through Phase 6 only ever reads `IPrinterConnector`'s telemetry stream.
3. **Content-addressed Part file storage + non-destructive replace** (R6) — a real behavior change to
   Phase 4's existing "replace is always destructive" semantics, not purely additive; called out here so
   it isn't missed during implementation of `ReplacePartFileCommandHandler`.
4. **Fractional `QueuePosition` reordering** (R3) — this codebase's first fractional-index column; every
   prior ordered list (Assembly lines, Part tags) has used natural/insertion order with no reorder
   operation at all.
5. **`IJobTelemetrySync`** (R12) — a third fan-out target for `PrinterConnectionBackgroundService`'s
   already-existing telemetry-event loop, alongside the existing `IFeedSlotTelemetrySync`, and the single
   writer of every telemetry-observable `PrintJob` transition (start, pause, resume, and — combined with
   `CancelRequestedAtUtc` — the Cancelled-vs-Failed disambiguation) for a live-integrated job.
6. **`PrintJob.RequeuedAsJobId`** (R13) — a one-to-one "lost job → its replacement" link, this codebase's
   first modeling of a shortfall/requeue relationship of any kind.
