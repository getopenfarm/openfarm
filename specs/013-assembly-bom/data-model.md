# Data Model: Assembly (BOM)

**Feature**: `013-assembly-bom` | **Date**: 2026-08-06

## Entities

### `Assembly` (new entity, `OpenFarm.Domain.Assemblies`)

A named description of a finished product (spec Key Entities). Mirrors `Part`'s shape (private
constructor + `Create` factory, `xmin` concurrency token) but carries no file/estimate fields at all.

```csharp
public sealed class Assembly
{
    private readonly List<BomLine> _lines = [];

    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public IReadOnlyList<BomLine> Lines => _lines;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Assembly Create(Guid organizationId, string name, string? description, IReadOnlyList<BomLineSpec> lines);
    public void Rename(string name, string? description);
    public BomLine AddLine(BomLineSpec spec);          // FR-013 duplicate-target check happens here
    public void UpdateLineQuantity(Guid lineId, decimal quantity);
    public void RemoveLine(Guid lineId);               // rejects if it would leave zero lines (FR-002)
}
```

- **Invariant (FR-002)**: an `Assembly` is never persisted with zero lines. `Create` requires
  `lines.Count >= 1`; `RemoveLine` throws `AssemblyMustHaveAtLeastOneLineException` if `_lines.Count == 1`.
  This is checked in `Domain` (constructible-invalid-state prevention) — it does **not** by itself
  guarantee no *cyclic* zero-line intermediate state exists across a multi-line edit sequence, because
  cycle/depth validation (below) is a cross-assembly, cross-transaction concern the entity itself cannot
  see; that's `AssemblyGraph` and `research.md` R2/R3's job, run by the command handler before the
  repository ever persists the change.
- **No status, no stock, no printer/job/run relationship** (spec Key Entities) — deliberately nothing
  beyond `Name`/`Description`/`Lines` on this entity.
- **Self-reference vs. transitive cycle**: `AddLine`/`UpdateLineQuantity` do **not** themselves run cycle
  detection — that requires the caller's already-loaded org-wide edge graph (`research.md` R2), which the
  entity has no access to. The command handler is what calls `AssemblyGraph.ValidateSave(...)` before
  invoking these entity methods, then persists only if it passes. A direct-self-reference CHECK constraint
  (`TargetAssemblyId <> AssemblyId`) still exists at the DB layer as defense-in-depth for the trivial case
  (spec Edge Cases: "a direct self-reference is just the shortest cycle").

### `BomLine` (new entity, `OpenFarm.Domain.Assemblies`)

One polymorphic component entry, owned by exactly one `Assembly` (spec Key Entities / FR-003/FR-004).

```csharp
public enum BomLineTargetKind { Part, Assembly, NonPrintedComponent }

public sealed class BomLine
{
    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public Guid AssemblyId { get; }                       // owning assembly (FK, ON DELETE CASCADE)
    public BomLineTargetKind TargetKind { get; }
    public Guid? TargetPartId { get; }                    // set iff TargetKind == Part
    public Guid? TargetAssemblyId { get; }                // set iff TargetKind == Assembly
    public Guid? TargetNonPrintedComponentId { get; }     // set iff TargetKind == NonPrintedComponent
    public decimal Quantity { get; private set; }

    internal static BomLine Create(Guid organizationId, Guid assemblyId, BomLineSpec spec);
    internal void UpdateQuantity(decimal quantity);
}
```

- **Quantity validation (FR-004a/FR-004b)**, enforced in `BomLine.Create`/`UpdateQuantity`:
  - `TargetKind` is `Part` or `Assembly` → `Quantity` must be a whole number `>= 1`
    (`Quantity == Math.Truncate(Quantity)`).
  - `TargetKind` is `NonPrintedComponent` → `Quantity` must be `> 0`; fractional values allowed.
  - Stored as `numeric` (not `int`) specifically to admit the second case — see
    `BomLineConfiguration` below.
- **Exactly-one-target invariant (FR-003)** is enforced in the private constructor (throws
  `ArgumentException` if the constructor is ever reached with zero or more than one target set — a
  programming-error guard, since `BomLineSpec` itself is a closed discriminated shape that can't
  represent an invalid combination) and backstopped by a DB CHECK constraint (`BomLineConfiguration`).
- `BomLineSpec` (a small internal factory-input record: `TargetKind`, the three nullable target ids,
  `Quantity`) is how `Assembly.AddLine`/`Create` receive a line to add without exposing `BomLine`'s
  constructor publicly — mirrors `FilamentRequirementSpec`/`ThumbnailSpec`'s existing role for `Part`.

### `NonPrintedComponent` (new entity, `OpenFarm.Domain.Assemblies`)

A reusable, informational catalog entry (spec Key Entities / FR-005). Structurally the simplest new
entity in this feature — no state machine, no derived data.

```csharp
public sealed class NonPrintedComponent
{
    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public string Name { get; private set; }
    public string Unit { get; private set; }
    public decimal? UnitCost { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static NonPrintedComponent Create(Guid organizationId, string name, string unit, decimal? unitCost);
    public void Edit(string name, string unit, decimal? unitCost);
}
```

- **No stock/on-hand/reorder fields of any kind** (FR-005a) — this is the entire reason the entity is
  simple: there is nothing here to model beyond identity, display unit, and an optional cost.
- `UnitCost` is genuinely optional (FR-005c) — `null` is a fully valid, permanently supported state, not
  an "incomplete" one; nothing anywhere gates on it being present.
- `Unit` is a free-text short label (spec Assumptions — "each", "ml", "m", "pack"), validated only for
  non-empty; nothing in the system parses or interprets it.

## Persistence (`OpenFarm.Infrastructure.Persistence`)

### Tables

```text
Assemblies
  Id                uuid PK
  OrganizationId    uuid NOT NULL
  Name              text NOT NULL
  Description       text NULL
  CreatedAtUtc       timestamptz NOT NULL
  UpdatedAtUtc       timestamptz NOT NULL
  xmin              (Postgres system column, mapped as IsRowVersion())

NonPrintedComponents
  Id                uuid PK
  OrganizationId    uuid NOT NULL
  Name              text NOT NULL
  Unit              text NOT NULL
  UnitCost          numeric NULL
  CreatedAtUtc       timestamptz NOT NULL
  UpdatedAtUtc       timestamptz NOT NULL
  xmin

BomLines
  Id                            uuid PK
  OrganizationId                uuid NOT NULL
  AssemblyId                    uuid NOT NULL  FK -> Assemblies.Id  ON DELETE CASCADE
  TargetKind                    text NOT NULL  (HasConversion<string>(), matches PartFileFormat's convention)
  TargetPartId                  uuid NULL      FK -> Parts.Id                ON DELETE RESTRICT
  TargetAssemblyId              uuid NULL      FK -> Assemblies.Id           ON DELETE RESTRICT
  TargetNonPrintedComponentId   uuid NULL      FK -> NonPrintedComponents.Id ON DELETE RESTRICT
  Quantity                      numeric NOT NULL
  CHECK (
    (TargetKind = 'Part'               AND TargetPartId IS NOT NULL AND TargetAssemblyId IS NULL AND TargetNonPrintedComponentId IS NULL) OR
    (TargetKind = 'Assembly'           AND TargetAssemblyId IS NOT NULL AND TargetPartId IS NULL AND TargetNonPrintedComponentId IS NULL) OR
    (TargetKind = 'NonPrintedComponent' AND TargetNonPrintedComponentId IS NOT NULL AND TargetPartId IS NULL AND TargetAssemblyId IS NULL)
  )
  CHECK (TargetAssemblyId IS NULL OR TargetAssemblyId <> AssemblyId)   -- direct self-reference backstop
  UNIQUE INDEX (AssemblyId, TargetPartId)               WHERE TargetPartId IS NOT NULL
  UNIQUE INDEX (AssemblyId, TargetAssemblyId)            WHERE TargetAssemblyId IS NOT NULL
  UNIQUE INDEX (AssemblyId, TargetNonPrintedComponentId) WHERE TargetNonPrintedComponentId IS NOT NULL
```

`BomLines.AssemblyId` and `BomLines.TargetAssemblyId` are two independently-configured FKs to the same
`Assemblies` table (`HasOne<Assembly>().WithMany(a => a.Lines).HasForeignKey(l => l.AssemblyId)` with
`OnDelete(DeleteBehavior.Cascade)`, vs. a second unnavigated `HasOne<Assembly>().HasForeignKey(l =>
l.TargetAssemblyId)` with `OnDelete(DeleteBehavior.Restrict)`) — EF Core supports this without conflict
since they're distinct foreign key properties.

All three entities call `.IsMultiTenant()` in their `IEntityTypeConfiguration`, consistent with every
tenant-scoped entity since Phase 0 (constitution: Finbuckle's EF Core global query filter is the *only*
sanctioned tenant-isolation mechanism).

### Migration

One migration, `AddAssembliesAndBom`, creating all three tables plus their FKs/CHECKs/indexes above — no
change to any existing table (`Parts`, `Organizations`, etc. are referenced, never altered).

## Read-side shapes (Application layer, not persisted)

### `BomExplosionResult` (spec Key Entities — FR-009's output)

```csharp
public sealed record BomExplosionResult(
    IReadOnlyList<ExplodedPartQuantity> PrintedParts,
    IReadOnlyList<ExplodedNonPrintedComponentQuantity> NonPrintedComponents);

public sealed record ExplodedPartQuantity(Guid PartId, string PartName, decimal Quantity);
public sealed record ExplodedNonPrintedComponentQuantity(Guid NonPrintedComponentId, string Name, string Unit, decimal Quantity);
```

Never persisted (FR-009) — recomputed on every call by `AssemblyGraphQueryService.ExplodeAsync`
(`research.md` R1). `PrintedParts`/`NonPrintedComponents` are reported separately per FR-009d, never
merged into one list a caller could misread.

### `BuildableUnitsResult` (spec Key Entities — FR-010's output)

```csharp
public sealed record BuildableUnitsResult
{
    public required bool IsApplicable { get; init; }
    public int? Value { get; init; }          // set iff IsApplicable
    public string? NotApplicableReason { get; init; }  // set iff !IsApplicable (FR-010d)
}
```

A discriminated shape rather than a bare `int` specifically so "not applicable" (no printed components
anywhere in the tree) can never be silently read as `0` or as unbounded (`research.md` R6).

### `AssemblyGraph` (Domain, `research.md` R2 — the TDD target)

```csharp
public sealed class AssemblyGraph
{
    public static AssemblyGraph FromEdges(IReadOnlyCollection<(Guid ParentAssemblyId, Guid ChildAssemblyId)> edges);

    // Throws AssemblyCycleException / AssemblyDepthLimitExceededException; no-op if valid.
    public void ValidateSave(Guid savingAssemblyId, Guid newOrChangedTargetAssemblyId);
}
```

Pure, constructor-injected-nothing, unit-testable with plain in-memory edge lists — no EF/DB dependency
at all, which is exactly what makes it TDD-able per constitution Principle II.

## Domain Exceptions (`OpenFarm.Domain.Assemblies`)

| Exception | Thrown when | HTTP mapping |
|---|---|---|
| `AssemblyNotFoundException` | referenced `Assembly` id doesn't exist / isn't in the caller's org | 404 |
| `NonPrintedComponentNotFoundException` | referenced `NonPrintedComponent` id doesn't exist | 404 |
| `BomLineNotFoundException` | referenced line id doesn't exist on the given assembly | 404 |
| `AssemblyMustHaveAtLeastOneLineException` | `Create`/`RemoveLine` would leave zero lines (FR-002) | 422 |
| `DuplicateBomLineTargetException` | target already appears on the assembly (FR-013) | 409 |
| `AssemblyCycleException` | save would create a direct/transitive cycle (FR-007), carries the offending path | 422 |
| `AssemblyDepthLimitExceededException` | save would exceed the 10-level max (FR-006a), carries the limit | 422 |
| `AssemblyConcurrencyConflictException` | same-row optimistic-concurrency conflict (`research.md` R4) | 409 |
| `NonPrintedComponentConcurrencyConflictException` | same, for `NonPrintedComponent` | 409 |
| `AssemblyReferencedException` | delete blocked — assembly is a sub-assembly target elsewhere (FR-011), names referencing assemblies | 409 |
| `PartReferencedByAssemblyException` | delete blocked — Part is targeted by a BOM line (narrows Phase 4's own delete rule, FR-011) | 409 |
| `NonPrintedComponentReferencedException` | delete blocked — component is targeted by a BOM line (FR-011) | 409 |
| `InvalidExplosionQuantityException` | explosion requested at quantity `< 1` or non-integer (FR-009e) | 400 |

All follow the existing `DomainExceptionHandler` switch-expression pattern — one new arm per exception,
no new middleware.
