---

description: "Task list for Fleet- and Inventory-Aware Filament Matching"
---

# Tasks: Fleet- and Inventory-Aware Filament Matching

**Input**: Design documents from `/specs/010-filament-matching/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md (all present; no `contracts/` — this feature changes no request/response shape)

**Tests**: Included, test-after per this codebase's convention for orchestration-layer `Application` code (Constitution Principle II — `RepresentativeFilamentResolver`/`ResolveSlicingProfileSelection` are orchestration over repositories, not a `Domain` state machine or weight-tracking math, so tests follow implementation rather than preceding it).

**Organization**: Tasks are grouped by user story (spec.md priorities: US1/US2 = P1, US3 = P2). US1 is fully independent of US2/US3 — it fixes the STL-add bug using the resolver's *existing*, unmodified signature. US2 replaces that resolver with the tiered matcher; US3 extends US2's tiering with one more tier. There is no Foundational phase: nothing is shared/blocking across all three stories (see Dependencies below).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3, per spec.md
- File paths are exact, relative to repo root

---

## Phase 1: Setup

**Purpose**: This feature modifies existing files in existing projects only — no new project, package, or scaffolding is needed (per plan.md's Technical Context). One sanity task establishes the pre-change baseline.

- [ ] T001 Confirm baseline: `dotnet build api/OpenFarm.slnx` succeeds and `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~RepresentativeFilamentResolver|FullyQualifiedName~ResolveSlicingProfileSelection"` passes on the current branch before any change below, so later failures are attributable to this feature's edits.

---

## Phase 2: Foundational

**Purpose**: N/A for this feature — there is no schema change, no new shared type required before *any* story can start, and no DI wiring gap (`RepresentativeFilamentResolver`'s constructor dependencies added in US2 — `ISpoolRepository`, `IFilamentRepository`, `IFeedUnitRepository` — are already registered in `api/src/OpenFarm.Api/Program.cs:255-257`, so `AddScoped<RepresentativeFilamentResolver>()` at line 301 needs no change). US1 and US2 are independent of each other; US3 depends only on US2's own resolver shape. Proceed directly to Phase 3.

---

## Phase 3: User Story 1 - Adding an STL part no longer fails on filament resolution (Priority: P1) 🎯 MVP

**Goal**: Fix the "Generic PLA not found" defect — an STL's fallback material resolves as a real material type (`"PLA"`), never an engine-specific literal profile name, using the resolver's existing (not-yet-retiered) matching logic.

**Independent Test**: Upload an STL with no printer targeted against an organization with System PLA profiles already ingested; verify success with a resolved PLA filament and no "not found" failure. Separately, against an organization with zero PLA System profiles, verify a failure that names `"PLA"` (not `"Generic PLA"`).

### Implementation for User Story 1

- [ ] T002 [P] [US1] Replace `Dictionary<SlicerEngine, string> GenericFallbackMaterialProfile` with `string FallbackMaterialType = "PLA"` in `api/src/OpenFarm.Infrastructure/Parts/SlicerOptions.cs`, updating its doc comment to describe a single engine-independent material type (data-model.md "Changed configuration").
- [ ] T003 [P] [US1] Change `ISlicerDefaultsProvider.GetGenericFallbackMaterialProfile(SlicerEngine engine)` to `GetFallbackMaterialType()` (no parameter) in `api/src/OpenFarm.Application/Parts/ISlicerDefaultsProvider.cs`.
- [ ] T004 [US1] Update `SlicerDefaultsProvider` in `api/src/OpenFarm.Infrastructure/Parts/SlicerDefaultsProvider.cs` to implement the renamed method, returning `options.Value.FallbackMaterialType` directly with no dictionary lookup and no `InvalidOperationException` path (data-model.md). Depends on T002, T003.
- [ ] T005 [P] [US1] Update the call site in `api/src/OpenFarm.Application/Parts/AddPartCommandHandler.cs` (~line 57): `slicerDefaults.GetGenericFallbackMaterialProfile(slicerUsed.Engine)` → `slicerDefaults.GetFallbackMaterialType()`. Depends on T003.
- [ ] T006 [P] [US1] Update the equivalent call site in `api/src/OpenFarm.Application/Parts/ReplacePartFileCommandHandler.cs` (~line 51), same rename. Depends on T003.

### Tests for User Story 1 (test-after)

- [ ] T007 [P] [US1] Add `SlicerDefaultsProviderTests` in `api/tests/OpenFarm.Infrastructure.Tests/Parts/SlicerDefaultsProviderTests.cs` asserting `GetFallbackMaterialType()` returns `"PLA"` by default and reflects a configured override — no test file for this class exists today. Depends on T004.
- [ ] T008 [US1] Add a regression integration test in `api/tests/OpenFarm.Api.IntegrationTests/Parts/` reproducing the exact reported bug end-to-end: seed an organization with an ingested System PLA filament profile, `POST` an STL via `/api/v1/parts`, assert it succeeds with no `SlicingProfileUnconfiguredException` naming `"Generic PLA"`/`"Bambu PLA Basic"`; separately, seed an organization with zero PLA System profiles and assert the failure names material `"PLA"` (FR-003, spec Acceptance Scenario 3). Depends on T005, T006.

**Checkpoint**: The reported bug is fixed and independently verified — this alone is a shippable MVP, using the resolver's current (pre-US2) matching logic.

---

## Phase 4: User Story 2 - Filament resolution prefers what the fleet already has loaded (Priority: P1)

**Goal**: Replace the resolver's "any System catalog profile" pick with a tiered match that prefers a spool loaded on the target printer, then any fleet-loaded spool, before falling back to the System catalog.

**Independent Test**: With a printer in the fleet loaded with a matching-material spool and unrelated System profiles of the same material also available, verify the loaded spool's filament is chosen (fleet-wide, and preferentially the target printer's own when one is specified).

### Implementation for User Story 2

- [ ] T009 [P] [US2] Add `FilamentMatchSource` enum (`FleetLoaded` | `FilamentLibrary` | `SystemCatalog`) in `api/src/OpenFarm.Application/Profiles/FilamentMatchSource.cs` (data-model.md).
- [ ] T010 [US2] Add `FilamentMatchResult` record (`FilamentProfile Profile, FilamentMatchSource Source`) in `api/src/OpenFarm.Application/Profiles/FilamentMatchResult.cs`. Depends on T009.
- [ ] T011 [US2] Rewrite `RepresentativeFilamentResolver` in `api/src/OpenFarm.Application/Profiles/RepresentativeFilamentResolver.cs`: new signature `Task<FilamentMatchResult> ResolveAsync(Guid? printerId, string materialType, CancellationToken cancellationToken)`; add constructor dependencies `ISpoolRepository`, `IFilamentRepository`, `IFeedUnitRepository` alongside the existing `IFilamentProfileRepository`; implement tier 1 (a `Using` spool on the target printer, when `printerId` is given, joined `FilamentVariant → Filament` on `MaterialType` with a resolvable `Filament.FilamentProfileId`) and tier 2 (any fleet-wide `Using` spool meeting the same criteria, tie-broken by owning printer `Id` then `Spool.Id` ascending via `IFeedUnitRepository.GetByFeedSlotIdAsync`), falling through to the existing System-catalog body (now the final tier, wrapped as `FilamentMatchSource.SystemCatalog`) when neither tier finds an eligible candidate (research.md R2, R3). Depends on T009, T010.
- [ ] T012 [US2] Update `ResolveSlicingProfileSelection.ResolveAsync` in `api/src/OpenFarm.Application/Parts/ResolveSlicingProfileSelection.cs` (~line 48): pass `printerId` through to `representativeFilamentResolver.ResolveAsync(printerId, materialType, cancellationToken)` and unwrap `.Profile` immediately for the existing JSON-merge flow — `ResolvedSlicingProfiles`'s own shape is unchanged. Depends on T011.

### Tests for User Story 2 (test-after)

- [ ] T013 [P] [US2] Add `FakeSpoolRepository`, `FakeFilamentRepository` (`OpenFarm.Application.FilamentTracking`'s, distinct from the existing `FakeFilamentProfileRepository`), and `FakeFeedUnitRepository` under `api/tests/OpenFarm.Application.Tests/Profiles/Fakes/`, matching the existing `FakeFilamentProfileRepository` in-memory style — no fakes for these three repositories exist yet.
- [ ] T014 [US2] Update `RepresentativeFilamentResolverTests` in `api/tests/OpenFarm.Application.Tests/Profiles/RepresentativeFilamentResolverTests.cs` for the new constructor/signature (existing 5 tests: pass `printerId: null`, unwrap `.Profile`, assert `.Source == FilamentMatchSource.SystemCatalog`); add new cases covering spec US2's acceptance scenarios: a target printer's own loaded spool wins over a fleet-wide loaded spool (Scenario 3), a fleet-wide loaded spool wins over unrelated System profiles when no printer is targeted (Scenario 1), the choice is stable across repeated calls with two printers loaded (Scenario 2), a wrong-material loaded spool is never chosen (Scenario 4), and — per research.md R4 — a fleet-loaded spool on a printer with `IsReachable = false` still wins the fleet-loaded tier (no availability filtering). Depends on T011, T013.
- [ ] T015 [US2] Update `ResolveSlicingProfileSelectionTests`'s `CreateSelection` helper in `api/tests/OpenFarm.Application.Tests/Parts/ResolveSlicingProfileSelectionTests.cs` for `RepresentativeFilamentResolver`'s new constructor, and add two cases: (a) a printer-targeted resolution prefers that printer's own loaded spool over the System catalog; (b) FR-011 regression — set up a fleet-loaded spool that would win tier 1/2 if matching ran, call `ResolveAsync` with a non-null `narrowedFilamentProfileId` pointing at a different, distinguishable `FilamentProfile`, and assert the returned `FilamentSettingsJson` reflects the narrowed profile, not the fleet-loaded one — the matching capability must never even run when a requirement is narrowed. Depends on T012, T013.

**Checkpoint**: Fleet-loaded filament is preferred over an arbitrary System catalog pick, independently testable/demoable on top of US1 (whether or not US1 has shipped yet — US2's tests exercise the resolver directly with `materialType: "PLA"`, bypassing `AddPartCommandHandler` entirely).

---

## Phase 5: User Story 3 - Filament resolution falls back to the filament library, then the catalog (Priority: P2)

**Goal**: Add the filament-library tier between the fleet-loaded tiers and the System catalog fallback.

**Independent Test**: With no fleet printer loaded with a matching-material spool but a matching-material spool sitting in storage, verify the stored spool's filament is chosen over the System catalog; verify a stored spool with no usable profile link is skipped rather than causing a failure.

### Implementation for User Story 3

- [ ] T016 [US3] Extend `RepresentativeFilamentResolver`'s tiering in `api/src/OpenFarm.Application/Profiles/RepresentativeFilamentResolver.cs`: add tier 3 (a `Spool` with `State = InStorage`, same material/link eligibility as tiers 1–2, tie-broken by `Spool.Id` ascending) between the fleet-loaded tiers (T011) and the System-catalog fallback, per research.md R2 step 3; a matching library spool with no resolvable `Filament.FilamentProfileId` (unlinked or dangling) is skipped in favor of the next eligible candidate rather than causing a failure (FR-010). Depends on T011.

### Tests for User Story 3 (test-after)

- [ ] T017 [US3] Extend `RepresentativeFilamentResolverTests` with library-tier cases per spec US3's acceptance scenarios: a stored (`InStorage`) matching spool wins over an unrelated System profile when nothing is fleet-loaded (Scenario 1); falls back to the System catalog when neither a loaded nor a stored spool exists (Scenario 2); a stored spool with an unlinked/dangling `FilamentProfileId` is skipped without error, proceeding to the next candidate or the catalog fallback (Scenario 3). Depends on T016.
- [ ] T018 [US3] Add an integration test in `api/tests/OpenFarm.Api.IntegrationTests/Parts/` covering the library-fallback path end-to-end: unload a fleet-loaded spool back to storage, then verify a subsequent STL add's estimate uses that stored spool's linked filament profile. Depends on T016.

**Checkpoint**: The full 4-tier match (target printer → fleet → library → catalog) is complete; all three user stories are independently deliverable and, together, resolve both problems from the original bug report.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T019 [P] Run `dotnet format api/OpenFarm.slnx --verify-no-changes` and fix any formatting drift across every file touched by T002–T018.
- [ ] T020 Update the class-level doc comments on `RepresentativeFilamentResolver` and `ResolveSlicingProfileSelection` (`api/src/OpenFarm.Application/Parts/ResolveSlicingProfileSelection.cs`'s own summary still describes "`RepresentativeFilamentResolver`'s deterministic per-material-type pick" as a single-tier System-catalog lookup) so they accurately describe the new tiered algorithm. Depends on T011, T012, T016.
- [ ] T021 Run every scenario in `quickstart.md` end-to-end against a local stack (`cd infra && docker compose up`) to confirm US1–US3 behave as documented, including the regression check that a manually narrowed filament requirement bypasses matching entirely. Depends on T008, T014, T015, T017, T018.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — one baseline sanity check.
- **Foundational (Phase 2)**: Empty — nothing blocks any user story (see Phase 2 note).
- **User Story 1 (Phase 3)**: Depends only on Setup. Fully independent of User Stories 2 and 3.
- **User Story 2 (Phase 4)**: Depends only on Setup. Independent of User Story 1 (different files, different code paths — see Phase 3 Checkpoint).
- **User Story 3 (Phase 5)**: Depends on User Story 2 (T011) — it extends the same resolver method's tiering. Does not depend on User Story 1.
- **Polish (Phase 6)**: Depends on whichever of US1–US3 are in scope for the change being shipped; T021 depends on all test tasks across all three stories.

### Within Each User Story

- Implementation tasks before their own tests (test-after convention, not TDD red-green).
- Story complete and checkpointed before its dependents (only US3→US2) begin.

### Parallel Opportunities

- T002 and T003 (different files, no code dependency on each other) can run in parallel.
- T005 and T006 (different handler files, both depend only on T003) can run in parallel.
- T009 (new enum) can start immediately in parallel with T002/T003/T005/T006 — no dependency between the US1 and US2 file sets at all.
- T013 (new test fakes) has no dependency on T009–T012's implementation — the fakes only implement already-existing repository interfaces — so it can be written in parallel with T009–T012.
- T007 [P] can run alongside T013 (different test projects).
- T019 is independent of everything except "some code exists to format."

---

## Parallel Example: User Story 1 + User Story 2 together

```bash
# US1 and US2 touch entirely disjoint files — a two-person split works cleanly:
# Developer A (US1):
Task: "T002 Replace GenericFallbackMaterialProfile with FallbackMaterialType in SlicerOptions.cs"
Task: "T003 Rename GetGenericFallbackMaterialProfile to GetFallbackMaterialType in ISlicerDefaultsProvider.cs"
Task: "T005 Update AddPartCommandHandler.cs call site"
Task: "T006 Update ReplacePartFileCommandHandler.cs call site"

# Developer B (US2), starting at the same time:
Task: "T009 Add FilamentMatchSource enum"
Task: "T013 Add FakeSpoolRepository/FakeFilamentRepository/FakeFeedUnitRepository"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001).
2. Complete Phase 3: User Story 1 (T002–T008).
3. **STOP and VALIDATE**: Run T008's regression test; confirm the reported "Generic PLA not found" bug no longer reproduces.
4. Ship — this alone resolves the blocking defect, independent of the intelligent-matching work.

### Incremental Delivery

1. Setup → User Story 1 → validate → ship (bug fix only).
2. Add User Story 2 → validate independently (resolver tiering, fleet-loaded preference) → ship (better estimates when the fleet has matching filament loaded).
3. Add User Story 3 → validate independently (library tier) → ship (complete 4-tier matching).
4. Polish (Phase 6) once all three are in.

### Parallel Team Strategy

With two developers: one takes User Story 1 (Phase 3) entirely, the other starts User Story 2 (Phase 4) immediately — no shared prerequisite blocks either. User Story 3 (Phase 5) can only start once User Story 2's resolver rewrite (T011) lands, so it's a natural third step for whoever finishes US1/US2 first.

---

## Notes

- [P] tasks = different files, no dependency on an incomplete task.
- [Story] label maps each task to its user story for traceability.
- No `contracts/` directory and no `Domain`/`Api`/`web/` changes in this feature — every task above stays within `OpenFarm.Application`/`OpenFarm.Infrastructure` and their test projects.
- Commit after each task or logical group.
- Stop at either Phase 3 or Phase 4's checkpoint to validate that story independently before continuing.
