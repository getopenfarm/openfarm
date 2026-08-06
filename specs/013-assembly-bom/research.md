# Phase 0 Research: Assembly (BOM)

No `NEEDS CLARIFICATION` markers remain in this feature's Technical Context — `spec.md`'s own
Clarifications session (2026-08-06) already resolved the two genuinely open questions (concurrent
cross-assembly cycle prevention, dropping the per-path breakdown). This document instead records the
non-trivial *implementation-approach* decisions a spec/clarification pass doesn't cover: none of them
have prior art elsewhere in this codebase, the same situation `007-part-library`'s research.md was in
for its five new infrastructure pieces.

## R1 — Recursive BOM decomposition: a literal `WITH RECURSIVE` CTE, scoped to explosion only

**Decision**: BOM explosion (FR-009, the quantity-multiplication-and-aggregation pass) runs as one
Postgres `WITH RECURSIVE` CTE issued via `Database.SqlQueryRaw<T>` from `AssemblyGraphQueryService`
(`Infrastructure`), not as application-side recursion walking EF-loaded entities.

**Rationale**: the constitution is explicit and non-optional on this exact point (Principle V:
"BOM/assembly decomposition MUST run as a recursive CTE at the database layer, not as application-side
recursion, so correctness and latency don't degrade as nesting gets deeper") and SC-007 gives it a hard
number (500 lines across 10 levels, under 1 second) that application-side recursion — N+1 loading each
level's children — would put at real risk. A single recursive CTE walks the tree, multiplies quantity
down each edge, and aggregates by leaf target in one round trip, with cost bounded by rows scanned, not
by round-trips-per-level.

**Shape** (illustrative — final column/table names are `tasks.md`/implementation-level, not fixed here):

```sql
WITH RECURSIVE explosion AS (
    SELECT bl."Id", bl."TargetKind", bl."TargetPartId", bl."TargetAssemblyId",
           bl."TargetNonPrintedComponentId", bl."Quantity" AS multiplier, 1 AS depth
    FROM "BomLines" bl
    WHERE bl."AssemblyId" = @rootAssemblyId AND bl."OrganizationId" = @organizationId
    UNION ALL
    SELECT child."Id", child."TargetKind", child."TargetPartId", child."TargetAssemblyId",
           child."TargetNonPrintedComponentId", child."Quantity" * e.multiplier, e.depth + 1
    FROM "BomLines" child
    JOIN explosion e ON child."AssemblyId" = e."TargetAssemblyId"
    WHERE e."TargetKind" = 'Assembly' AND e.depth < 10   -- FR-009f: terminate at the max depth, don't recurse unbounded
)
SELECT "TargetKind", COALESCE("TargetPartId", "TargetNonPrintedComponentId") AS leaf_id, SUM(multiplier) AS total_quantity
FROM explosion
WHERE "TargetKind" IN ('Part', 'NonPrintedComponent')
GROUP BY "TargetKind", leaf_id;
```

The requested root quantity (FR-009a) multiplies the anchor member's `multiplier` rather than being
applied as a second pass, keeping the whole computation inside one query. `depth < 10` on the recursive
term is FR-009f's termination guarantee, independent of and in addition to save-time cycle rejection —
explosion still performs no cycle *validation* (that stays FR-007's job), it just refuses to loop
forever if it ever somehow reached un-validated data.

**Testing approach**: this query is raw SQL text, not a unit-testable C# algorithm, so it's TDD'd via
`WebApplicationFactory` + Testcontainers.PostgreSql integration tests written *before* the query
(red-green against a real Postgres instance) — the closest available approximation of red-green-refactor
for SQL, and the same tool this codebase already uses for every Postgres-dependent integration test.
SC-001's two reference assemblies (full 10-level depth; a part at three depths) and SC-007's 500-line
perf assembly are the literal fixtures.

## R2 — Cycle/depth validation: an in-memory DFS over a flat edge list, not a second recursive CTE

**Decision**: FR-006a (depth) and FR-007 (cycle) validation, run on every structural save, load the
requesting organization's **entire Assembly→Assembly edge list** — `SELECT "AssemblyId", "TargetAssemblyId" FROM "BomLines" WHERE "OrganizationId" = @org AND "TargetKind" = 'Assembly'`, a plain non-recursive
LINQ query — into memory once per command, then run cycle detection and bidirectional depth calculation
(FR-008: downward through the edit's own subtree, upward through every assembly that already includes
it) as a **pure, unit-tested Domain-layer graph algorithm** (`AssemblyGraph`, a DFS/BFS over an
adjacency map), not a second `WITH RECURSIVE` query.

**Rationale**: this is a deliberate split from R1, not an inconsistency, and it's worth being explicit
about why. The constitution's CTE mandate is about *decomposition* — walking a tree while multiplying
Part-level quantities down it, the thing that's genuinely expensive and needs to scale with row count
(SC-007). Cycle/depth validation never touches a Part or NonPrintedComponent row at all — it only cares
about the much smaller Assembly→Assembly edge subgraph, which for a real print farm's catalog (assemblies
are hand-authored catalog entities, not per-job data) is expected to stay small enough to hold entirely
in memory even at hundreds of assemblies. Doing it as a pure C# algorithm over a fetched edge list, rather
than a second independent SQL implementation of graph traversal, keeps exactly one recursive-traversal
implementation to reason about and test (R1's), while making the validation rule itself — the thing the
constitution separately calls out as "the clearest TDD target in the plan" per `docs/FEATURE_PLAN.md` —
directly unit-testable with xUnit + FluentAssertions against constructed graphs, no database required.
This is squarely inside what constitution Principle II requires TDD for ("Domain state machines,
BOM/assembly recursive decomposition... non-trivial FluentValidation gates").

**Algorithm sketch**: given edge set `E` (existing edges) and the proposed new/changed edge `X → Y`
(assembly `X` gains or updates a line targeting sub-assembly `Y`):
- **Cycle**: reject if `X` is reachable from `Y` in `E ∪ {X→Y}` — i.e., DFS from `Y` downward; if `X`
  is visited, the edge would close a cycle. The visited path is exactly the "offending containment path"
  FR-007 requires naming.
- **Depth**: compute `rootDistance(X)` = the longest upward chain from `X` to any assembly that has no
  further parent (i.e., how many levels already sit above `X`, `X` itself counting as 0), and
  `subtreeDepth(Y)` = the longest downward chain starting at `Y` (`Y` itself counting as 1, matching
  FR-006's "assembly being saved counts as level 1"). Reject if `rootDistance(X) + subtreeDepth(Y) + 1 > 10`.
  Both are plain DFS/memoized-DFS over the same in-memory adjacency map — no second query.

## R3 — Guaranteeing no cycle under concurrent saves (FR-007a): a per-organization Postgres advisory lock

**Decision**: every command that mutates BOM structure (add/edit/remove a line, create/delete an
Assembly) opens its DB transaction by taking `pg_advisory_xact_lock(hashtext(@organizationId))` —
a transaction-scoped Postgres advisory lock keyed by organization — **before** running R2's
validation read and the subsequent write, and releases automatically at commit/rollback.

**Rationale — why not just reuse Part's xmin/`PartConcurrencyConflictException` precedent directly**:
that precedent (already used here too, see R4) is single-row optimistic concurrency — it detects that
*the same row* changed between read and write. FR-007a's failure mode is different in kind: two
*different* assemblies (X and Y) are each individually valid against the state each transaction read,
and only *jointly* create a cycle. Optimistic concurrency on X's row and Y's row separately cannot catch
this — neither row's own version conflicts with the other's write. Serializable transaction isolation
with client-side retry was considered and rejected as needlessly complex for what structural BOM edits
actually need: they are infrequent, low-throughput catalog-authoring operations (not a hot job-queue
path), so serializing all of them within one organization behind a single lock has no meaningful
throughput cost and is far simpler to reason about and test than retry-on-serialization-failure.

**What this actually produces, concretely**: transaction A (editing X to include Y) and transaction B
(editing Y to include X) both attempt the advisory lock; one blocks until the other commits. Say A wins
and commits first. B then acquires the lock and re-runs R2's validation — which now sees A's just-committed
edge X→Y, so B's own edge Y→X is correctly detected as closing a cycle **by the ordinary FR-007 rule**,
not a separate "someone else touched this" conflict type. This is a better outcome than a generic
optimistic-concurrency error: the operator whose save is rejected sees the same "this would create a
cycle through this path" message anyone hitting FR-007 synchronously would, which is more actionable than
"retry your edit." `AssemblyConcurrencyConflictException` (mirroring `PartConcurrencyConflictException`,
R4) is retained for the narrower same-row case (two operators editing the *same* assembly's name/lines
concurrently) — the two exception types cover genuinely different failure modes and are not redundant.

**Scope check against SC-003** ("0 cyclic or over-depth structures are ever persisted... under no
interleaving"): the lock is held for the validation read *and* the write in the same transaction, so no
interleaving is possible — this is what makes the guarantee unconditional rather than probabilistic.

## R4 — Per-row optimistic concurrency stays, unchanged from Part's pattern

**Decision**: `Assembly` and `NonPrintedComponent` both carry a Postgres `xmin`-mapped EF Core
concurrency token, exactly like `Part` (`PartConfiguration.cs`: `builder.Property<uint>("xmin").IsRowVersion()`).
`AssemblyRepository`/`NonPrintedComponentRepository` catch `DbUpdateConcurrencyException` from
`SaveChangesAsync` and rethrow as `AssemblyConcurrencyConflictException`/
`NonPrintedComponentConcurrencyConflictException`, mirroring `PartRepository.SaveChangesAsync`
byte-for-byte. This is unrelated to R3 — it's the ordinary "someone edited the same row already" case
R3 doesn't cover (R3 is specifically about *different* rows jointly creating a cycle).

## R5 — Polymorphic BOM line: three nullable FK columns + a discriminator enum, not a generic `(TargetType, TargetId)` pair

**Decision**: `BomLine` carries `TargetKind` (enum: `Part | Assembly | NonPrintedComponent`) plus three
nullable FK columns (`TargetPartId`, `TargetAssemblyId`, `TargetNonPrintedComponentId`), with a DB CHECK
constraint enforcing exactly one is non-null and consistent with `TargetKind` (FR-003).

**Rationale**: a free-form `(string TargetType, Guid TargetId)` pair was considered and rejected — it
gives up real FK referential integrity and, critically, the *differentiated* delete behavior FR-011
needs (deleting a referenced Part/Assembly/NonPrintedComponent must be blocked, naming the referencing
assemblies) for a bespoke integrity check that would have to be reimplemented and kept correct by hand.
Three real FKs, each `ON DELETE RESTRICT`, get that for free as a DB-level backstop underneath the
Application-level check that produces FR-011's named-list error. This mirrors `PartFilamentRequirement`'s
existing pattern of narrow nullable FK fields (`NarrowFilamentVariantId`/`NarrowFilamentProfileId`) for
"at most one of several optional references," just with a required discriminator added since exactly one
(not zero-or-one) must be set here.

`BomLine.AssemblyId` (the *owning* assembly) is a fourth FK to `Assembly`, `ON DELETE CASCADE` — deleting
an assembly deletes its own lines. This is a second, independently-configured FK to the same `Assemblies`
table as `TargetAssemblyId` (`ON DELETE RESTRICT`), which EF Core supports as two distinct navigations
with two distinct delete behaviors on the same target table.

**Duplicate-target rejection (FR-013)** is enforced at the Application layer (checked against the loaded
assembly's current lines before save) and backstopped by three partial unique indexes —
`(AssemblyId, TargetPartId) WHERE TargetPartId IS NOT NULL`, and the equivalent for the other two target
columns — so a race on the same assembly can't slip a duplicate target past a concurrent
in-flight request even before R3's lock would serialize it (defense in depth, not the primary mechanism).

## R6 — Buildable units: a pure Application-layer function over R1's quantity-1 explosion, not its own SQL

**Decision**: `CalculateBuildableUnitsQueryHandler` calls the same `AssemblyGraphQueryService.ExplodeAsync`
from R1 at `quantity: 1` to get each printed part's aggregated per-unit requirement, then applies
`floor(min over printed parts of (suppliedGoodQuantity / perUnitQuantity))` as a small, deterministic,
unit-tested (TDD, per constitution — this is exactly the "filament weight-tracking math"-adjacent
deterministic calculation the constitution names TDD for) C# function — no new SQL. Non-printed
component leaves from the explosion result are dropped before the calculation runs (FR-010b). An
explosion whose printed-part set is empty (only non-printed components anywhere in the tree) returns a
distinct "not applicable" result value (FR-010d) — modeled as a small discriminated result type
(`BuildableUnitsResult` with `IsApplicable`/`Value`/`Reason`), not a sentinel `-1` or `null`, so callers
can't accidentally treat "not applicable" as zero.

## R7 — Assembly detail view: single-level read with client-driven lazy expansion, not an eagerly nested tree

**Decision**: `GetAssemblyQuery` returns one assembly's own direct lines only — each line's target kind,
target name, quantity, and (for a sub-assembly line) that sub-assembly's id — not a deep nested tree.
The frontend's "expandable structure" (FR-014) fetches a sub-assembly's own lines on-demand, by calling
the same query again with that sub-assembly's id, when the operator expands a row.

**Rationale**: FR-014's own language ("browsable... expandable structure") describes progressive
disclosure, not a single eager payload, and this avoids building a *second* independent tree-shaped
serialization to keep in sync with R1's aggregated explosion result — one flat per-assembly read query,
reused recursively by the client, is simpler than a bespoke nested DTO and has no risk of silently
drifting from what explosion actually computes (they're allowed to look different — the detail view
shows raw per-path structure, R1's explosion shows the aggregate — but only one of them needs new
query logic).

## R8 — Non-printed component identity and reuse

**Decision**: `NonPrintedComponent` is a standalone organization-scoped catalog entity (own table, own
CRUD endpoints, own repository), referenced by `BomLine.TargetNonPrintedComponentId` — never inlined
data on a line — exactly as `spec.md`'s own Assumptions section states, mirroring how `Tag`/`Part` are
already reusable catalog entities referenced by, not duplicated onto, the rows that use them.

## Summary of what's genuinely new (no prior art)

Two pieces of infrastructure this feature introduces have no precedent elsewhere in the codebase, the
same "confirmed by direct research, not assumption" framing `007-part-library`'s plan used for its own
new pieces:

1. `AssemblyGraphQueryService` — this codebase's first raw-SQL recursive CTE (`Database.SqlQueryRaw<T>`),
   scoped narrowly to R1's explosion query.
2. The per-organization Postgres advisory lock (R3) — this codebase's first use of
   `pg_advisory_xact_lock`; every prior concurrency guarantee here (`Part`, `Spool`) has been single-row
   optimistic concurrency via `xmin`, which is retained (R4) but is insufficient on its own for FR-007a's
   cross-row invariant.
