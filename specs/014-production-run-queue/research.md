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

## R5 — Extending `IPrinterConnector` to push a file and start a print

**Decision**: `IPrinterConnector` gains one new method:

```csharp
Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, CancellationToken cancellationToken);
```

`BambuLanConnector` implements it as an FTPS upload (port 990, per `docs/FEATURE_PLAN.md`'s own Phase 1
risk note: "FTPS file transfer runs on port 990 and becomes relevant in Phase 7 ... not this phase" —
confirming this integration point was always intended to land here, not a gap in Phase 1) followed by an
MQTT print-start command, per the vendored `third_party/printers/bambulab/` protocol reference (M0.0).
`SimulatorPrinterConnector` implements a fake version consistent with its existing role: it immediately
begins emitting a `Printing → Finish` telemetry sequence through its already-existing `ConnectAsync`
stream, with no real file transfer.

For a printer whose brand has no live integration (FR-012's manual-brand branch), `StartJobAsync` is never
called at all — the dispatcher instead marks the job dispatched-but-not-started (`DispatchedAtUtc` set,
`Status` stays `Queued`) and a new `POST /api/v1/print-jobs/{id}/mark-started` endpoint lets the operator
transition it to `Printing` themselves once they've retrieved the file and started the print physically.

**Rationale**: `IPrinterConnector` is the constitution-named swappable boundary for printer connectivity
(Principle I/IV) — extending the existing interface, rather than introducing a parallel
`IPrinterJobDispatchClient`, keeps that single boundary intact so a future brand (Phase 15) or transport
(the local-agent relay, Phase 23) implements one enlarged contract, not two contracts that both need
implementing per brand. `VerifyAsync`/`ConnectAsync` (read-only, already used since Phase 1) are untouched;
`StartJobAsync` is additive.

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

## R12 — `PrintJob.Paused` is a display-only, telemetry-driven state — no pause/resume command exists

**Finding**: `Printer`'s own status machine has no `Paused` member at all — confirmed directly in
`PrinterStatusTransitions.ObserveStatus`, which maps a Bambu `PAUSE` gcode state to `PrinterStatus.Printing`,
the same coarse status as ordinary running. `Printer` was deliberately kept to three states (`Idle` /
`Printing` / `AwaitingClear`) in Phase 1 — `Paused` was never modeled at the printer level, and this
feature does not change that.

**Decision**: `PrintJob.Status` keeps `Paused` (the spec's own job workflow names it), but it is **never
reachable through the API** — there is no pause/resume endpoint, and no `Application` command calls
`PrintJob.MarkPaused()`/`MarkResumed()`. The only caller is a new `IJobPauseTelemetrySync`
(`Application.Production`, implemented in `Infrastructure.Production`), invoked directly from
`PrinterConnectionBackgroundService.HandleEventAsync` alongside its existing `IFeedSlotTelemetrySync` call:
after applying `printer.ObserveStatus(gcodeState)` (which, per the finding above, does not distinguish
pause), the raw `gcodeState` is checked a second time — if it equals the pause sentinel and a `PrintJob` is
currently `DispatchedPrinterId == this printer` with `Status == Printing`, call `MarkPaused()`; if it
returns to a running state and the job is `Paused`, call `MarkResumed()`. Both changes publish through the
existing `IProductionEventPublisher` → `FarmHub` path (constitution Principle III), the same as every other
job status change. A manual-brand (non-live) job has no telemetry to observe at all, so it simply never
enters `Paused` — it stays `Printing` until a terminal transition, which is an accurate reflection of what
OpenFarm can actually know about it, not a gap.

**Rationale**: this follows the same design rule FR-017 already states explicitly for stopping a print
("OpenFarm never sends a stop command on the operator's behalf... the operator standing at the machine is
better placed to judge") — pause and resume are physical/app-level actions on the printer itself, not
something this feature should expose as a remote command. Keeping `Paused` as a *state* (so the queue can
display it) while removing any *command* that reaches it keeps the useful half (an operator glancing at
the queue can see a job is paused) without adding API surface that contradicts the rest of this feature's
"observe, never command" posture for anything except the one command genuinely new to this phase
(`StartJobAsync`, R5 — starting a print is the one physical action OpenFarm does initiate, precisely
because nothing else can: a queued job cannot start itself the way a human can pause one).

**Cross-reference to the `IFeedSlotTelemetrySync` precedent**: `PrinterConnectionBackgroundService` already
fans a single telemetry event out to more than one feature-specific sync interface (today: the printer's
own entity update, then `IFeedSlotTelemetrySync` for FilamentTracking). `IJobPauseTelemetrySync` is a third
fan-out target of the same event, not a new mechanism — `HandleEventAsync` gains one more injected
dependency and one more call in its existing sequence, nothing structurally new.

## Summary of what's genuinely new (no prior art in this codebase)

1. **`ProductionDispatchBackgroundService`** (R4) — the third `BackgroundService` in this codebase,
   following the established bounded-channel + reconciliation shape but the first to *write* connector
   commands rather than only *read* connector-reported telemetry.
2. **`IPrinterConnector.StartJobAsync`** (R5) — this codebase's first outbound printer command (file push +
   print start); everything through Phase 6 only ever reads `IPrinterConnector`'s telemetry stream.
3. **Content-addressed Part file storage + non-destructive replace** (R6) — a real behavior change to
   Phase 4's existing "replace is always destructive" semantics, not purely additive; called out here so
   it isn't missed during implementation of `ReplacePartFileCommandHandler`.
4. **Fractional `QueuePosition` reordering** (R3) — this codebase's first fractional-index column; every
   prior ordered list (Assembly lines, Part tags) has used natural/insertion order with no reorder
   operation at all.
5. **`IJobPauseTelemetrySync`** (R12) — a third fan-out target for `PrinterConnectionBackgroundService`'s
   already-existing telemetry-event loop, alongside the existing `IFeedSlotTelemetrySync` — not a new
   mechanism, but the first time a *second* feature's background service reaches into `Production` this way.
