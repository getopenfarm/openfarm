---

description: "Task list for build plate selection implementation"
---

# Tasks: Build Plate Selection — Per-Printer Physical Plate, Configured Alongside Machine/Process Profile

**Input**: Design documents from `/specs/012-build-plate-selection/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/printers-api.md, quickstart.md

**Tests**: Included. `plan.md`'s Constitution Check and `research.md` R8 explicitly scope TDD to the
plate-type↔temp-key mapping/"no value recorded" detection and the compatibility gate, and test-after
to everything else (EF config, read-side query, React) — this file follows that split per task.

**Organization**: Tasks are grouped by user story (spec.md: US1 and US2, both Priority P1) so each can
be implemented and validated independently, per `quickstart.md`'s two scenario walkthroughs.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)

## Path Conventions

Existing `api/` (four-project Clean Architecture) + `web/` (SPA) monorepo — no new projects/folders,
per `plan.md`'s Structure Decision. All paths below are exact existing files being extended, or new
files sitting alongside them in the same directory.

---

## Phase 1: Setup

**Purpose**: Confirm the baseline is clean before extending it — no new dependencies, no scaffolding
needed (this feature only extends existing files, per `plan.md`).

- [X] T001 Confirm baseline is green: `dotnet build api/OpenFarm.slnx --configuration Release`, `dotnet test api/OpenFarm.slnx`, `cd web && bun run lint && bunx tsc -b && bun run test`

**Checkpoint**: Baseline confirmed clean — any later failure is attributable to this feature's changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `BuildPlateType` enum, its vendor-name lookup, the `Printer` entity property, the
catalog's compatibility data, the new domain exception, and persistence — every later task in both
user stories depends on these existing.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Add `BuildPlateType` enum (six values: `Cool`, `Engineering`, `HighTemp`, `TexturedPei`, `TexturedCool`, `Supertack`) and the `BuildPlateTypeVendorNames` static code↔vendor-string lookup (per `data-model.md`'s mapping table) in new file `api/src/OpenFarm.Domain/Printers/BuildPlateType.cs`
- [X] T003 [P] Add nullable `BuildPlateType? BuildPlateType` property and `SelectBuildPlate(BuildPlateType)`/`ClearBuildPlate()` methods to `api/src/OpenFarm.Domain/Printers/Printer.cs` (depends on T002)
- [X] T004 [P] Add `IReadOnlySet<BuildPlateType>? CompatiblePlateTypes` field to the `PrinterModel` record in `api/src/OpenFarm.Domain/Printers/PrinterModel.cs` (depends on T002)
- [X] T005 Populate `CompatiblePlateTypes` for every entry in `PrinterModelCatalog.Entries` in `api/src/OpenFarm.Domain/Printers/PrinterModelCatalog.cs` — `A1`/`A1MINI` per `research.md` R2 (`{ TexturedPei, HighTemp }`); `X1`, `X1C`, `X1E`, `P1P`, `P1S` via the same two-signal method (`not_support_bed_type` authoritative when declared, else non-`"0"` filament-temp keys) against the real vendor bundle; `System`/`Simulation` gets all six values (depends on T004)
- [X] T006 [P] Add `IncompatibleBuildPlateException(BuildPlateType requested, string brand, string modelCode)` in new file `api/src/OpenFarm.Domain/Printers/IncompatibleBuildPlateException.cs` (depends on T002)
- [X] T007 [P] Map `IncompatibleBuildPlateException` to `422 Unprocessable Entity` ("Build plate incompatible with printer model") in the exception switch in `api/src/OpenFarm.Api/ExceptionHandling/DomainExceptionHandler.cs` (depends on T006)
- [X] T008 Add EF Core migration `AddPrinterBuildPlate` (nullable `text` column on `Printers`, no FK/index) in `api/src/OpenFarm.Infrastructure/Persistence/Migrations/` via `dotnet ef migrations add AddPrinterBuildPlate` (depends on T003)
- [X] T009 Add `builder.Property(p => p.BuildPlateType).HasConversion<string>();` to `api/src/OpenFarm.Infrastructure/Persistence/Configurations/PrinterConfiguration.cs` (depends on T003)

**Checkpoint**: Domain model, catalog data, persistence, and error mapping exist — both user stories
can now proceed.

---

## Phase 3: User Story 1 - Select a printer's build plate on its configure page (Priority: P1) 🎯 MVP

**Goal**: An operator can select/change/clear a printer's build plate on its own page, from choices
limited to that printer's registered brand/model, independent of whether a machine profile is linked —
persisted, activity-logged, and displayed with a backend-resolved friendly label (FR-001–FR-003,
FR-008–FR-009, FR-012).

**Independent Test**: `quickstart.md` Story 1 — open a printer with no machine profile linked, confirm
build-plate choices (with labels) appear immediately, select and save one, confirm it persists and is
activity-logged, confirm an incompatible value is rejected with 422, confirm two same-model printers
keep independent selections.

### Tests for User Story 1

> Write these first; TDD tasks (T010, T011) must fail before their implementation tasks land. T012–T014 are test-after, written alongside/after their implementation.

- [X] T010 [P] [US1] TDD: `PrinterModelCatalog` compatibility assertions (e.g. `A1` → `{TexturedPei, HighTemp}`, `System` → all six, an incompatible value is absent from a model's set) in `api/tests/OpenFarm.Domain.Tests/Printers/PrinterModelCatalogTests.cs`
- [X] T011 [P] [US1] TDD: `EditPrinterCommandHandler` build-plate cases — compatible selection persists, incompatible value throws `IncompatibleBuildPlateException`, `null` clears, no machine-profile prerequisite, granular activity-log events fire only on real change — in `api/tests/OpenFarm.Application.Tests/Printers/EditPrinterCommandHandlerTests.cs`
- [X] T012 [P] [US1] Test-after: `EditPrinterEndpointTests` build-plate PATCH/GET cases (persist, 422 on incompatible value, `buildPlateTypeLabel`/`compatibleBuildPlateTypes` shape in the GET response) in `api/tests/OpenFarm.Api.IntegrationTests/Printers/EditPrinterEndpointTests.cs`
- [X] T013 [P] [US1] Test-after: `PrinterFormPage.test.tsx` build-plate `<Select>` cases (renders backend-supplied labels, submits the enum value, no prerequisite gate) in `web/src/features/printers/PrinterFormPage.test.tsx`
- [X] T014 [P] [US1] Test-after: `PrinterDetailPage.test.tsx` build-plate read-only row case (renders `buildPlateTypeLabel`, "Not selected" when null) in `web/src/features/printers/PrinterDetailPage.test.tsx`

### Implementation for User Story 1

- [X] T015 [US1] Add `BuildPlateType? BuildPlateType` to `EditPrinterCommand` in `api/src/OpenFarm.Application/Printers/EditPrinterCommand.cs` (depends on T002)
- [X] T016 [US1] In `api/src/OpenFarm.Application/Printers/EditPrinterCommandHandler.cs`: resolve `PrinterModelCatalog.Resolve(printer.Brand, printer.ModelCode).CompatiblePlateTypes`, throw `IncompatibleBuildPlateException` when the requested value isn't in it, call `SelectBuildPlate`/`ClearBuildPlate`, and write `PrinterBuildPlateSelected`/`PrinterBuildPlateCleared` activity-log events only when the value actually changed (mirrors the existing `PrinterMachineProfileLinked/Unlinked` pattern at lines ~145-159) (depends on T005, T006, T015; makes T011 pass)
- [X] T017 [US1] In `api/src/OpenFarm.Application/Printers/GetPrinterDetailQuery.cs` / `GetPrinterDetailQueryHandler.cs`: add `BuildPlateType? BuildPlateType`, `string? BuildPlateTypeLabel`, and `IReadOnlyList<BuildPlateTypeOption> CompatibleBuildPlateTypes` (new `BuildPlateTypeOption(BuildPlateType Value, string Label)` record) to `PrinterDetail`, resolved from `PrinterModelCatalog.Resolve(...).CompatiblePlateTypes` and `BuildPlateTypeVendorNames` (depends on T002, T005)
- [X] T018 [US1] Add `buildPlateType` request field to `EditPrinterEndpoint`'s request DTO in `api/src/OpenFarm.Api/Endpoints/Printers/EditPrinterEndpoint.cs` (depends on T015)
- [X] T019 [US1] Add `buildPlateType`, `buildPlateTypeLabel`, `compatibleBuildPlateTypes` (as `{value, label}` pairs) to `PrinterDetailResponse` and its mapping in `api/src/OpenFarm.Api/Endpoints/Printers/GetPrinterDetailEndpoint.cs` (depends on T017; makes T012 pass)
- [X] T020 [US1] Regenerate the Orval-generated frontend API client (`web/src/lib/api/generated/endpoints.schemas.ts`, `web/src/lib/api/generated/printers/printers.ts`) from the updated OpenAPI spec (depends on T018, T019)
- [X] T021 [US1] Add a build-plate `<Select>` (shadcn `src/components/ui/`, not `ProfilePickerCombobox`) to `web/src/features/printers/PrinterFormPage.tsx`, buffered like `selectedMachineProfileId`/`selectedProcessProfileId`, rendering `compatibleBuildPlateTypes[].label` and submitting `.value`, with no `disabled` prerequisite gate (depends on T020; makes T013 pass)
- [X] T022 [US1] Add a read-only "Build plate" row to `web/src/features/printers/PrinterDetailPage.tsx` rendering `buildPlateTypeLabel ?? 'Not selected'` (depends on T020; makes T014 pass)

**Checkpoint**: User Story 1 is fully functional and independently testable — run `quickstart.md` Story 1.

---

## Phase 4: User Story 2 - Slice using the bed temperature for the printer's actual installed plate (Priority: P1)

**Goal**: A slice tied to a printer resolves its bed temperature from that printer's own selected build
plate, refusing with a clear reason when the plate or a matching temperature value is missing
(FR-005–FR-007).

**Independent Test**: `quickstart.md` Story 2 — two printers of the same model with different build
plates and the same filament produce different `curr_bed_type`/resolved bed temperatures; a printer
with no plate selected is refused; a filament preset missing a temp value for the selected plate is
refused; an estimate-only slice derives its plate from a fleet-representative printer.

### Tests for User Story 2

> TDD — write first, confirm failing before T025-T027 land.

- [X] T023 [P] [US2] TDD: `ResolveForPrinterAsync` throws `SlicingProfileUnconfiguredException` naming the printer when `BuildPlateType` is null; throws naming the filament preset + plate type when the resolved temp-key is missing/`"0"`; injects the correct `curr_bed_type` vendor string otherwise — in `api/tests/OpenFarm.Application.Tests/Parts/ResolveSlicingProfileSelectionTests.cs`
- [X] T024 [P] [US2] TDD: SC-003 case — two `BuildPlateType` selections for the same filament/machine/process produce different `curr_bed_type` values in `ProcessSettingsJson`; `ResolveForFleetRepresentativeAsync`'s representative-selection filter excludes printers with `BuildPlateType == null` — same file as T023

### Implementation for User Story 2

- [X] T025 [US2] In `api/src/OpenFarm.Application/Parts/ResolveSlicingProfileSelection.cs`'s `ResolveForPrinterAsync`: require `printer.BuildPlateType`, throwing `SlicingProfileUnconfiguredException($"Printer '{printer.Name}' has no build plate selected.")` when null (depends on T002; makes part of T023 pass)
- [X] T026 [US2] Same method: after the filament preset's merged `FilamentSettingsJson` is produced, look up the temp key for the printer's `BuildPlateType` (via the `BuildPlateTypeVendorNames`-adjacent key table from `research.md` R3), throw `SlicingProfileUnconfiguredException` naming the filament preset and plate type when missing/`"0"`, then inject `curr_bed_type` (the plate's vendor string) into the merged `ProcessSettingsJson` (depends on T025; makes rest of T023, T024 pass)
- [X] T027 [US2] Add `&& p.BuildPlateType is not null` to `ResolveForFleetRepresentativeAsync`'s existing `.Where(...)` predicate (depends on T002; makes rest of T024 pass)

**Checkpoint**: User Story 2 is fully functional and independently testable — run `quickstart.md` Story 2.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Formatting/lint gates and full end-to-end validation across both stories.

- [X] T028 [P] `dotnet format api/OpenFarm.slnx --verify-no-changes` (fix any formatting drift from Phases 2-4)
- [X] T029 [P] `cd web && bun run lint && bun run format && bunx tsc -b` (fix any drift from Phase 3)
- [X] T030 Run `quickstart.md` Story 1 and Story 2 scenarios end-to-end against the local stack (`infra/`) and confirm every step's expected result, including the regression checks (existing `EditPrinterEndpointTests` machine/process cases unaffected; existing `RealSlicerGoldenFileTests` suite unaffected)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — run first.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS both user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational only. No dependency on US2.
- **User Story 2 (Phase 4)**: Depends on Foundational only (specifically T002's enum/vendor-name lookup). Independently testable from US1, though in practice a plate must be selectable (US1) before a real end-to-end slice can be demonstrated (US2's quickstart assumes US1 is done, but US2's own tests/implementation do not import anything from US1's files).
- **Polish (Phase 5)**: Depends on both user stories being complete.

### Within Each User Story

- Tests before implementation (T010-T014 before T015-T022; T023-T024 before T025-T027).
- Domain/Application before Api before web (T015-T017 before T018-T019 before T020-T022).

### Parallel Opportunities

- T002, T003, T004, T006 can start in parallel once Setup completes (different files; T003/T004/T006 each only need T002's enum to exist as a type reference, so stagger those three slightly behind T002 if working solo).
- T007 (parallel with T008/T009 — different files).
- Within US1: T010-T014 (all different files) in parallel; within US2: T023-T024 (same file, sequential in practice even though both are TDD-first).
- T028 and T029 in parallel (backend vs. frontend tooling).

---

## Parallel Example: Phase 2 (Foundational)

```bash
# After T002 (BuildPlateType enum) lands:
Task: "Add BuildPlateType? property + Select/ClearBuildPlate to Printer.cs"
Task: "Add CompatiblePlateTypes field to PrinterModel.cs"
Task: "Add IncompatibleBuildPlateException.cs"
```

## Parallel Example: User Story 1 tests

```bash
Task: "PrinterModelCatalog compatibility assertions in PrinterModelCatalogTests.cs"
Task: "EditPrinterCommandHandler build-plate cases in EditPrinterCommandHandlerTests.cs"
Task: "EditPrinterEndpoint build-plate cases in EditPrinterEndpointTests.cs"
Task: "PrinterFormPage build-plate Select cases in PrinterFormPage.test.tsx"
Task: "PrinterDetailPage build-plate row case in PrinterDetailPage.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup.
2. Phase 2: Foundational (blocks everything).
3. Phase 3: User Story 1 — operators can select/persist/view a build plate, with backend-resolved
   labels, even though slicing doesn't use it yet.
4. **STOP and VALIDATE**: run `quickstart.md` Story 1.
5. Ship if desired — US1 alone is a real, demoable increment (the selection exists and is visible,
   just not yet consumed by slicing).

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add User Story 1 → validate independently → MVP.
3. Add User Story 2 → validate independently → the actual defect (wrong bed temperature) is now fixed.
4. Phase 5 polish → ship.

---

## Notes

- [P] tasks touch different files with no unmet dependency.
- [Story] labels map every Phase 3/4 task to spec.md's US1/US2 for traceability.
- TDD tasks (T010, T011, T023, T024) must be written and observed failing before their paired
  implementation tasks land, per constitution Principle II and `research.md` R8.
- Commit after each task or logical group; stop at either checkpoint to validate that story alone.
- `X1`/`X1E`/`P1P`/`P1S` compatible-plate-type sourcing (T005) requires the same real-vendor-bundle
  inspection method `research.md` R2 already applied to `A1`/`A1MINI`/`X1C` — not guesswork.
