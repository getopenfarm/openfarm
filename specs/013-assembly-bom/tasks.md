# Tasks: Phase 6 — Assembly (BOM)

**Input**: Design documents from `/specs/013-assembly-bom/` (plan.md, spec.md, research.md, data-model.md,
contracts/assembly-bom-api.md, quickstart.md)

**Tests**: Included. `plan.md`'s Constitution Check scopes TDD to exactly: the `AssemblyGraph` cycle/depth
algorithm (research.md R2), the buildable-units calculation (research.md R6), and the FluentValidation
gates named in FR-002/FR-004a/b/FR-009e/FR-013. `AssemblyGraphQueryService.ExplodeAsync`'s raw-SQL CTE is
TDD'd via `WebApplicationFactory` + Testcontainers.PostgreSql integration tests (research.md R1 — the
closest available red-green equivalent for SQL text). Everything else (repositories, EF configuration,
orchestration-only command/query handlers, React components/pages) is test-after, per constitution
Principle II and this feature's own plan.md Constitution Check.

**Organization**: Tasks are grouped by user story (spec.md priorities: US1 P1, US2 P1, US3 P2, US4 P3).
FR-006/FR-007/FR-007a/FR-008 (cycle/depth, including under concurrency) and FR-011's Assembly/Part
delete-blocking are spec.md Acceptance Scenarios 3–4 and Edge Cases of **User Story 1** ("Define an
assembly..." — the save-time integrity of the definition layer), so they're scoped to US1 below, not
Foundational. FR-011's NonPrintedComponent delete-blocking is scoped to US4, the story that introduces the
entity.

**Known pre-existing state this plan must account for**: `web/src/features/assemblies/` already contains a
**mock-data-only UI prototype** (`AssembliesPage.tsx`, `AssemblyFormPage.tsx`, `components/ComponentRow.tsx`,
`components/PartPickerDialog.tsx`, `components/SummarySidebar.tsx`, `lib/computeAssemblyTotals.ts`, and
`data/mock*.ts`) built during `007-part-library` (see that feature's `tasks.md` T060 note: "Assemblies
(Phase 6, still mock-only) depended on this store, so its own decoupled copy was relocated to
`web/src/features/assemblies/data/`"). `web/src/app/routes.tsx` and `nav-config.ts` already wire `/assemblies`
and `/assemblies/new`/`/assemblies/:id/edit` to these mock pages. **This plan's plan.md Structure Decision
states "the current `web/` has no assembly UI at all, mock or otherwise" — that statement is stale/incorrect
against the actual repository; the tasks below correct for it** by retiring the mock prototype and its
Part-only, quantity-only, client-computed-totals model in favor of the real API-backed pages, polymorphic
`BomLineEditor`, and server-computed explosion/buildable-units panels the contract defines. No backend
Assembly code exists yet (`api/src/OpenFarm.Domain/Assemblies/` etc. are all new), so plan.md's Structure
Decision is accurate for the backend.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US4, mapping to spec.md's four user stories
- Every task lists its exact file path(s), per `plan.md`'s Project Structure section

## Path Conventions

Existing monorepo layout (unchanged): `api/src/OpenFarm.{Domain,Application,Infrastructure,Api}/`,
`api/tests/OpenFarm.{Domain,Application}.Tests/` + `OpenFarm.Api.IntegrationTests/`,
`web/src/features/assemblies/`, `web/src/features/non-printed-components/`.

---

## Phase 1: Setup

**Purpose**: Confirm the baseline is clean before extending it — no new dependencies, no scaffolding needed
(this feature only adds new files/one migration alongside existing ones, per plan.md).

- [ ] T001 Confirm baseline is green: `dotnet build api/OpenFarm.slnx --configuration Release`, `dotnet test api/OpenFarm.slnx`, `cd web && bun run lint && bunx tsc -b && bun run test`

**Checkpoint**: Baseline confirmed clean — any later failure is attributable to this feature's changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The three new entities, the polymorphic `BomLine` shape, every domain exception, the
repository/query-service contracts, persistence (EF configs + the one migration), and error-mapping
plumbing — every user story below reads or writes through these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain (`api/src/OpenFarm.Domain/Assemblies/`)

- [ ] T002 [P] `BomLineTargetKind.cs` (enum: `Part` \| `Assembly` \| `NonPrintedComponent`)
- [ ] T003 `BomLineSpec.cs` (internal factory-input record: `TargetKind`, three nullable target ids, `Quantity`) + `BomLine.cs` entity (`Id`, `OrganizationId`, `AssemblyId`, `TargetKind`, three nullable target FK properties, `Quantity`; private ctor enforces exactly-one-target FR-003; `Create`/`UpdateQuantity` enforce FR-004a/b whole-number-≥1 for Part/Assembly vs. `>0`-fractional-allowed for NonPrintedComponent) (depends on T002)
- [ ] T004 `Assembly.cs` entity (`Id`, `OrganizationId`, `Name`, `Description?`, `Lines` read-only list, `CreatedAtUtc`/`UpdatedAtUtc`; `Create(organizationId, name, description, lines)` requires `lines.Count >= 1` (FR-002); `Rename`; `AddLine` — FR-013 duplicate-target check against current `Lines`; `UpdateLineQuantity`; `RemoveLine` — throws if it would leave zero lines) (depends on T003)
- [ ] T005 [P] `NonPrintedComponent.cs` entity (`Id`, `OrganizationId`, `Name`, `Unit`, `UnitCost?`, timestamps; `Create`/`Edit`; `Unit` validated non-empty only, `UnitCost` genuinely optional per FR-005c)
- [ ] T006 Domain exceptions, one file each, in `api/src/OpenFarm.Domain/Assemblies/`: `AssemblyNotFoundException.cs`, `NonPrintedComponentNotFoundException.cs`, `BomLineNotFoundException.cs`, `AssemblyMustHaveAtLeastOneLineException.cs`, `DuplicateBomLineTargetException.cs`, `AssemblyCycleException.cs` (carries the offending containment path), `AssemblyDepthLimitExceededException.cs` (carries the limit), `AssemblyConcurrencyConflictException.cs`, `NonPrintedComponentConcurrencyConflictException.cs`, `AssemblyReferencedException.cs` (carries referencing assembly names), `PartReferencedByAssemblyException.cs` (carries referencing assembly names), `NonPrintedComponentReferencedException.cs` (carries referencing assembly names), `InvalidExplosionQuantityException.cs` (depends on T004, T005)

### Application seams (`api/src/OpenFarm.Application/Assemblies/`)

- [ ] T007 [P] `IAssemblyRepository.cs` (CRUD + `GetByIdAsync`/`ExistsAsync`, org-scoped), `INonPrintedComponentRepository.cs` (same shape)
- [ ] T008 [P] `IAssemblyGraphQueryService.cs` — declares `Task<BomExplosionResult> ExplodeAsync(Guid organizationId, Guid rootAssemblyId, int quantity)` (research.md R1) and `Task<IReadOnlyList<(Guid ParentAssemblyId, Guid ChildAssemblyId)>> GetOrganizationEdgesAsync(Guid organizationId)` (feeds R2's `AssemblyGraph`) — both bodies implemented later (`ExplodeAsync` in US2, `GetOrganizationEdgesAsync` in US1)
- [ ] T009 [P] Read-side records in `api/src/OpenFarm.Application/Assemblies/BomExplosionResult.cs`: `BomExplosionResult`, `ExplodedPartQuantity`, `ExplodedNonPrintedComponentQuantity` (data-model.md) and `api/src/OpenFarm.Application/Assemblies/BuildableUnitsResult.cs`: `BuildableUnitsResult` (`IsApplicable`/`Value?`/`NotApplicableReason?`)

### Infrastructure (`api/src/OpenFarm.Infrastructure/Assemblies/` and `Persistence/`)

- [ ] T010 [P] `AssemblyRepository.cs`, `NonPrintedComponentRepository.cs` — implement T007's interfaces; catch `DbUpdateConcurrencyException` from `SaveChangesAsync`, rethrow as `AssemblyConcurrencyConflictException`/`NonPrintedComponentConcurrencyConflictException`, mirroring `PartRepository`'s byte-for-byte pattern (research.md R4)
- [ ] T011 EF configurations in `api/src/OpenFarm.Infrastructure/Persistence/Configurations/`: `AssemblyConfiguration.cs` (`xmin` `IsRowVersion()`, `.IsMultiTenant()`), `NonPrintedComponentConfiguration.cs` (same), `BomLineConfiguration.cs` (`TargetKind` `HasConversion<string>()`; owning-assembly FK `HasForeignKey(l => l.AssemblyId)` `OnDelete(Cascade)`; three nullable target FKs each `OnDelete(Restrict)` incl. a second, independently-configured FK to `Assemblies` for `TargetAssemblyId`; CHECK constraint enforcing exactly one target set and matching `TargetKind`; CHECK `TargetAssemblyId IS NULL OR TargetAssemblyId <> AssemblyId`; three partial unique indexes `(AssemblyId, TargetPartId) WHERE TargetPartId IS NOT NULL` and the equivalent for the other two target columns — FR-013 backstop; `.IsMultiTenant()`) per data-model.md's full table shape (depends on T003, T004, T005)
- [ ] T012 Generate `api/src/OpenFarm.Infrastructure/Persistence/Migrations/<timestamp>_AddAssembliesAndBom.cs` via `dotnet ef migrations add AddAssembliesAndBom` — creates `Assemblies`, `NonPrintedComponents`, `BomLines` with every FK/CHECK/index from T011; no change to any existing table (depends on T011)
- [ ] T013 Map every T006 exception to its HTTP status in the switch expression in `api/src/OpenFarm.Api/ExceptionHandling/DomainExceptionHandler.cs`: `AssemblyNotFoundException`/`NonPrintedComponentNotFoundException`/`BomLineNotFoundException` → 404; `AssemblyMustHaveAtLeastOneLineException`/`AssemblyCycleException`/`AssemblyDepthLimitExceededException` → 422; `DuplicateBomLineTargetException`/`AssemblyConcurrencyConflictException`/`NonPrintedComponentConcurrencyConflictException`/`AssemblyReferencedException`/`PartReferencedByAssemblyException`/`NonPrintedComponentReferencedException` → 409; `InvalidExplosionQuantityException` → 400 (data-model.md's exception table) (depends on T006)
- [ ] T014 Register `IAssemblyRepository`→`AssemblyRepository`, `INonPrintedComponentRepository`→`NonPrintedComponentRepository`, and `IAssemblyGraphQueryService`→`AssemblyGraphQueryService` (stub class created in T010/US1's T017) as scoped services in `api/src/OpenFarm.Api/Program.cs` (depends on T010)

**Checkpoint**: Entities, persistence, and error mapping exist — User Story 1 can now begin.

---

## Phase 3: User Story 1 - Define an assembly from parts, quantities, and sub-assemblies (Priority: P1) 🎯 MVP

**Goal**: An operator creates, edits, browses, and deletes assemblies made of Part/sub-Assembly lines
(NonPrintedComponent lines land in US4), with save-time cycle/depth rejection that holds even under
concurrent cross-assembly saves (FR-001–FR-004a, FR-006–FR-008, FR-011 for Assembly/Part, FR-013–FR-015).

**Independent Test**: quickstart.md Stories 1–3 and 6 (minus the NonPrintedComponent line) — create a
nested assembly, confirm structure/quantities persist and browse correctly; attempt a cycle and an
over-depth save and see both rejected with the named path/limit; race two concurrent cross-assembly saves
and confirm exactly one commits; attempt to delete a referenced Assembly/Part and see it blocked.

### Tests for User Story 1

> T015 is TDD — write first, confirm every case fails before T018 lands. The rest are test-after, written
> alongside/after their implementation.

- [ ] T015 [P] [US1] TDD: `AssemblyGraphTests.cs` in `api/tests/OpenFarm.Domain.Tests/Assemblies/` — direct self-reference rejected; transitive cycle (`Handle → Enclosure → Handle`) rejected, offending path reported; a chain of exactly 10 levels succeeds, an 11th level rejected with the limit stated; an edit to a deep child that would push an *unrelated existing parent* past the limit is rejected (FR-008's upward evaluation, quickstart.md Story 2 step 4); a valid add with no cycle/depth issue is a no-op
- [ ] T016 [P] [US1] Test-after: `AssemblyCrudEndpointTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Assemblies/` — create/list/get/rename/delete round trip, empty-`lines` create rejected (422), last-line delete rejected (409), duplicate-target line add rejected (409), unresolvable `targetId` rejected (404)
- [ ] T017 [P] [US1] Test-after: `ConcurrentCycleRejectionTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Assemblies/` — two independent assemblies X, Y; two parallel `CustomWebApplicationFactory` clients fire `POST .../lines` targeting each other at the same instant (`Task.WhenAll`); assert exactly one `201` and one `409` naming the cycle, and that a subsequent `GET` on both never shows both edges present (FR-007a, quickstart.md Story 3)
- [ ] T018 [P] [US1] Test-after: `DeleteBlockingTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Assemblies/` — deleting a sub-assembly still referenced by a parent is blocked (409, names the parent); deleting a Part referenced by a BOM line is blocked (409, names the assembly); both unblock once the referencing line is removed

### Implementation for User Story 1

- [ ] T019 [US1] `AssemblyGraph.cs` in `api/src/OpenFarm.Domain/Assemblies/` — `FromEdges(edges)`, `ValidateSave(savingAssemblyId, newOrChangedTargetAssemblyId)` per research.md R2's algorithm sketch (cycle: DFS from target downward, reject + report path if the saving assembly is reachable; depth: `rootDistance(saving) + subtreeDepth(target) + 1 > 10` reject + report the limit), to pass T015 (depends on T015)
- [ ] T020 [US1] `OrganizationStructuralLockExtensions.cs` in `api/src/OpenFarm.Infrastructure/Assemblies/` — `AcquireStructuralLockAsync(DbContext, organizationId)` issuing `pg_advisory_xact_lock(hashtext(@organizationId))` via a raw parameterized command on the current transaction's connection (research.md R3)
- [ ] T021 [US1] `AssemblyGraphQueryService.cs` in `api/src/OpenFarm.Infrastructure/Assemblies/` — implement `IAssemblyGraphQueryService.GetOrganizationEdgesAsync` only (`SELECT "AssemblyId", "TargetAssemblyId" FROM "BomLines" WHERE "OrganizationId" = @org AND "TargetKind" = 'Assembly'`); `ExplodeAsync` throws `NotImplementedException` until US2's T032 fills it in (depends on T008)
- [ ] T022 [US1] `CreateAssemblyCommand.cs`/`Handler.cs`/`Validator.cs` in `api/src/OpenFarm.Application/Assemblies/` — validator enforces non-empty `name`, `lines.Count >= 1`, per-line FR-004a/b quantity rules; handler resolves each line's target existence (Part via `IPartRepository`, Assembly via `IAssemblyRepository`, dedup FR-013 client-side before persist), acquires T020's lock, runs T019's `AssemblyGraph.ValidateSave` for every `Assembly`-kind line against T021's edge list, persists inside the same transaction, writes the activity-log entry (`IActivityLoggable`) (depends on T019, T020, T021)
- [ ] T023 [P] [US1] `EditAssemblyCommand.cs`/`Handler.cs`/`Validator.cs` — rename/redescribe only, no line changes
- [ ] T024 [P] [US1] `DeleteAssemblyCommand.cs`/`Handler.cs` — FR-011: query for any `BomLine` targeting this assembly, throw `AssemblyReferencedException` naming every referencing assembly if found, else delete (cascades its own `BomLines` via the DB FK)
- [ ] T025 [US1] `AddBomLineCommand.cs`/`Handler.cs`/`Validator.cs` — validator enforces FR-004a/b quantity rule for the line's `targetKind`; handler resolves target existence, FR-013 duplicate check against the assembly's current lines, and — only when `targetKind == Assembly` — acquires T020's lock and runs T019's `ValidateSave` against T021's fresh edge list before persisting (mirrors T022's create-time path for the single-line-add case) (depends on T019, T020, T021)
- [ ] T026 [P] [US1] `UpdateBomLineQuantityCommand.cs`/`Handler.cs`/`Validator.cs` — quantity-only change, same FR-004a/b rule, no cycle/depth re-check (target is immutable once created, per contracts)
- [ ] T027 [P] [US1] `RemoveBomLineCommand.cs`/`Handler.cs` — throws `AssemblyMustHaveAtLeastOneLineException` if it's the assembly's last line
- [ ] T028 [P] [US1] `GetAssembliesQuery.cs`/`Handler.cs` — paginated list (`id`, `name`, `description`, `lineCount`), mirrors `GetPartsQuery`'s `page`/`pageSize`/`{items,totalCount}` shape
- [ ] T029 [P] [US1] `GetAssemblyQuery.cs`/`Handler.cs` — single-level read: assembly's own fields + each line's `id`/`targetKind`/`targetId`/`targetName`/`quantity` (research.md R7 — no deep nesting; resolves `targetName` per kind)
- [ ] T030 [US1] `CreateAssemblyEndpoint.cs` (`POST /api/v1/assemblies`, `[Permission("assemblies:create")]`, 201/400/404/409), `ListAssembliesEndpoint.cs` (`GET /api/v1/assemblies`, `[Permission("assemblies:view")]`), `GetAssemblyEndpoint.cs` (`GET /api/v1/assemblies/{id}`, `[Permission("assemblies:view")]`, 404), `EditAssemblyEndpoint.cs` (`PUT /api/v1/assemblies/{id}`, `[Permission("assemblies:edit")]`), `DeleteAssemblyEndpoint.cs` (`DELETE /api/v1/assemblies/{id}`, `[Permission("assemblies:delete")]`, 204/404/409) in `api/src/OpenFarm.Api/Endpoints/Assemblies/` (depends on T022–T024, T028, T029)
- [ ] T031 [US1] `AddBomLineEndpoint.cs` (`POST /api/v1/assemblies/{id}/lines`, `[Permission("assemblies:edit")]`, 201/400/404/409), `EditBomLineEndpoint.cs` (`PUT /api/v1/assemblies/{id}/lines/{lineId}`, quantity-only, 200/400/404), `RemoveBomLineEndpoint.cs` (`DELETE /api/v1/assemblies/{id}/lines/{lineId}`, 204/404/409) in `api/src/OpenFarm.Api/Endpoints/Assemblies/` (depends on T025–T027)
- [ ] T032 [US1] Register `CreateAssemblyHandlerType`/`EditAssemblyHandlerType`/`DeleteAssemblyHandlerType`/`AddBomLineHandlerType`/`UpdateBomLineQuantityHandlerType`/`RemoveBomLineHandlerType` and the `GetAssembliesQuery`/`GetAssemblyQuery` `IQueryHandler<>` pairs in `api/src/OpenFarm.Api/Program.cs` (depends on T022–T029)
- [ ] T033 [US1] Rebuild `api/` to regenerate `api/openapi.json` and run `bunx orval` in `web/` to produce `src/lib/api/generated` hooks for every Assembly CRUD/line endpoint built so far (depends on T030, T031)
- [ ] T034 [US1] Delete the mock-only Assembly UI: `web/src/features/assemblies/data/mockAssemblies.ts`, `mockAssembliesStore.ts`, `mockPartsSeed.ts`, `mockPartsStore.ts`, `components/PartPickerDialog.tsx` (+ its test), `components/ComponentRow.tsx`, `components/SummarySidebar.tsx`, `lib/computeAssemblyTotals.ts`/`.test.ts` — confirm no dangling imports remain anywhere in `web/src`
- [ ] T035 [US1] `web/src/features/assemblies/components/BomLineEditor.tsx` — add/edit/remove a line; target-kind picker (Part | Assembly for now, NonPrintedComponent option added in US4's T054); on the edit page, queues pending line changes in local state and flushes them as sequential line-level API calls on "Save" (contracts/assembly-bom-api.md's "Client UX note") — one operator-perceived save, one API call per logical line change, so FR-015's per-line audit log needs no server diffing (depends on T033)
- [ ] T036 [US1] Rewrite `web/src/features/assemblies/AssembliesPage.tsx` — `DataTable` + pagination wired to the generated `GET /assemblies` hook (replacing `useAssemblies()`/mock data), row click navigates to the new detail route (T038), "New assembly" navigates to `/assemblies/new` (depends on T033, T034)
- [ ] T037 [US1] Rewrite `web/src/features/assemblies/AssemblyFormPage.tsx` — react-hook-form + zod against the real `name`/`description`/`lines[]` create shape for `mode="add"` (single `POST /assemblies` call, per the contract's "new assembly" client-UX note) and against `PUT /assemblies/{id}` (rename/description only) + T035's queued line flush for `mode="edit"`, using `BomLineEditor` (T035) in place of `ComponentRow`/`PartPickerDialog`/`SummarySidebar` (depends on T033–T035)
- [ ] T038 [US1] New `web/src/features/assemblies/AssemblyDetailPage.tsx` — read-only browse view (FR-014) wired to `GET /assemblies/{id}`, rendering each line's target/quantity and, for a sub-assembly line, a lazily-fetched expand-in-place (research.md R7 — calls the same query again with that line's `targetId`); "Edit" action navigates to `/assemblies/{id}/edit` (depends on T033)
- [ ] T039 [US1] Add an `:id` route (element `AssemblyDetailPage`) alongside the existing `:id/edit` route under `/assemblies` in `web/src/app/routes.tsx`, and point `AssembliesPage`'s row click (T036) at it instead of straight to edit (depends on T038)
- [ ] T040 [P] [US1] Vitest+RTL tests for `AssembliesPage`/`AssemblyFormPage`/`AssemblyDetailPage`/`BomLineEditor` covering real-API rendering, cycle/depth 409 surfaced as a form error, and last-line-delete 409 in `web/src/features/assemblies/*.test.tsx`

**Checkpoint**: User Story 1 is fully functional and independently testable — run quickstart.md Stories
1, 2, 3, and 6 (minus the non-printed-component line, added in US4).

---

## Phase 4: User Story 2 - Explode an assembly into aggregated part demand (Priority: P1)

**Goal**: A pure, deterministic, side-effect-free explosion endpoint that multiplies and aggregates
quantities down the tree, reporting each printed part exactly once (even across multiple depths) and
non-printed component totals separately, terminating at the max depth rather than recursing unbounded
(FR-009, FR-009a–f).

**Independent Test**: quickstart.md Story 4 — explode a hand-computed nested assembly at several
quantities, confirm flat per-part totals match by hand (including a part at three depths); explode the
same input 100 times and confirm identical results with zero rows changed anywhere; explode the SC-007
500-line/10-level reference assembly and confirm it completes under 1 second; reject quantity 0 and -1.

### Tests for User Story 2

> T041 is TDD — write first against real Testcontainers Postgres, confirm failing before T042 lands
> (research.md R1 — the closest available red-green equivalent for raw SQL).

- [ ] T041 [US2] TDD: `AssemblyExplosionEndpointTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Assemblies/` — SC-001's two reference assemblies (a full 10-level-deep chain; a part appearing at three different depths, asserting it appears exactly once with the summed total); Acceptance Scenario 1's worked example (3×A + 2×B exploded at qty 4 → 12×A, 8×B); Acceptance Scenario 2's diamond case (2 knobs direct + 2×Handle(2 knobs) → 6 knobs, one aggregated line); a non-printed component line reported separately from printed parts (FR-009d); SC-007's 500-line/10-level assembly completing in under 1 second; 100 repeated explosions of the same input returning byte-identical results with zero DB rows changed (SC-002); quantity 0/-1/non-integer rejected 400 (FR-009e)

### Implementation for User Story 2

- [ ] T042 [US2] Implement `ExplodeAsync` in `api/src/OpenFarm.Infrastructure/Assemblies/AssemblyGraphQueryService.cs` (T021) — the literal `WITH RECURSIVE` CTE from research.md R1 via `Database.SqlQueryRaw<T>`, anchored on the root assembly's own lines with the requested quantity as the anchor multiplier, recursive term joining child `BomLines` on `TargetKind = 'Assembly'` with `depth < 10` (FR-009f termination guard — no cycle *validation* here, that's US1's save-time job), final aggregation grouped by `TargetKind`/leaf id, split into `PrintedParts` (`Part`) and `NonPrintedComponents` (`NonPrintedComponent`) result lists (FR-009d), to pass T041 (depends on T041)
- [ ] T043 [P] [US2] `ExplodeAssemblyQuery.cs`/`Handler.cs` in `api/src/OpenFarm.Application/Assemblies/` — validates quantity is a whole number ≥ 1 (throws `InvalidExplosionQuantityException` otherwise, FR-009e), calls T042's `ExplodeAsync`, resolves `PartName`/`Name` display fields (depends on T042)
- [ ] T044 [P] [US2] `ExplodeAssemblyEndpoint.cs` (`GET /api/v1/assemblies/{id}/explosion?quantity={n}`, `[Permission("assemblies:view")]`, 200/400/404) in `api/src/OpenFarm.Api/Endpoints/Assemblies/` (depends on T043)
- [ ] T045 [US2] Register `IQueryHandler<ExplodeAssemblyQuery, BomExplosionResult>` in `api/src/OpenFarm.Api/Program.cs`; rebuild `api/` and run `bunx orval` in `web/` to add the explosion hook to `src/lib/api/generated` (depends on T044)
- [ ] T046 [US2] `web/src/features/assemblies/components/ExplosionResultView.tsx` — renders `printedParts`/`nonPrintedComponents` as two visually separate lists (FR-009d — "no consumer can mistake one set for the other"), with a quantity input driving `GET .../explosion?quantity=` (depends on T045)
- [ ] T047 [US2] Wire `ExplosionResultView` (T046) into `AssemblyDetailPage.tsx` (T038) as an explosion panel
- [ ] T048 [P] [US2] Vitest+RTL tests for `ExplosionResultView` (separate rendering of printed vs. non-printed totals, quantity-driven refetch) in `web/src/features/assemblies/components/ExplosionResultView.test.tsx`

**Checkpoint**: User Story 2 is fully functional and independently testable — run quickstart.md Story 4.

---

## Phase 5: User Story 3 - See how many complete sets are buildable (Priority: P2)

**Goal**: `floor(min over printed components of (good quantity ÷ per-unit aggregated quantity))`, computed
against caller-supplied good quantities, excluding non-printed lines entirely, and reporting "not
applicable" (never 0, never unbounded) when the tree has no printed parts at all (FR-010–FR-010e).

**Independent Test**: quickstart.md Story 5 — `min(floor(10÷2)=5, floor(12÷3)=4)=4`; a zero-good-quantity
component drives the result to 0 even at 90% elsewhere; a part at multiple depths uses its aggregated
per-unit total; non-printed lines present vs. removed produce identical results (SC-005); an all-non-printed
tree reports `isApplicable:false` with a reason, never 0 or unbounded.

### Tests for User Story 3

> T049 is TDD — write first, confirm failing before T050 lands (research.md R6 — deterministic calculation
> the constitution names TDD for).

- [ ] T049 [P] [US3] TDD: `CalculateBuildableUnitsQueryHandlerTests.cs` in `api/tests/OpenFarm.Application.Tests/Assemblies/` — Acceptance Scenario 1's worked example (2×A+3×B/unit, 10 A + 12 B on hand → 4); Scenario 2 (90 A + 0 B → 0); a part at multiple depths uses its aggregated per-unit quantity, not any single line's; non-printed lines excluded entirely and never change the result (against a fake `IAssemblyGraphQueryService.ExplodeAsync(quantity:1)` result); an exploded tree with zero printed parts returns `IsApplicable=false` with a non-null reason, never `0` or unbounded

### Implementation for User Story 3

- [ ] T050 [US3] `CalculateBuildableUnitsQueryHandler.cs` in `api/src/OpenFarm.Application/Assemblies/` — calls `IAssemblyGraphQueryService.ExplodeAsync(quantity: 1)`, drops non-printed leaves (FR-010b), computes `floor(min over printed parts of (suppliedGoodQuantity ?? 0) / perUnitQuantity)` (FR-010c: missing/omitted part in the caller's map treated as 0), returns `BuildableUnitsResult.IsApplicable=false` with a stated reason when the printed-part set is empty (FR-010d), to pass T049 (depends on T049)
- [ ] T051 [P] [US3] `CalculateBuildableUnitsQuery.cs` (`AssemblyId`, `IReadOnlyDictionary<Guid, decimal> GoodQuantities`) in `api/src/OpenFarm.Application/Assemblies/` (depends on T050)
- [ ] T052 [P] [US3] `CalculateBuildableUnitsEndpoint.cs` (`POST /api/v1/assemblies/{id}/buildable-units`, `[Permission("assemblies:view")]`, 200/404) in `api/src/OpenFarm.Api/Endpoints/Assemblies/` (depends on T051)
- [ ] T053 [US3] Register `IQueryHandler<CalculateBuildableUnitsQuery, BuildableUnitsResult>` in `api/src/OpenFarm.Api/Program.cs`; rebuild `api/` and run `bunx orval` in `web/` to add the buildable-units hook (depends on T052)
- [ ] T054 [US3] `web/src/features/assemblies/components/BuildableUnitsPanel.tsx` — a good-quantity input per printed part (sourced from T046's `ExplosionResultView` printed-parts list at quantity 1) driving `POST .../buildable-units`, rendering `isApplicable`/`value`/`notApplicableReason` distinctly (never conflating "not applicable" with 0) (depends on T053)
- [ ] T055 [US3] Wire `BuildableUnitsPanel` (T054) into `AssemblyDetailPage.tsx` (T038) alongside the explosion panel
- [ ] T056 [P] [US3] Vitest+RTL tests for `BuildableUnitsPanel` (renders `isApplicable:false` distinctly from `value:0`) in `web/src/features/assemblies/components/BuildableUnitsPanel.test.tsx`

**Checkpoint**: User Story 3 is fully functional and independently testable — run quickstart.md Story 5.

---

## Phase 6: User Story 4 - Record non-printed components on a BOM (Priority: P3)

**Goal**: A reusable, stock-free `NonPrintedComponent` catalog (name, unit, optional cost) that BOM lines
can target, appearing in structure/explosion but never gating buildable units or any other calculation
(FR-005–FR-005c, FR-011 for NonPrintedComponent).

**Independent Test**: quickstart.md Story 1 step 1 + Story 5 step 3/4 + Story 6 step 3 — create a
non-printed component with and without a cost, add it to an assembly, confirm it appears in structure and
in explosion's separate non-printed totals, confirm buildable units is unchanged with it present vs.
removed, confirm deleting a referenced one is blocked.

### Implementation for User Story 4

- [ ] T057 [P] [US4] `CreateNonPrintedComponentCommand.cs`/`Handler.cs`/`Validator.cs` (non-empty `name`/`unit`, `unitCost` optional) in `api/src/OpenFarm.Application/Assemblies/`
- [ ] T058 [P] [US4] `EditNonPrintedComponentCommand.cs`/`Handler.cs`/`Validator.cs` — same validation shape
- [ ] T059 [P] [US4] `DeleteNonPrintedComponentCommand.cs`/`Handler.cs` — FR-011: query for any `BomLine` targeting this component, throw `NonPrintedComponentReferencedException` naming every referencing assembly if found, else delete
- [ ] T060 [P] [US4] `ListNonPrintedComponentsQuery.cs`/`Handler.cs` — paginated list, same envelope shape as `GetAssembliesQuery`
- [ ] T061 [US4] `CreateNonPrintedComponentEndpoint.cs` (`POST /api/v1/non-printed-components`, `[Permission("non-printed-components:create")]`), `EditNonPrintedComponentEndpoint.cs` (`PUT .../{id}`, `[Permission("non-printed-components:edit")]`), `DeleteNonPrintedComponentEndpoint.cs` (`DELETE .../{id}`, `[Permission("non-printed-components:delete")]`, 409 on referenced), `ListNonPrintedComponentsEndpoint.cs` (`GET /api/v1/non-printed-components`, `[Permission("non-printed-components:view")]`) in `api/src/OpenFarm.Api/Endpoints/NonPrintedComponents/` (depends on T057–T060)
- [ ] T062 [US4] Register the four T057–T060 handler pairs in `api/src/OpenFarm.Api/Program.cs`; rebuild `api/` and run `bunx orval` in `web/` to add the non-printed-component hooks (depends on T061)
- [ ] T063 [US4] Extend `BomLineEditor.tsx`'s (T035) target-kind picker with the `NonPrintedComponent` option (name/unit-labeled picker + fractional quantity input, vs. the whole-number stepper used for Part/Assembly), sourced from the new list hook (depends on T062)
- [ ] T064 [US4] `web/src/features/non-printed-components/NonPrintedComponentsPage.tsx` (`DataTable` + pagination, "New" action) and `NonPrintedComponentFormPage.tsx` (react-hook-form + zod create/edit, Page-first per `docs/UI_CONVENTIONS.md`) wired to T062's hooks (depends on T062)
- [ ] T065 [US4] Add `/non-printed-components`, `/non-printed-components/new`, `/non-printed-components/:id/edit` routes to `web/src/app/routes.tsx` (element T064's pages) and a `NavItem` entry (mirroring the existing `assemblies`/`parts` `NavItem` shape) in `web/src/app/layout/nav-config.ts`'s `Library` section (depends on T064)
- [ ] T066 [P] [US4] Test-after: `NonPrintedComponentEndpointTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Assemblies/` — create/edit/list round trip, empty `name`/`unit` rejected 400, delete-while-referenced blocked 409 naming the assembly, delete unblocked once the referencing line is removed
- [ ] T067 [P] [US4] Test-after: Vitest+RTL tests for `NonPrintedComponentsPage`/`NonPrintedComponentFormPage` and `BomLineEditor`'s new target-kind option in `web/src/features/non-printed-components/*.test.tsx`

**Checkpoint**: All four user stories independently functional — run quickstart.md Stories 1–7 end-to-end.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Formatting/lint gates, activity-log coverage (FR-015), and full end-to-end validation across
all four stories, including SC-006's zero-printer-knowledge proof.

- [ ] T068 [P] `api/tests/OpenFarm.Api.IntegrationTests/ActivityLog/AssemblyBomActivityLogTests.cs` — FR-015: assembly creation, a line add/change/remove, an assembly deletion attempt (blocked) and a successful one, and non-printed component creation/edit all recorded with actor and timestamp
- [ ] T069 [P] `dotnet format api/OpenFarm.slnx --verify-no-changes` (fix any formatting drift from Phases 2–6)
- [ ] T070 [P] `cd web && bun run lint && bun run format && bunx tsc -b` (fix any drift from Phases 3–6)
- [ ] T071 Run `dotnet test api/OpenFarm.slnx` and `cd web && bun run test` full suites, confirm green
- [ ] T072 Execute quickstart.md Stories 1–7 end-to-end against the local stack (`infra/`), on a fresh organization with 0 printers/files/profiles/jobs/runs (SC-006) — confirm every step's expected result, including the SC-007 500-line/10-level perf case timed under 1 second

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — run first.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks every user story.**
- **User Story 1 (Phase 3)**: Depends on Foundational only. No dependency on US2–US4.
- **User Story 2 (Phase 4)**: Depends on Foundational; its endpoint/tests target assemblies that must
  already be createable, so it's built after US1 in practice, though its own new files (`ExplodeAsync`'s
  CTE, `ExplodeAssemblyQuery`, `ExplosionResultView`) don't import anything from US1's application/API
  layer — only `AssemblyDetailPage.tsx` (T038, built in US1) is a shared file US2 extends (T047).
- **User Story 3 (Phase 5)**: Depends on US2's `ExplodeAsync` (T042) being implemented — `CalculateBuildableUnitsQueryHandler` calls it directly at quantity 1. Its own unit tests (T049) use a fake, so the *TDD* portion doesn't literally require T042 to exist, but the integration path does.
- **User Story 4 (Phase 6)**: Depends on Foundational (the `NonPrintedComponent` entity/repository already exist from T005/T007/T010). Independently testable on its own CRUD surface; its BOM-line integration (T063) extends `BomLineEditor.tsx`, a US1 file.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Within Each User Story

- Tests before implementation where TDD is scoped (T015→T019 for US1's `AssemblyGraph`; T041→T042 for
  US2's `ExplodeAsync`; T049→T050 for US3's buildable-units calculation). Test-after tasks (T016–T018,
  T040, T048, T056, T066–T067) are written alongside/after their implementation.
- Domain/Application before Infrastructure before Api before web, within each story.
- Backend endpoints before their Orval regeneration task, before the frontend components that consume them.

### Parallel Opportunities

- T002 (enum) unblocks T003/T005 in parallel; T005 has no dependency on T003/T004 at all.
- T006 (all domain exceptions) is one task by design (mirrors `007-part-library` T015) — file-independent internally but sequenced as a single unit since every exception is small and low-risk to front-load.
- T007, T008, T009 (Application interfaces/records) run in parallel — different files.
- T010 (repositories) parallel with T011 only once T003–T005 exist; T011 itself must be one task since `BomLineConfiguration` cross-references `AssemblyConfiguration`'s and `NonPrintedComponentConfiguration`'s FK targets.
- Within US1: T023, T024 parallel with each other and with T025's chain (different files); T026, T027, T028, T029 all parallel (different files, all depend only on Foundational + T019–T021).
- T040 (US1 frontend tests), T048 (US2), T056 (US3), T066–T067 (US4) are each independently parallelizable within their own story once that story's implementation lands.
- T069 and T070 in parallel (backend vs. frontend tooling).

---

## Parallel Example: Foundational Domain layer

```bash
Task: "BomLineTargetKind.cs enum in api/src/OpenFarm.Domain/Assemblies/BomLineTargetKind.cs"
Task: "NonPrintedComponent.cs entity in api/src/OpenFarm.Domain/Assemblies/NonPrintedComponent.cs"
```

## Parallel Example: User Story 1 read/mutation queries

```bash
Task: "UpdateBomLineQuantityCommand.cs / Handler.cs / Validator.cs"
Task: "RemoveBomLineCommand.cs / Handler.cs"
Task: "GetAssembliesQuery.cs / Handler.cs"
Task: "GetAssemblyQuery.cs / Handler.cs"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 together)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (blocks everything).
3. Complete Phase 3: User Story 1 — a real, browsable, cycle/depth-safe Assembly catalog, replacing the
   old mock UI.
4. Complete Phase 4: User Story 2 — explosion, this phase's headline capability (plan.md Summary: "the
   reason Phase 6 sits ahead of Phase 7 at all").
5. **STOP and VALIDATE**: run quickstart.md Stories 1–4, 6.
6. Deploy/demo if ready — US1+US2 together are spec.md's own framing of this phase's core value.

### Incremental Delivery

1. Setup + Foundational → entities/persistence ready, no user-facing capability yet.
2. Add US1 → real assembly definition + browsing replaces the mock prototype → demoable.
3. Add US2 → explosion → the phase's headline capability works end-to-end.
4. Add US3 → buildable units → the "how many can I box today" answer works.
5. Add US4 → non-printed components → the BOM becomes a truthful, complete product description.
6. Polish (Phase 7) → activity-log coverage, full lint/test pass, quickstart validation.

### Parallel Team Strategy

With multiple developers/agents: complete Setup + Foundational together, then split US1 (definition layer)
and US4 (non-printed components — only genuinely blocked on Foundational) across two people/agents in
parallel, merging carefully on `BomLineEditor.tsx` (US1 creates it in T035, US4 extends it in T063 — treat
as sequential on that one file). US2 starts once US1's `CreateAssemblyEndpoint`/`AddBomLineEndpoint` exist
to build fixtures against; US3 starts once US2's `ExplodeAsync` (T042) lands.

---

## Notes

- [P] tasks touch different files with no unmet dependency.
- [Story] labels map every Phase 3–6 task to spec.md's US1–US4 for traceability.
- TDD tasks (T015, T041, T049) must be written and observed failing before their paired implementation
  tasks land, per constitution Principle II and plan.md's Constitution Check.
- Commit after each task or logical group; run `dotnet format`/`bun run lint` before committing (CI-enforced).
- `AssemblyGraphQueryService.cs` is touched by more than one phase (US1's T021 implements
  `GetOrganizationEdgesAsync`; US2's T042 implements `ExplodeAsync`) — treat those two edits as sequential,
  not parallel, even though they land in different story phases.
- `AssemblyDetailPage.tsx` is touched by three phases (US1 creates it, T038; US2 adds the explosion panel,
  T047; US3 adds the buildable-units panel, T055) — sequential, not parallel, across those three edits.
- `BomLineEditor.tsx` is touched by two phases (US1 creates it, T035; US4 extends its target-kind picker,
  T063) — sequential, not parallel.
- T034 (retiring the mock UI) must land before T036/T037 rewrite the same files it deletes — sequenced
  within Phase 3 above, not parallel, despite both being marked in the same story.
- Avoid: vague tasks, same-file conflicts run in parallel, cross-story dependencies that break independence
  beyond the shared files explicitly called out above.
