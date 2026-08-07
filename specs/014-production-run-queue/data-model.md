# Data Model: Production runs, jobs & print queue

**Feature**: `014-production-run-queue` | **Date**: 2026-08-07

## Entities

### `ProductionRun` (new entity, `OpenFarm.Domain.Production`)

The demand container (spec Key Entities). `Name`/`DueDate`/`Priority` are all optional display metadata —
none of the three feed the dispatcher (FR-008a, `research.md` R3).

```csharp
public enum ProductionRunStatus { Active, Complete, Closed, Cancelled }

public sealed class ProductionRun
{
    private readonly List<RunLine> _lines = [];

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public string? Name { get; private set; }              // FR-001a — null is a fully valid, fully usable run
    public DateOnly? DueDate { get; private set; }          // display-only (spec Assumptions)
    public string? Priority { get; private set; }           // display-only (spec Assumptions)
    public string? Notes { get; private set; }
    public ProductionRunStatus Status { get; private set; }
    public IReadOnlyList<RunLine> Lines => _lines;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ProductionRun Create(Guid organizationId, string? name, DateOnly? dueDate, string? priority, string? notes, IReadOnlyList<RunLineSpec> lines);
    public void MarkComplete();     // FR-016a — every finite requirement satisfied; throws if any is not
    public void Close();            // spec Assumptions — explicit operator action, the only path to the terminal state
    public void Cancel();           // FR-017 — Active/Complete -> Cancelled
}
```

- **`MarkComplete` is not terminal** (FR-016c) — a completed run's own status can still be read by Phase 8
  as "not yet closed," and nothing in this entity prevents a later phase from reopening it; there is no
  `Reopen()` method here because nothing in *this* phase ever calls one (Phase 8 owns that behavior once
  outcome lines exist).
- **A run created with `Name = null`** (FR-001a) behaves identically to a named run everywhere in this
  model — the field is nullable specifically so "unnamed" needs no sentinel value and no second entity
  shape. List views derive a display label from `Lines` (first line's target name + count) when `Name` is
  null; that derivation is a read-side concern (see below), not stored.
- **`RequiresConfirmation`** (FR-016b — a named run's last job asks for confirmation before completing) is
  computed, not stored: `Name is not null`. `MarkComplete` itself is always the same call; the confirmation
  prompt is a frontend gate in front of calling it for a named run, never a second server-side flow.

### `RunLine` (new entity, `OpenFarm.Domain.Production`)

One requested thing on a run (spec Key Entities / FR-002) — a Part or an Assembly, never both, plus either
a finite quantity or `null` for unlimited (backfill).

```csharp
public enum RunLineTargetKind { Part, Assembly }
public enum RevisionMode { Snapshot, FollowLatest }

public sealed class RunLine
{
    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public Guid ProductionRunId { get; }
    public RunLineTargetKind TargetKind { get; }
    public Guid TargetId { get; }                          // Part.Id or Assembly.Id
    public int? Quantity { get; }                          // null => unlimited/backfill (FR-006)
    public RevisionMode RevisionMode { get; private set; }
    public AssignmentMode DefaultAssignmentMode { get; private set; }   // applied to every job created under this line
    public Guid? DefaultPinnedPrinterId { get; private set; }           // set iff DefaultAssignmentMode == Pinned
    public int BackfillPrinterCap { get; private set; }                 // meaningful only when Quantity is null; default 1 (FR-006c)
    public int? BackfillCumulativeUnitCeiling { get; private set; }     // meaningful only when Quantity is null; null = no ceiling (FR-006d, spec Assumptions: off by default)

    internal static RunLine Create(Guid organizationId, Guid productionRunId, RunLineSpec spec);
    public void SetRevisionMode(RevisionMode mode);                    // FR-009b — editable after queueing, affects only future plates
    public void SetBackfillLimits(int printerCap, int? cumulativeUnitCeiling);  // FR-006c/d, editable
}
```

- **Quantity nullability is the unlimited/finite discriminator** (FR-006) — no separate boolean flag,
  matching the same "meaning lives in the value, not a second field" shape `NonPrintedComponent.UnitCost`
  already established in `013-assembly-bom`.
- **`BackfillPrinterCap`/`BackfillCumulativeUnitCeiling` are stored on the line, not derived** — they are
  operator-set configuration, unlike the *occupancy*/*cumulative-so-far* numbers the dispatcher checks
  against them, which are always computed from the line's own `PrintJob` rows at read time (`research.md`
  R4), never stored redundantly.
- **An Assembly-targeted unlimited line** produces one `PartRequirement` per printed part in a quantity-1
  explosion (`research.md` R1/R2) — the cap/ceiling above apply at the **line** level, aggregated across
  every `PrintJob` whose `PartRequirement.RunLineId` is this line, not per individual part. A single
  backfill cycle for an assembly-target line therefore means "one lazily-created job per distinct printed
  part in that assembly," all counted together against the same cap.

### `PartRequirement` (new entity, `OpenFarm.Domain.Production`)

The exploded leaf demand — one row per distinct (RunLine, Part) pair (spec Key Entities / FR-004),
snapshotted once and never re-derived (`research.md` R8).

```csharp
public sealed class PartRequirement
{
    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public Guid RunLineId { get; }
    public Guid PartId { get; }
    public decimal? Quantity { get; }     // null mirrors the owning RunLine's unlimited quantity; otherwise the summed exploded total (FR-004)

    internal static PartRequirement Create(Guid organizationId, Guid runLineId, Guid partId, decimal? quantity);
}
```

- **No back-reference to `Assembly`/`BomLine`** beyond `RunLineId` (`research.md` R8) — this is the entire
  mechanism that makes FR-003's snapshot guarantee true by construction: there is nothing here a later BOM
  edit could reach.
- **Non-printed components never produce a row here** (FR-004a) — `PartRequirement` is exclusively
  printed-part demand; `research.md` R1's resolution step discards `BomExplosionResult.NonPrintedComponents`
  before this entity is ever created.

### `PrintJob` (new entity, `OpenFarm.Domain.Production`)

The execution unit (spec Key Entities) — flat and atomic, unlike everything above it.

```csharp
public enum PrintJobStatus { Queued, Printing, Paused, Failed, Completed, Cancelled }
public enum AssignmentMode { Unassigned, Pinned }

public sealed class PrintJob
{
    private readonly List<JobFilamentConsumption> _filamentConsumption = [];

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public Guid PartRequirementId { get; }
    public double QueuePosition { get; private set; }              // fractional index (research.md R3)
    public int PlateQuantity { get; }                               // == Part.UnitsPerPrint at creation time — informational, this plate's target count
    public AssignmentMode AssignmentMode { get; private set; }
    public Guid? PinnedPrinterId { get; private set; }              // set iff AssignmentMode == Pinned (FR-007c)
    public Guid? DispatchedPrinterId { get; private set; }          // set by the dispatcher once actually sent (either mode)
    public PrintJobStatus Status { get; private set; }
    public string FileRevisionHash { get; private set; }            // FR-009c — copied from Part at queue (Snapshot) or dispatch (Follow-latest) time
    public int FileRevisionNumber { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }    // set when sent to a printer (live push, or ready-for-manual-start)
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public int? ActualDurationMinutes { get; private set; }         // FR-015, derived from Started/EndedAtUtc on close
    public IReadOnlyList<JobFilamentConsumption> FilamentConsumption => _filamentConsumption;
    public DateTimeOffset CreatedAtUtc { get; }

    internal static PrintJob Create(Guid organizationId, Guid partRequirementId, double queuePosition, int plateQuantity, AssignmentMode assignmentMode, Guid? pinnedPrinterId, string fileRevisionHash, int fileRevisionNumber, IReadOnlyList<JobFilamentRequirementSpec> filamentRequirements);
    public void Pin(Guid printerId);                                // operator action, FR-007
    public void Unpin();                                            // -> AssignmentMode.Unassigned
    public void Reorder(double newQueuePosition);                   // FR-008
    public void Dispatch(Guid printerId, string fileRevisionHash, int fileRevisionNumber);  // FR-009 — Follow-latest re-reads the revision here
    public void MarkStarted();                                      // FR-012 manual-brand path, or live auto-detect (Queued -> Printing)
    public void MarkPaused();                                       // telemetry-only (research.md R12) — no command/endpoint calls this
    public void MarkResumed();                                      // telemetry-only (research.md R12) — Paused -> Printing
    public void MarkFailed(string? reason);
    public void MarkCompleted();
    public void MarkCancelled();                                    // FR-017
    public void RecordFilamentConsumption(Guid consumptionLineId, Guid? spoolId, decimal gramsConsumed);  // FR-013/FR-013a, callable on any terminal transition
}
```

- **Status transitions are guarded by a `PrintJobStatusTransitions` static class** (FR-014), the same
  strict-transition-table shape `PrinterStatusTransitions` already established for `Printer.TransitionTo` —
  a deterministic state machine, TDD'd per constitution Principle II. Legal edges: `Queued → Printing`,
  `Printing → Paused`, `Paused → Printing`, `Printing/Paused → Failed`, `Printing/Paused → Completed`,
  `Queued/Printing/Paused → Cancelled`. Any other transition throws `InvalidPrintJobStatusTransitionException`.
- **`MarkPaused`/`MarkResumed` are display-only and telemetry-driven, never operator-triggered**
  (`research.md` R12) — no `Application` command and no endpoint calls either method. The only caller is
  the new `IJobPauseTelemetrySync` implementation (`Infrastructure.Production`), invoked directly from
  `PrinterConnectionBackgroundService.HandleEventAsync`, mirroring exactly how that same method already
  calls `IFeedSlotTelemetrySync` today. This is the same "Infrastructure calls a Domain entity method
  directly off live telemetry, no Application command in between" shape `Printer.ObserveStatus` itself
  already uses — `PrintJob` isn't doing anything novel here, just following the established precedent for
  a second entity.
- **Reaching `Completed`/`Failed`/`Cancelled` triggers the owning printer's `TransitionTo(AwaitingClear)`**
  (FR-014a) — this is an *Application*-layer side effect (the command handler that calls
  `job.MarkCompleted()` also calls `printer.TransitionTo(PrinterStatus.AwaitingClear)` in the same
  transaction), not something `PrintJob` itself can do, since it has no reference to `Printer`.
- **`FileRevisionHash`/`FileRevisionNumber` are set at `Create` time and MAY be overwritten exactly once,
  by `Dispatch`**, only when the owning line's `RevisionMode == FollowLatest` (`research.md` R6) — under
  `Snapshot` the value from `Create` is never touched again. The Application-layer dispatch handler decides
  whether to call the revision-updating overload of `Dispatch`, not `PrintJob` itself (which has no
  visibility into its own `RunLine`'s mode).
- **`JobFilamentConsumption` rows are created at `Create` time** with `SpoolId = null`/`GramsConsumed =
  null` (defaulted from the Part's filament requirements, FR-013b) and filled in via
  `RecordFilamentConsumption` as the job closes — never a separate late-added collection.

### `JobFilamentConsumption` (new entity, `OpenFarm.Domain.Production`)

One material/color breakdown line on a job (spec Key Entities — "a collection, not a single value," FR-013).

```csharp
public sealed class JobFilamentConsumption
{
    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public Guid PrintJobId { get; }
    public string? MaterialType { get; }        // copied from Part.FilamentRequirements at job creation
    public string? DetectedColorHex { get; }
    public Guid? SpoolId { get; private set; }        // resolved at dispatch/completion time, not creation
    public decimal? GramsConsumed { get; private set; }  // null until the job closes (FR-013a fills it even on failure)
}
```

### `Part` (amended — `OpenFarm.Domain.Parts`)

Two additive fields (spec Key Entities — "the only two amendments made to the Part entity in this
phase"), both nullable-free with safe defaults so every existing `Part` row remains valid without a data
backfill beyond the migration's default-value column add:

```csharp
public int UnitsPerPrint { get; private set; }     // FR-005a — default 1, auto-suggested, operator-overridable
public string RevisionHash { get; private set; }    // FR-009c — SHA-256 hex of the current file's bytes
public int RevisionNumber { get; private set; }      // FR-009c — starts at 1, incremented on every ReplaceFile
```

`RevisionHash`/`RevisionNumber` are computed and set in the same place `StoragePath` already is —
`Part.Create` and `Part.ReplaceFile` — by the Application-layer caller (the file's bytes are hashed once,
before either method is invoked, exactly where `StoragePath` is already resolved today; `research.md` R6).
`ReplaceFile`'s existing unconditional `IPartFileStorage.DeleteAsync(oldPath)` call is removed from the
Application-layer handler that invokes it — the old, now content-addressed file is simply left in place.

## Persistence (`OpenFarm.Infrastructure.Persistence`)

### Tables

```text
ProductionRuns
  Id                uuid PK
  OrganizationId    uuid NOT NULL
  Name              text NULL
  DueDate           date NULL
  Priority          text NULL
  Notes             text NULL
  Status            text NOT NULL   (HasConversion<string>(), research.md R9)
  CreatedAtUtc       timestamptz NOT NULL
  UpdatedAtUtc       timestamptz NOT NULL
  xmin              (IsRowVersion())

RunLines
  Id                          uuid PK
  OrganizationId              uuid NOT NULL
  ProductionRunId             uuid NOT NULL  FK -> ProductionRuns.Id  ON DELETE CASCADE
  TargetKind                  text NOT NULL  (Part | Assembly)
  TargetId                    uuid NOT NULL  (no DB FK — polymorphic target across two tables; validated at Application layer, mirrors how BomLine's three-column CHECK approach was rejected here since there are only ever two kinds and no NonPrintedComponent-shaped third option muddying it — see research note below)
  Quantity                    int NULL       (NULL = unlimited/backfill)
  RevisionMode                text NOT NULL
  DefaultAssignmentMode       text NOT NULL
  DefaultPinnedPrinterId      uuid NULL      FK -> Printers.Id  ON DELETE SET NULL
  BackfillPrinterCap          int NOT NULL DEFAULT 1
  BackfillCumulativeUnitCeiling int NULL
  CHECK (Quantity IS NULL OR Quantity >= 1)
  CHECK (DefaultAssignmentMode = 'Unassigned' OR DefaultPinnedPrinterId IS NOT NULL)

PartRequirements
  Id                uuid PK
  OrganizationId    uuid NOT NULL
  RunLineId         uuid NOT NULL  FK -> RunLines.Id  ON DELETE CASCADE
  PartId            uuid NOT NULL  FK -> Parts.Id     ON DELETE RESTRICT
  Quantity          numeric NULL   (NULL mirrors the owning RunLine's unlimited quantity)
  UNIQUE (RunLineId, PartId)   -- FR-004's grain rule, enforced structurally

PrintJobs
  Id                    uuid PK
  OrganizationId        uuid NOT NULL
  PartRequirementId     uuid NOT NULL  FK -> PartRequirements.Id  ON DELETE CASCADE
  QueuePosition         double precision NOT NULL
  PlateQuantity         int NOT NULL
  AssignmentMode        text NOT NULL
  PinnedPrinterId       uuid NULL   FK -> Printers.Id  ON DELETE SET NULL
  DispatchedPrinterId   uuid NULL   FK -> Printers.Id  ON DELETE SET NULL
  Status                text NOT NULL
  FileRevisionHash      text NOT NULL
  FileRevisionNumber    int NOT NULL
  DispatchedAtUtc       timestamptz NULL
  StartedAtUtc          timestamptz NULL
  EndedAtUtc            timestamptz NULL
  ActualDurationMinutes int NULL
  CreatedAtUtc           timestamptz NOT NULL
  xmin                  (IsRowVersion())
  INDEX (OrganizationId, Status, AssignmentMode, QueuePosition)   -- the dispatcher's own hot-path query (research.md R4)
  INDEX (OrganizationId, PinnedPrinterId) WHERE PinnedPrinterId IS NOT NULL

JobFilamentConsumptions
  Id                uuid PK
  OrganizationId    uuid NOT NULL
  PrintJobId        uuid NOT NULL  FK -> PrintJobs.Id  ON DELETE CASCADE
  MaterialType      text NULL
  DetectedColorHex  text NULL
  SpoolId           uuid NULL  FK -> Spools.Id  ON DELETE SET NULL
  GramsConsumed     numeric NULL
```

**`RunLines.TargetId` has no database foreign key** — unlike `BomLine`'s three-nullable-FK-column shape
(`013-assembly-bom` `research.md` R5), a `RunLine` has exactly two possible target tables and the
discriminator (`TargetKind`) is already required and non-nullable, so a single `(TargetKind, TargetId)`
pair validated at the Application layer (existence check against the right table before the line is
persisted) is simpler here and loses nothing `BomLine`'s extra FK columns bought — `BomLine` needed real
per-target-kind `ON DELETE` behavior (block delete of a referenced Part/Assembly/NonPrintedComponent,
FR-011 there); `RunLine`'s targets are never deleted out from under it in a way that matters, since a
`RunLine`'s `PartRequirement`s are already snapshotted and independent of the source Part/Assembly's
continued existence (`research.md` R8).

All five new tables call `.IsMultiTenant()`, consistent with every tenant-scoped entity since Phase 0.

### `Parts` table amendment

```text
ALTER TABLE "Parts" ADD "UnitsPerPrint" int NOT NULL DEFAULT 1;
ALTER TABLE "Parts" ADD "RevisionHash" text NOT NULL DEFAULT '';   -- backfilled from existing StoragePath content in the migration's data step, see below
ALTER TABLE "Parts" ADD "RevisionNumber" int NOT NULL DEFAULT 1;
```

The migration includes a one-time data-backfill step (raw SQL, run once, inside the same migration) that
computes `RevisionHash` for every existing `Part` row from its current stored file, so no row is ever left
with the placeholder empty-string default in a running system — the same "migrate existing data, don't
leave a placeholder" bar `007-part-library` and `013-assembly-bom` both held themselves to for their own
additive columns.

### Migrations

Two migrations, kept separate since one alters an existing table's data and the other adds wholly new
ones (constitution: "one EF Core migration per PR when the schema changes... unrelated schema changes are
not bundled into a single migration" — these are related in feature but distinct in blast radius, so
`tasks.md` should confirm the final split; sketched here as two for planning purposes):
1. `AddPartRevisionAndUnitsPerPrint` — the three `Parts` columns above, plus the one-time hash backfill.
2. `AddProductionRunsAndJobs` — `ProductionRuns`, `RunLines`, `PartRequirements`, `PrintJobs`,
   `JobFilamentConsumptions`.

## Read-side shapes (Application layer, not persisted)

### `QueueJobRow` — the flat, dispatch-order queue view (FR-008, the "by dispatch order" lens)

```csharp
public sealed record QueueJobRow(
    Guid JobId, double QueuePosition, PrintJobStatus Status, AssignmentMode AssignmentMode,
    Guid? PinnedPrinterId, Guid? DispatchedPrinterId, bool IsBlocked,   // IsBlocked: Pinned + target printer unavailable (FR-007c)
    Guid PartId, string PartName, int PlateQuantity,
    Guid RunLineId, Guid ProductionRunId, string RunDisplayLabel);      // derived label — run's own Name, or "<PartName> ×<Quantity>" / "N parts — ..." when null
```

Powers the virtualized (`TanStack Virtual`, constitution Principle V) flat queue list — one page-able,
`ORDER BY QueuePosition` query, no tree assembly needed for this lens.

### `RunDetailView` — the "by run" tree lens (single-level, lazily expanded — mirrors `013-assembly-bom`
`research.md` R7's pattern for Assembly detail)

```csharp
public sealed record RunDetailView(
    Guid Id, string? Name, DateOnly? DueDate, string? Priority, string? Notes, ProductionRunStatus Status,
    IReadOnlyList<RunLineSummary> Lines);

public sealed record RunLineSummary(
    Guid Id, RunLineTargetKind TargetKind, Guid TargetId, string TargetName, int? Quantity,
    RevisionMode RevisionMode, IReadOnlyList<PartRequirementSummary> Requirements);

public sealed record PartRequirementSummary(
    Guid Id, Guid PartId, string PartName, decimal? Quantity, int JobCount, int CompletedJobCount);
```

Job-level detail (individual `PrintJob` rows under a requirement) is fetched separately, paginated, the
same "expand on demand" shape `013-assembly-bom` established rather than one deeply nested payload.

### `DispatchCandidate` (Application/Infrastructure boundary, `research.md` R4 — internal to the dispatcher)

```csharp
internal sealed record DispatchCandidate(Guid JobId, double QueuePosition, decimal RequiredNozzleDiameterMm, IReadOnlyList<(string? MaterialType, string? ColorHex)> FilamentRequirement);
```

The pure matching function (`ProductionDispatchMatcher.SelectNextJob(printerNozzleDiameter,
printerLoadedMaterials, candidates)` — unit-testable with no DB dependency, per `research.md` R4's TDD
note) operates over this shape, kept deliberately separate from the EF query that produces it.

## Domain Exceptions (`OpenFarm.Domain.Production`)

| Exception | Thrown when | HTTP mapping |
|---|---|---|
| `ProductionRunNotFoundException` | referenced run id doesn't exist / isn't in the caller's org | 404 |
| `RunLineNotFoundException` | referenced line id doesn't exist on the given run | 404 |
| `PrintJobNotFoundException` | referenced job id doesn't exist | 404 |
| `RunLineMustTargetExactlyOnePartOrAssemblyException` | a line specifies zero or both target kinds (Application-layer request validation, not reachable via valid DTOs — defense in depth) | 400 |
| `ProductionRunMustHaveAtLeastOneLineException` | `Create` called with zero lines | 422 |
| `RunLineTargetNotFoundException` | a line's `TargetId` doesn't resolve to an existing Part/Assembly in the caller's org | 404 |
| `InvalidPrintJobStatusTransitionException` | `PrintJob` status change outside the legal graph (FR-014) | 422 |
| `InvalidRunCompletionException` | `MarkComplete` called while any finite requirement is unsatisfied (FR-016a) | 422 |
| `PinnedJobRequiresPrinterException` | `Pin` called with no printer id, or a `RunLine` default-assignment spec is `Pinned` with no default printer | 400 |
| `PrintJobAlreadyDispatchedException` | `Reorder`/`Pin`/`Unpin` attempted on a job no longer in `Queued` (FR-008/FR-007 only make sense pre-dispatch) | 409 |
| `ProductionRunConcurrencyConflictException` | same-row optimistic-concurrency conflict on a run/job (`xmin`) | 409 |
| `FilamentConsumptionSumMismatchException` | (guardrail, not spec-required this phase, but cheap defense-in-depth) attempted to record consumption for a material/color pair not present on the job | 400 |

All follow the existing `DomainExceptionHandler` switch-expression pattern — one new arm per exception, no
new middleware.
