# Implementation Plan: Phase 6 — Assembly (BOM)

**Branch**: `013-assembly-bom` | **Date**: 2026-08-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/013-assembly-bom/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

A pure definition layer — no printers, no live connections, no files, no jobs/runs (those are Phase 7's,
FR-016/FR-017) — that lets an operator describe a finished product as a named `Assembly` made of one or
more polymorphic `BomLine`s, each targeting exactly one of a catalog `Part` (Phase 4), another `Assembly`
(a sub-assembly, nestable to a spec-fixed maximum of 10 levels), or a new reusable, stock-free
`NonPrintedComponent` (screws, inserts, packaging). Two calculations sit on top of that structure: **BOM
explosion** (FR-009), a pure, deterministic function multiplying and aggregating quantities down the tree
for a requested build quantity, reporting each printed part exactly once even when it appears at several
depths (with non-printed totals reported separately, FR-009d) and no per-path breakdown (dropped in this
phase's Clarifications session); and **buildable units** (FR-010),
`floor(min over printed components of (good quantity ÷ quantity per assembly))`, computed against
caller-supplied good quantities (Phase 8 supplies real ones later) and excluding non-printed lines
entirely. The two hardest correctness requirements are save-time graph integrity: a create/edit MUST be
rejected if it would create a direct/transitive cycle or exceed the depth limit, evaluated across the
*whole* containment graph the saved assembly participates in (both its own subtree and every assembly
that already includes it, FR-008), and that guarantee MUST hold even when two saves to *different*
assemblies commit concurrently and each is individually valid against the state it read (FR-007a) — no
interleaving may ever let a cycle persist, even momentarily. Technical approach, in brief (full reasoning
in `research.md`): BOM explosion runs as a literal Postgres `WITH RECURSIVE` CTE at the database layer
(constitution-mandated for BOM/assembly decomposition, and the only way to credibly hit SC-007's
500-line/10-level/<1s bound); cycle/depth validation instead loads the organization's much smaller
Assembly→Assembly edge list into memory and runs as a pure, TDD'd Domain-layer graph algorithm — a
deliberate split from the CTE, reasoned through in `research.md` R2; and the concurrent-cycle guarantee
is enforced by a transaction-scoped Postgres advisory lock keyed per organization (`research.md` R3) —
this codebase's first use of either raw recursive SQL or an advisory lock, alongside `Part`'s existing
`xmin` optimistic-concurrency pattern (retained, unchanged, for ordinary same-row edit conflicts).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`api/`); TypeScript 5 (`strict: true`) / React 19 (`web/`) —
unchanged from prior phases.

**Primary Dependencies**: FastEndpoints, EF Core 10 + Npgsql, FluentValidation (backend); React Router v7,
TanStack Query, react-hook-form + zod, shadcn/ui, Orval-generated API client (frontend) — all pre-existing,
no new NuGet or npm package introduced by this feature. The recursive-CTE explosion query
(`research.md` R1) and the advisory-lock helper (`research.md` R3) both use APIs already available through
EF Core/Npgsql (`Database.SqlQueryRaw<T>`, a plain parameterized SQL statement over the same
`NpgsqlConnection`) — no new client library.

**Storage**: PostgreSQL via the existing `OpenFarmDbContext`. One new migration adding three tables:
`Assemblies` (name, optional description, `xmin` concurrency token — mirrors `Part`), `NonPrintedComponents`
(name, unit, optional unit cost, `xmin`), and `BomLines` (owning `AssemblyId` FK `ON DELETE CASCADE`,
`TargetKind` enum + three nullable target FKs each `ON DELETE RESTRICT`, `Quantity` as `numeric` to admit
non-printed components' optional fractional quantities, a CHECK constraint enforcing exactly one target
column is set and matches `TargetKind`, and three partial unique indexes backstopping FR-013's
one-target-per-assembly rule — full detail in `data-model.md`). No changes to any existing table.

**Testing**: xUnit + FluentAssertions, TDD, for the `AssemblyGraph` cycle/depth algorithm (`research.md`
R2) and the buildable-units calculation (`research.md` R6) — both deterministic, well-specified logic the
constitution explicitly names TDD for. xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql,
written test-first against the real recursive CTE (the closest available red-green equivalent for raw SQL,
`research.md` R1), for `AssemblyGraphQueryService.ExplodeAsync` — covering SC-001's two reference
assemblies (full 10-level depth; a part at three depths) and SC-007's 500-line perf case — plus ordinary
test-after integration coverage for every endpoint, the FR-007a concurrent-cycle scenario (two parallel
`WebApplicationFactory` clients racing X→Y vs. Y→X), and FR-011's delete-blocking. Vitest + React Testing
Library, test-after, for the new `web/src/features/assemblies/` and
`web/src/features/non-printed-components/` pages/components.

**Target Platform**: Linux server (API host) + browser SPA — unchanged; no new platform surface.

**Project Type**: Web application (existing `api/` + `web/` monorepo) — no new project/service.

**Performance Goals**: SC-007 — an assembly of 500 total BOM lines across the full 10 levels of nesting
explodes in under 1 second; met by R1's single-round-trip recursive CTE rather than N+1 application-side
traversal. No other perf-sensitive path — cycle/depth validation (R2) and structural-write serialization
(R3) run on low-frequency catalog-authoring commands, not a hot/real-time path, so an in-memory DFS and a
per-organization advisory lock carry no meaningful cost here.

**Constraints**: FR-016 — every operation in this phase MUST succeed with zero printers, connections,
profiles, or files in existence; nothing in this feature may take a dependency on any Phase 1–5
printer-connectivity type. FR-009/FR-009f — explosion MUST be side-effect-free and MUST terminate at the
max depth rather than recurse unbounded. FR-007/FR-007a/FR-008 — cycle and depth rejection MUST be
unconditional, evaluated bidirectionally, and MUST hold under any interleaving of concurrent saves, not
just within one transaction's own view. FR-009c — no per-path breakdown may be computed, stored, or
returned in this phase (spec Clarifications, 2026-08-06) — `docs/FEATURE_PLAN.md`'s Phase 6 prose still
describes retaining one as display-only metadata, which this spec's own later clarification session
explicitly supersedes; noted here so the discrepancy isn't mistaken for a design gap during implementation.

**Scale/Scope**: Single self-hosted organization scope, unchanged from prior phases. SC-007's 500-line/
10-level assembly is this feature's own upper bound on a *single* explosion; `research.md` R2 separately
assumes an organization's total Assembly→Assembly edge count stays small enough (hundreds, not millions —
assemblies are hand-authored catalog entities, not per-job data) to hold in memory for cycle/depth
validation without needing its own pagination or streaming strategy.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture, Enforced by the Compiler and CI** — PASS. `Assembly`, `BomLine`,
  `NonPrintedComponent`, `BomLineTargetKind`, `AssemblyGraph` (the cycle/depth algorithm), and every
  domain exception live in `Domain/Assemblies/` — framework-agnostic, no FastEndpoints/EF references.
  `Application/Assemblies/` defines the repository/query-service contracts
  (`IAssemblyRepository`, `INonPrintedComponentRepository`, `IAssemblyGraphQueryService`) and every
  command/query handler; `Infrastructure/Assemblies/` implements all three, including the raw-SQL
  `AssemblyGraphQueryService` and the advisory-lock acquisition, following the exact
  `Infrastructure`-implements-`Application`-contracts shape `PartRepository`/`PartProcessingBackgroundService`
  already establish. `Api` endpoints stay thin FastEndpoints REPR adapters. No FastEndpoints/EF Core
  reference crosses into `Domain`/`Application`.
- **II. Test Discipline Where It Pays, Not Uniformly** — PASS. TDD is scoped to exactly the deterministic,
  well-specified pieces the constitution names for this feature type: the `AssemblyGraph` cycle/depth
  algorithm and the buildable-units calculation (`research.md` R2/R6), plus the FluentValidation gates
  (line-quantity rules FR-004a/b, explosion-quantity FR-009e, the "at least one line" FR-002 rule).
  `AssemblyGraphQueryService.ExplodeAsync`'s raw-SQL CTE is TDD'd via Testcontainers-backed integration
  tests instead of pure unit tests, since it's SQL text, not a unit-testable C# algorithm — still written
  test-first against SC-001's hand-computed reference assemblies. Test-after for orchestration-only
  command/query handlers, EF configuration, the repositories, and every React component/page.
- **III. Consistent User Experience Across the Fleet** — PASS. No SignalR/real-time surface is introduced
  or required — this phase has no live/streaming data (FR-016), so nothing here risks a manual-refresh
  fallback the constitution would otherwise flag. Forms use react-hook-form + zod; primitives come from
  `src/components/ui/`; Add/Edit follow the Page-first convention (`docs/UI_CONVENTIONS.md`) for both
  Assembly and NonPrintedComponent. Every domain exception gets one `DomainExceptionHandler` mapping to
  RFC 7807 `ProblemDetails`, the same single error shape every other feature already uses.
- **IV. Build for Expansion, Not Speculation** — PASS. `Assembly`/`BomLine`/`NonPrintedComponent` are all
  `organization_id`-scoped from the first migration, via the existing Finbuckle `IsMultiTenant()` pattern
  — no bespoke tenant filter. No speculative abstraction: explosion doesn't pre-build a per-path
  breakdown Phase 7/8 doesn't need yet (FR-009c, deliberately dropped); buildable units doesn't try to
  source or persist "good quantities" itself (FR-010e — that's Phase 8's job); non-printed components get
  no stock/inventory fields despite looking adjacent to `Spool`/`FeedSlot` (FR-005a) because nothing in
  this phase or its stated consumers (Phase 18) needs them to have any. Conversely, this *is* real,
  currently-needed extension: FR-003's polymorphic `BomLine` is built now specifically because retrofitting
  polymorphism onto an already-shipped single-target line later would mean migrating every existing row
  (spec Assumptions) — the same "build it now because deferring costs a migration of live rows" reasoning
  `007-part-library` already used for `PartFilamentRequirement`.
- **V. Performance and Real-Time Responsiveness by Construction** — PASS. BOM decomposition (explosion)
  runs as a database-layer recursive CTE, exactly as this principle names — not application-side
  recursion (`research.md` R1). No `BackgroundService`, no unbounded list needing `TanStack Virtual` (an
  assembly's own line list and the org's assembly-browse list are both small, catalog-sized collections,
  not a job queue). No polling anywhere — this phase has nothing to push in real time to begin with.
- **Security & Multi-Tenancy Isolation** — PASS. `Assembly`, `BomLine`, `NonPrintedComponent` all use
  `IsMultiTenant()` exclusively — no hand-rolled tenant `WHERE` clause, including inside the raw-SQL
  explosion query, which takes `@organizationId` as an explicit parameter on every CTE anchor/recursive
  term rather than relying on EF's query filter (raw SQL bypasses it) — called out explicitly here since
  it's the one place in this feature a missed tenant filter would be a real cross-tenant leak, not a
  theoretical one. Every mutating endpoint carries a real `[Permission("assemblies:...")]`/
  `[Permission("non-printed-components:...")]` attribute, matching the existing convention. Every
  mutating endpoint's Request DTO implements `IActivityLoggable` (FR-015 — creation, edits including line
  add/change/remove, deletions, and non-printed-component creation/edits all land in the shared activity
  log via the existing post-processor, structurally, not by per-feature convention). Auth is the existing
  real JWT flow throughout — no new unauthenticated surface.

No violations requiring justification — **Complexity Tracking below records genuinely new infrastructure
this feature introduces (the recursive CTE, the advisory lock) for visibility, not as deviations**, the
same framing `007-part-library`'s plan used for its own new pieces.

**Post-Phase-1-design re-check** (per `research.md`/`data-model.md`/`contracts/`): confirms all of the
above with no new violations. `AssemblyGraphQueryService` living in `Infrastructure.Assemblies` and being
depended on by `Application.Assemblies` handlers only through its `Application`-defined interface
(`IAssemblyGraphQueryService`) mirrors the exact `Infrastructure`-implements-`Application`-contract shape
already established throughout this codebase — the raw SQL and the advisory lock are both fully contained
inside that one `Infrastructure` implementation, never leaking upward.

## Project Structure

### Documentation (this feature)

```text
specs/013-assembly-bom/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
│   └── assembly-bom-api.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
api/src/OpenFarm.Domain/Assemblies/
  Assembly.cs                          # entity — see data-model.md
  BomLine.cs / BomLineSpec.cs           # entity + internal factory-input record
  BomLineTargetKind.cs                  # enum: Part, Assembly, NonPrintedComponent
  NonPrintedComponent.cs                # entity
  AssemblyGraph.cs                      # pure cycle/depth algorithm — TDD target (research.md R2)
  AssemblyNotFoundException.cs / NonPrintedComponentNotFoundException.cs / BomLineNotFoundException.cs
  AssemblyMustHaveAtLeastOneLineException.cs / DuplicateBomLineTargetException.cs
  AssemblyCycleException.cs / AssemblyDepthLimitExceededException.cs
  AssemblyConcurrencyConflictException.cs / NonPrintedComponentConcurrencyConflictException.cs
  AssemblyReferencedException.cs / PartReferencedByAssemblyException.cs / NonPrintedComponentReferencedException.cs
  InvalidExplosionQuantityException.cs

api/src/OpenFarm.Application/Assemblies/
  IAssemblyRepository.cs / INonPrintedComponentRepository.cs
  IAssemblyGraphQueryService.cs          # ExplodeAsync (R1) + GetOrganizationEdgesAsync (feeds R2's AssemblyGraph)
  CreateAssemblyCommand.cs / Handler / Validator
  EditAssemblyCommand.cs / Handler / Validator            # rename/description only
  DeleteAssemblyCommand.cs / Handler                        # FR-011 reference check before delete
  AddBomLineCommand.cs / Handler / Validator                # runs AssemblyGraph.ValidateSave inside the advisory-lock transaction
  UpdateBomLineQuantityCommand.cs / Handler / Validator
  RemoveBomLineCommand.cs / Handler
  CreateNonPrintedComponentCommand.cs / Handler / Validator
  EditNonPrintedComponentCommand.cs / Handler / Validator
  DeleteNonPrintedComponentCommand.cs / Handler
  GetAssembliesQuery.cs / Handler                           # paginated list
  GetAssemblyQuery.cs / Handler                              # single-level read (research.md R7)
  ListNonPrintedComponentsQuery.cs / Handler
  ExplodeAssemblyQuery.cs / Handler
  CalculateBuildableUnitsQuery.cs / Handler                  # calls ExplodeAsync(quantity:1) + pure floor/min (research.md R6)

api/src/OpenFarm.Infrastructure/Assemblies/
  AssemblyRepository.cs / NonPrintedComponentRepository.cs   # xmin concurrency catch, mirrors PartRepository
  AssemblyGraphQueryService.cs           # implements IAssemblyGraphQueryService — the recursive WITH RECURSIVE
                                           # CTE (research.md R1) + the flat edge-list query feeding AssemblyGraph
  OrganizationStructuralLockExtensions.cs # pg_advisory_xact_lock(hashtext(organizationId)) helper (research.md R3)
  Persistence/Configurations/AssemblyConfiguration.cs / BomLineConfiguration.cs / NonPrintedComponentConfiguration.cs
    # BomLineConfiguration: TargetKind HasConversion<string>(), the three nullable target FKs (two distinct
    #   FKs to Assemblies with different OnDelete behaviors — data-model.md), the CHECK constraints and
    #   three partial unique indexes via HasCheckConstraint()/HasIndex().HasFilter()
  Persistence/Migrations/<timestamp>_AddAssembliesAndBom.cs

api/src/OpenFarm.Api/
  Endpoints/Assemblies/
    CreateAssemblyEndpoint.cs / EditAssemblyEndpoint.cs / DeleteAssemblyEndpoint.cs
    ListAssembliesEndpoint.cs / GetAssemblyEndpoint.cs
    AddBomLineEndpoint.cs / EditBomLineEndpoint.cs / RemoveBomLineEndpoint.cs
    ExplodeAssemblyEndpoint.cs / CalculateBuildableUnitsEndpoint.cs
  Endpoints/NonPrintedComponents/
    CreateNonPrintedComponentEndpoint.cs / EditNonPrintedComponentEndpoint.cs
    DeleteNonPrintedComponentEndpoint.cs / ListNonPrintedComponentsEndpoint.cs
  ExceptionHandling/DomainExceptionHandler.cs   # + this feature's exception -> status mappings

api/tests/
  OpenFarm.Domain.Tests/Assemblies/AssemblyGraphTests.cs             # TDD (research.md R2) — cycle/depth cases
  OpenFarm.Application.Tests/Assemblies/CalculateBuildableUnitsQueryHandlerTests.cs   # TDD (research.md R6)
  OpenFarm.Application.Tests/Assemblies/*CommandHandlerTests.cs      # test-after, orchestration only
  OpenFarm.Api.IntegrationTests/Assemblies/
    AssemblyExplosionEndpointTests.cs         # TDD against real Testcontainers Postgres (research.md R1) —
                                                 # SC-001's two reference assemblies + SC-007's 500-line case
    ConcurrentCycleRejectionTests.cs           # FR-007a — two parallel WebApplicationFactory clients racing
    AssemblyCrudEndpointTests.cs / NonPrintedComponentEndpointTests.cs / DeleteBlockingTests.cs   # test-after

web/src/features/assemblies/
  AssembliesPage.tsx                    # catalog list (DataTable + pagination)
  AssemblyDetailPage.tsx                 # expandable structure (lazy per-row fetch, research.md R7),
                                           # explosion panel, buildable-units panel
  AssemblyFormPage.tsx                   # new/edit — Page-first (docs/UI_CONVENTIONS.md), react-hook-form + zod
  components/BomLineEditor.tsx           # add/edit/remove a line; target-kind picker (Part/Assembly/NonPrintedComponent)
  components/ExplosionResultView.tsx     # printedParts / nonPrintedComponents, rendered separately (FR-009d)
  components/BuildableUnitsPanel.tsx     # good-quantity inputs per printed part -> isApplicable/value/reason
  *.test.tsx                              # test-after, Vitest + RTL

web/src/features/non-printed-components/
  NonPrintedComponentsPage.tsx / NonPrintedComponentFormPage.tsx / *.test.tsx
```

**Structure Decision**: Existing four-project Clean Architecture backend + single-SPA frontend, extended
in place — no new top-level project, following the same "extend, don't parallel-build" precedent
`012-build-plate-selection` and `007-part-library` both used. `Assemblies/` is a new feature folder at
every backend layer (there is no existing "assembly-shaped" code to extend), and a new `web/src/features/`
pair, since neither has any prior partial implementation to build on (the current `web/` has no assembly
UI at all, mock or otherwise).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. The two genuinely new pieces of infrastructure below are recorded for
visibility, not because either is a deviation — the same framing `007-part-library`'s plan used for its
own new pieces:

| Decision | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| A literal `WITH RECURSIVE` SQL CTE (`AssemblyGraphQueryService.ExplodeAsync`) — this codebase's first raw-SQL query of any kind | The constitution mandates BOM/assembly decomposition run as a database-layer recursive CTE specifically so correctness and latency don't degrade with nesting depth, and SC-007 (500 lines/10 levels/<1s) is a real, testable bound application-side N+1 traversal would put at risk | EF-Core-idiomatic application-side recursion (load an assembly, then loop loading each sub-assembly's own lines) was the more conventional option for this codebase, but is exactly the pattern the constitution calls out by name as insufficient for this specific operation |
| A per-organization Postgres advisory lock (`pg_advisory_xact_lock`) serializing every structural BOM write — this codebase's first use of an advisory lock, alongside the existing per-row `xmin` pattern (retained, not replaced) | FR-007a requires the no-cycle guarantee to hold even when two saves to *different* assemblies commit concurrently and each is individually valid against what it read — `Part`'s existing single-row optimistic-concurrency precedent (`xmin`) cannot detect this, because neither assembly's own row version conflicts with the other's write (`research.md` R3) | Serializable transaction isolation with client-side retry-on-conflict was considered and rejected as unnecessary complexity for a low-frequency, non-hot-path catalog-authoring operation — the advisory lock gets the same unconditional guarantee with a simpler, more directly testable mechanism (Story 3, `quickstart.md`) |
| Cycle/depth validation (`AssemblyGraph`) loads the organization's flat Assembly→Assembly edge list and runs a pure in-memory DFS, rather than a *second* recursive CTE alongside explosion's | The constitution's CTE mandate is specifically about decomposition (quantity multiplication/aggregation over the full tree including Part-level rows) — validation never touches a Part/NonPrintedComponent row, only the much smaller Assembly-to-Assembly edge subgraph, so a pure, exhaustively unit-testable Domain algorithm satisfies constitution Principle II's TDD mandate for "BOM/assembly recursive decomposition" more directly than a second opaque SQL implementation would (`research.md` R2) | A second `WITH RECURSIVE` query mirroring R1's, run for validation instead of explosion, was considered and rejected — it would mean two independent recursive-traversal implementations to keep correct and in sync, and neither would be unit-testable the way a plain C# graph algorithm over an in-memory adjacency map is |
