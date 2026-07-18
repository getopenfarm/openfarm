---

description: "Task list for 006-filament-tracking"

---

# Tasks: Filament Tracking

**Input**: Design documents from `/specs/006-filament-tracking/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/filament-tracking-api.md,
quickstart.md (all present)

**Tests**: Included per plan.md's explicit testing strategy — TDD (write-first) for
`SpoolStateTransitions`, the usage-decrement/floor/overrun math, the `RegisterSpoolCommandValidator`
weight-relationship gates, and `FeedUnitKindCatalog.Resolve` (deterministic, well-specified logic, the
constitution's own named non-negotiable examples plus the exact classification
`PrinterModelCatalog.Resolve` already carries). Test-after/fixture-replay for
`BambuMqttMessageParser.ParseExternalSpool` (mirrors its sibling `ParseTray`, using the real
already-captured fixtures that already contain `vt_tray` payloads — no new fixture files needed).
Test-after for orchestration-only `Application` handlers, EF Core repository/configuration code, and
React components/pages, per the constitution's existing carve-outs. Task order below reflects each
item's actual classification — TDD tasks are sequenced test-then-implementation; test-after tasks are
sequenced implementation-then-test.

**Organization**: Tasks are grouped by user story (spec.md's US1–US4) to enable independent
implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4); omitted for Setup/Foundational/Polish
- Every task names exact file paths

## Path Conventions

Existing four-project Clean Architecture backend + single-SPA frontend, extended in place (plan.md
"Structure Decision" — no new top-level project; `web/src/features/filament/` and
`web/src/features/spools/` replace their mock-data prototypes in place):

- `api/src/OpenFarm.Domain/FilamentTracking/`, `api/src/OpenFarm.Application/FilamentTracking/`,
  `api/src/OpenFarm.Infrastructure/FilamentTracking/`, `api/src/OpenFarm.Api/Endpoints/{Vendors,Filaments,FeedUnits,Spools}/`
- `api/src/OpenFarm.Domain/Printers/`, `api/src/OpenFarm.Infrastructure/Printers/` (existing files, extended)
- `api/tests/OpenFarm.Domain.Tests/FilamentTracking/`, `api/tests/OpenFarm.Application.Tests/FilamentTracking/`,
  `api/tests/OpenFarm.Infrastructure.Tests/Printers/Bambu/` (existing test file, extended),
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/`
- `web/src/features/filament/`, `web/src/features/spools/`, `web/src/features/printers/components/` (extended)

---

## Phase 1: Setup

**Purpose**: Project scaffolding — no new NuGet or Bun dependencies are needed (state machine, catalog,
and continuation orchestration are plain C#/EF Core following `Printer`'s/`PrinterModelCatalog`'s
existing shape; the telemetry sync reuses `System.Text.Json` parsing already in `BambuMqttMessageParser`;
no new frontend dependencies).

- [X] T001 Create empty `FilamentTracking/` folders under `api/src/OpenFarm.Domain/`,
  `api/src/OpenFarm.Application/`, `api/src/OpenFarm.Infrastructure/`, plus
  `api/src/OpenFarm.Api/Endpoints/Vendors/`, `Endpoints/Filaments/`, `Endpoints/FeedUnits/`,
  `Endpoints/Spools/`, per plan.md's Project Structure
- [X] T002 [P] Confirm the existing real Bambu MQTT fixtures in
  `api/tests/OpenFarm.Infrastructure.Tests/Printers/Fixtures/*.json` already contain a populated
  `print.vt_tray` block (confirmed present in `idle-a1.json`, `printing-a1.json`,
  `awaiting-clear-p1p-no-ams.json`, `awaiting-clear-x1c-multi-ams.json`) — no new fixture file is needed
  for `ParseExternalSpool`'s fixture-replay tests (research.md §6)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entities, persistence, the `FeedUnit`/`FeedSlot` connector-derived telemetry sync, and the
kind-driven reload-policy catalog every user story (US1–US4) depends on. The telemetry sync is
foundational rather than story-scoped because US2's own independent test requires a printer's Feed Slots
to already exist before a spool can be loaded (spec.md's Assumptions).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain entities and exceptions

- [X] T003 [P] Create `Vendor` entity in `api/src/OpenFarm.Domain/FilamentTracking/Vendor.cs`
  (`Id`, `OrganizationId`, `Name`, `CreatedAtUtc` per data-model.md)
- [X] T004 [P] Create `Filament` entity in `api/src/OpenFarm.Domain/FilamentTracking/Filament.cs`
  (`Id`, `OrganizationId`, `VendorId` FK, `Material`, `MaterialType`, `FilamentProfileId` bare `Guid?`,
  `CreatedAtUtc`, owns a `FilamentVariants` collection)
- [X] T005 [P] Create `FilamentVariant` entity in
  `api/src/OpenFarm.Domain/FilamentTracking/FilamentVariant.cs` (`Id`, `FilamentId` FK, `ColorName`,
  `ColorHex`, `CreatedAtUtc`)
- [X] T006 [P] Create `ReloadPolicy` enum in `api/src/OpenFarm.Domain/FilamentTracking/ReloadPolicy.cs`
  (`None`, `MatchRequired`, `Unconditional` per research.md §4)
- [X] T007 [P] Create `FeedUnit` entity in `api/src/OpenFarm.Domain/FilamentTracking/FeedUnit.cs`
  (`Id`, `OrganizationId`, `PrinterId` bare FK, `Kind` (`string`), `UnitIndex`, `Name?`,
  `ReplacesFeedUnitId?` self-FK, `IsRevealed` (`bool`, default `false`), `CreatedAtUtc`, owns a
  `FeedSlots` collection, per data-model.md — no `Kind`-specific fields split out)
- [X] T008 [P] Create `FeedSlot` entity in `api/src/OpenFarm.Domain/FilamentTracking/FeedSlot.cs`
  (`Id`, `OrganizationId`, `FeedUnitId` FK, `SlotPosition`, `DeactivatedAtUtc?`, `CreatedAtUtc` — no
  `Kind`, no AFR flag, no standby field per research.md §3)
- [X] T009 [P] Create `FeedUnitKindCatalog` in
  `api/src/OpenFarm.Domain/FilamentTracking/FeedUnitKindCatalog.cs` — static `Resolve(string kind) ->
  ReloadPolicy` per the table in research.md §4 (`MultiMaterialUnit` → `MatchRequired`,
  `AutomaticFilamentReloader` → `Unconditional`, `ExternalSpool` → `None`, unrecognized → `None`,
  never throws), mirroring `PrinterModelCatalog`'s existing lookup shape
- [X] T010 [P] Write `FeedUnitKindCatalog.Resolve` tests (TDD, write first) in
  `api/tests/OpenFarm.Domain.Tests/FilamentTracking/FeedUnitKindCatalogTests.cs` — each known `Kind`
  resolves to its documented `ReloadPolicy`, and an unrecognized string resolves to `None` without
  throwing; confirm the tests fail before T009 is implemented, then confirm they pass
- [X] T011 [P] Create `SpoolState` enum in `api/src/OpenFarm.Domain/FilamentTracking/SpoolState.cs`
  (`InStorage`, `Using`, `Finished`)
- [X] T012 Write `SpoolStateTransitions` tests (TDD, write first) in
  `api/tests/OpenFarm.Domain.Tests/FilamentTracking/SpoolStateTransitionsTests.cs` — every valid
  transition (`InStorage`→`Using`, `Using`→`InStorage`, `Using`→`Finished`) succeeds, every invalid one
  (including any transition out of `Finished`) throws `InvalidSpoolStateTransitionException`; confirm
  failing before implementation
- [X] T013 Implement `SpoolStateTransitions` in
  `api/src/OpenFarm.Domain/FilamentTracking/SpoolStateTransitions.cs` to make T012 pass, mirroring
  `PrinterStatusTransitions`'s existing shape
- [X] T014 [P] Create `InvalidSpoolStateTransitionException`, `SpoolConcurrencyConflictException`,
  `FeedSlotOccupiedException`, `FeedSlotDeactivatedException`, `FeedSlotNotFoundException`,
  `FeedUnitNotFoundException`, `NotAnAutomaticFilamentReloaderException` in
  `api/src/OpenFarm.Domain/FilamentTracking/` (one file per exception)
- [X] T015 [P] Create `VendorNotFoundException`, `FilamentNotFoundException`,
  `FilamentVariantNotFoundException`, `DuplicateVendorNameException`,
  `DuplicateAutomaticFilamentReloaderNameException` in `api/src/OpenFarm.Domain/FilamentTracking/`
- [X] T016 Write the usage-decrement/floor/overrun math tests (TDD, write first) in
  `api/tests/OpenFarm.Domain.Tests/FilamentTracking/SpoolTests.cs` — `RecordUsage` decrements
  `RemainingWeightGrams`, cumulative usage exceeding remaining weight floors at zero and sets
  `HasUsageOverrun = true` rather than going negative (spec.md's Edge Case), `LoadOntoFeedSlot`/
  `ReturnToStorage`/`MarkFinished` drive `SpoolStateTransitions`, and `CorrectRemainingWeightCommand`'s
  manual override clears `HasUsageOverrun`; confirm failing before implementation
- [X] T017 Create `Spool` entity in `api/src/OpenFarm.Domain/FilamentTracking/Spool.cs` — `Id`,
  `OrganizationId`, `FilamentVariantId` FK, `Label?`, `SpoolWeightGrams`, `RemainingWeightGrams`,
  `HasUsageOverrun`, `State`, `FeedSlotId?`, `CreatedAtUtc`, plus
  `LoadOntoFeedSlot`/`ReturnToStorage`/`MarkFinished`/`RecordUsage`/`CorrectRemainingWeight` behavior
  methods, to make T016 pass
- [X] T018 [P] Create `SpoolUsageRecord` entity in
  `api/src/OpenFarm.Domain/FilamentTracking/SpoolUsageRecord.cs` (`Id`, `OrganizationId`, `SpoolId` FK,
  `GramsConsumed`, `RecordedAtUtc`, `JobId?` reserved-unused, `Source` enum `Job`/`Manual`)

### `Printer` extensions (existing files)

- [X] T019 [P] Add `FilamentInletCount` (`int`, default `1`) to `PrinterModel` in
  `api/src/OpenFarm.Domain/Printers/PrinterModel.cs`, resolved via `PrinterModelCatalog` for every
  existing X1/P1/A1-series entry in
  `api/src/OpenFarm.Domain/Printers/PrinterModelCatalog.cs` (research.md §11, mirrors
  `CameraTransport`'s existing per-model lookup shape)
- [X] T020 Add `ExternalSpoolTray` (`AmsTray?`, reused type) to `Printer` in
  `api/src/OpenFarm.Domain/Printers/Printer.cs` — folds under `RecordTelemetry`'s existing delta rule,
  same as `AmsUnits` (data-model.md, research.md §6)

### Persistence

- [X] T021 [P] Create `VendorConfiguration` in
  `api/src/OpenFarm.Infrastructure/Persistence/Configurations/VendorConfiguration.cs`
  (`builder.IsMultiTenant()`, unique on `(OrganizationId, Name)`)
- [X] T022 [P] Create `FilamentConfiguration` and `FilamentVariantConfiguration` in
  `api/src/OpenFarm.Infrastructure/Persistence/Configurations/FilamentConfiguration.cs` /
  `FilamentVariantConfiguration.cs` (`FilamentConfiguration`: `IsMultiTenant()`, `VendorId` `ON DELETE
  RESTRICT`; `FilamentVariantConfiguration`: cascade delete from `Filament`, unique on
  `(FilamentId, ColorName)`)
- [X] T023 [P] Create `FeedUnitConfiguration` and `FeedSlotConfiguration` in
  `api/src/OpenFarm.Infrastructure/Persistence/Configurations/FeedUnitConfiguration.cs` /
  `FeedSlotConfiguration.cs` — `FeedUnitConfiguration`: `IsMultiTenant()`, `Kind` as a plain string
  column (no `HasConversion`), unique on `(PrinterId, Kind, UnitIndex)`, `ReplacesFeedUnitId` nullable
  self-FK `ON DELETE SET NULL`; `FeedSlotConfiguration`: `IsMultiTenant()`, unique on
  `(FeedUnitId, SlotPosition)`, `FeedUnitId` `ON DELETE RESTRICT`
- [X] T024 [P] Create `SpoolConfiguration` and `SpoolUsageRecordConfiguration` in
  `api/src/OpenFarm.Infrastructure/Persistence/Configurations/SpoolConfiguration.cs` /
  `SpoolUsageRecordConfiguration.cs` — `SpoolConfiguration`: `IsMultiTenant()`,
  `.HasConversion<string>()` on `State`, `builder.UseXminAsConcurrencyToken()`, partial unique index
  `(FeedSlotId) WHERE "State" = 'Using'`, `FeedSlotId` `ON DELETE RESTRICT`;
  `SpoolUsageRecordConfiguration`: `IsMultiTenant()`, cascade delete from `Spool`
  (data-model.md's EF Core configuration notes)
- [X] T025 Add the `AddFilamentTracking` EF Core migration in
  `api/src/OpenFarm.Infrastructure/Persistence/Migrations/` via `dotnet ef migrations add
  AddFilamentTracking` against `OpenFarmDbContext` — the seven new tables (depends on T003–T024)
- [X] T026 Add the `AddExternalSpoolTelemetry` EF Core migration in
  `api/src/OpenFarm.Infrastructure/Persistence/Migrations/` via `dotnet ef migrations add
  AddExternalSpoolTelemetry` — the one new `Printer.ExternalSpoolTray` column, kept as a separate
  migration since it's a schema-coherent change to a different existing table (depends on T020; two
  migrations total in this feature's PR, per the constitution's one-migration-per-schema-change rule
  applied per coherent unit)

### Repositories and the telemetry sync

- [X] T027 [P] Create `IVendorRepository`, `IFilamentRepository`, `IFeedUnitRepository`,
  `ISpoolRepository` interfaces in `api/src/OpenFarm.Application/FilamentTracking/` (each mirrors
  `IPrinterRepository`'s shape: `AddAsync`, `GetByIdAsync`, `ListAsync`, `SaveChangesAsync`, plus
  `IFeedUnitRepository.GetByPrinterIdAsync`/`FindByPrinterKindUnitIndexAsync` and
  `ISpoolRepository.FindLoadedInFeedUnitAsync` for the auto-continuation sibling lookup)
- [X] T028 [P] Implement `VendorRepository`, `FilamentRepository`, `FeedUnitRepository`,
  `SpoolRepository` in `api/src/OpenFarm.Infrastructure/FilamentTracking/` against `OpenFarmDbContext`
- [X] T029 [P] Create `IFeedSlotTelemetrySync` contract in
  `api/src/OpenFarm.Application/FilamentTracking/IFeedSlotTelemetrySync.cs` —
  `SyncAsync(Guid printerId, Guid organizationId, string brand, IReadOnlyList<AmsUnit> amsUnits, AmsTray?
  externalSpoolTray, CancellationToken ct)`
- [X] T030 Implement `FeedSlotTelemetrySyncHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/FeedSlotTelemetrySyncHandler.cs` — diffs currently-stored
  `MultiMaterialUnit`/`ExternalSpool` `FeedUnit`/`FeedSlot` rows against the reported topology; upserts
  new units/slots, soft-deactivates (`DeactivatedAtUtc`) ones no longer reported, clears
  `DeactivatedAtUtc` on ones that reappear; only writes when the diff is non-empty (Constitution
  Principle V); never touches `Kind = AutomaticFilamentReloader` rows (research.md §6)
- [X] T031 [P] Write `BambuMqttMessageParser.ParseExternalSpool` tests (fixture-replay, test-after) in
  `api/tests/OpenFarm.Infrastructure.Tests/Printers/Bambu/BambuMqttMessageParserTests.cs` (existing
  file, extended) — asserts `print.vt_tray` parses into an `AmsTray` using the existing real fixtures
  from T002, mirroring `ParseTray`'s existing test coverage
- [X] T032 Implement `ParseExternalSpool(JsonElement print)` in
  `api/src/OpenFarm.Infrastructure/Printers/Bambu/BambuMqttMessageParser.cs` (existing file, extended) —
  reads `print.vt_tray` into an `AmsTray`, mirrors `ParseTray`, to make T031 pass; wire its result into
  `TryParseTelemetry`'s returned snapshot
- [X] T033 Extend `PrinterConnectionBackgroundService.HandleEventAsync` in
  `api/src/OpenFarm.Infrastructure/Printers/PrinterConnectionBackgroundService.cs` (existing file) —
  its existing coalesced-flush telemetry block gains one call to `IFeedSlotTelemetrySync.SyncAsync` after
  `Printer.RecordTelemetry`, passing the printer's current `AmsUnits`/`ExternalSpoolTray` (research.md §6)
- [X] T034 [P] Add a static `ExternalSpoolTray` fixture to `SimulatedPrinterState` in
  `api/src/OpenFarm.Infrastructure/Printers/Simulator/SimulatedPrinterState.cs` (existing file) so the
  virtual/simulator printer reports an External Spool for quickstart.md Scenario 2's default-hidden
  behavior to be observable locally
- [X] T035 [P] Write `FeedSlotTelemetrySyncHandler` integration tests (test-after) in
  `api/tests/OpenFarm.Infrastructure.Tests/Printers/PrinterConnectionBackgroundServiceTests.cs` (existing
  file, extended) — a printer's first connection populates its `MultiMaterialUnit`/`ExternalSpool`
  `FeedUnit`/`FeedSlot` rows; a topology shrink soft-deactivates the missing slots without touching a
  `Spool` currently `Using` one of them (spec.md's Edge Case)

### Real-time publishing and DI

- [X] T036 [P] Create `IFilamentTrackingEventPublisher` contract in
  `api/src/OpenFarm.Application/FilamentTracking/IFilamentTrackingEventPublisher.cs` —
  `SpoolStateChangedAsync`, `FeedUnitAutoContinuedAsync`, `FeedUnitsChangedAsync` per
  contracts/filament-tracking-api.md's SignalR section
- [X] T037 Implement `FarmHubFilamentTrackingEventPublisher` in
  `api/src/OpenFarm.Api/Hubs/FarmHubFilamentTrackingEventPublisher.cs` — reuses the existing `/hubs/farm`
  hub, tenant-scoped groups, no new hub (Constitution Principle III/V)
- [X] T038 Extend `DomainExceptionHandler` in
  `api/src/OpenFarm.Api/ExceptionHandling/DomainExceptionHandler.cs` (existing file) with this feature's
  exception → `ProblemDetails` status mappings (404 for the `*NotFoundException`s, 409 for
  `SpoolConcurrencyConflictException`/`FeedSlotOccupiedException`, 422 for
  `InvalidSpoolStateTransitionException`/`FeedSlotDeactivatedException`/`NotAnAutomaticFilamentReloaderException`,
  409 for `DuplicateVendorNameException`/`DuplicateAutomaticFilamentReloaderNameException`)
- [X] T039 Register `IVendorRepository`, `IFilamentRepository`, `IFeedUnitRepository`,
  `ISpoolRepository`, `IFeedSlotTelemetrySync`, `IFilamentTrackingEventPublisher` in the composition
  root's DI container (`api/src/OpenFarm.Api/Program.cs` or its existing service-registration extension
  method)

**Checkpoint**: Foundation ready — user story implementation can now begin. A connected printer's Feed
Units/Feed Slots populate automatically; `FeedUnitKindCatalog` and `SpoolStateTransitions` are covered by
passing TDD suites.

---

## Phase 3: User Story 1 - Define reusable Filament and Vendor catalog entries (Priority: P1) 🎯 MVP

**Goal**: Operators can create Vendors and Filaments (with color variants) as a reusable catalog,
entirely independent of any spool or printer connection.

**Independent Test**: Create a Vendor, then create a Filament referencing it with at least one color
variant, and confirm both appear in their catalogs with the variant selectable — without creating a
single spool.

- [X] T040 [P] [US1] Create `CreateVendorCommand` + Validator in
  `api/src/OpenFarm.Application/FilamentTracking/CreateVendorCommand.cs` /
  `CreateVendorCommandValidator.cs` (`Name` required non-empty)
- [X] T041 [US1] Implement `CreateVendorCommandHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/CreateVendorCommandHandler.cs` — throws
  `DuplicateVendorNameException` on a `(OrganizationId, Name)` collision (FR-017)
- [X] T042 [P] [US1] Create `GetVendorsQuery` + Handler in
  `api/src/OpenFarm.Application/FilamentTracking/GetVendorsQuery.cs` / `GetVendorsQueryHandler.cs`
- [X] T043 [P] [US1] Create `CreateFilamentCommand` + Validator in
  `api/src/OpenFarm.Application/FilamentTracking/CreateFilamentCommand.cs` /
  `CreateFilamentCommandValidator.cs` (`VendorId`, `Material`, `MaterialType` required; at least one
  `variants` entry with non-empty `ColorName`/`ColorHex`; FR-016)
- [X] T044 [US1] Implement `CreateFilamentCommandHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/CreateFilamentCommandHandler.cs` — throws
  `VendorNotFoundException` on an unresolvable `VendorId`
- [X] T045 [P] [US1] Create `AddFilamentVariantCommand` + Validator in
  `api/src/OpenFarm.Application/FilamentTracking/AddFilamentVariantCommand.cs` /
  `AddFilamentVariantCommandValidator.cs`
- [X] T046 [US1] Implement `AddFilamentVariantCommandHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/AddFilamentVariantCommandHandler.cs` — throws
  `FilamentNotFoundException` on an unresolvable `FilamentId`; relies on the unique
  `(FilamentId, ColorName)` index for the 409 duplicate case
- [X] T047 [P] [US1] Create `GetFilamentsQuery` + Handler in
  `api/src/OpenFarm.Application/FilamentTracking/GetFilamentsQuery.cs` /
  `GetFilamentsQueryHandler.cs` — joined read including `vendorName` and `variants[]`
  (contracts/filament-tracking-api.md)
- [X] T048 [P] [US1] Create `CreateVendorEndpoint` + `ListVendorsEndpoint` in
  `api/src/OpenFarm.Api/Endpoints/Vendors/` — `POST /api/v1/vendors`
  (`[Permission("vendors:create")]`) and `GET /api/v1/vendors` (`[Permission("vendors:list")]`)
- [X] T049 [P] [US1] Create `CreateFilamentEndpoint`, `AddFilamentVariantEndpoint`,
  `ListFilamentsEndpoint` in `api/src/OpenFarm.Api/Endpoints/Filaments/` — `POST /api/v1/filaments`
  (`[Permission("filaments:create")]`), `POST /api/v1/filaments/{id}/variants`
  (`[Permission("filaments:add-variant")]`), `GET /api/v1/filaments`
  (`[Permission("filaments:list")]`)
- [X] T050 [P] [US1] Integration test: creating a Vendor then a Filament referencing it with a variant
  makes both appear in their respective list endpoints, and a duplicate Vendor name is rejected 409, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/VendorAndFilamentCatalogTests.cs` (FR-016,
  FR-017, US1 Acceptance Scenarios 1–2)
- [X] T051 [P] [US1] Integration test: adding a variant to an unknown `FilamentId` 404s; adding a
  duplicate `(FilamentId, ColorName)` 409s, in the same test file
- [X] T052 [P] [US1] Regenerate the Orval-generated API client against the updated OpenAPI spec to
  produce Vendor/Filament hooks in `web/src/lib/api/generated/`
- [X] T053 [US1] Create `CreateVendorDialog.tsx` and `VendorTable.tsx` in
  `web/src/features/filament/components/` — a lightweight Vendor tab embedded in the Filament feature
  page, not its own nav item (plan.md's Structure Decision)
- [X] T054 [US1] Rewire `FilamentCatalogTable.tsx`, `VariantsCard.tsx`, `VariantDialog.tsx` in
  `web/src/features/filament/components/` to the real Orval-generated hooks, replacing their mock-data
  wiring
- [X] T055 [US1] Rewire `FilamentPage.tsx` and `FilamentFormPage.tsx` in `web/src/features/filament/` —
  add the embedded Vendor tab (`VendorTable.tsx`/`CreateVendorDialog.tsx`), wire the catalog table and
  create/variant forms to real data
- [X] T056 [P] [US1] Delete `web/src/features/filament/data/mockFilament.ts` and
  `mockFilamentStore.ts`, updating `FilamentPage.test.tsx` / `FilamentFormPage.test.tsx` to cover the
  real API-backed catalog instead of the mock prototype
- [X] T057 [P] [US1] Vitest + RTL tests for `CreateVendorDialog.tsx` and `VendorTable.tsx` in
  `web/src/features/filament/components/` (create-Vendor flow; new Vendor becomes selectable on the
  Filament create form)

**Checkpoint**: US1 is fully functional and independently testable — create Vendor → create Filament
with a variant → both appear in their catalogs, no spool or printer involved.

---

## Phase 4: User Story 2 - Track a spool through its lifecycle (Priority: P1)

**Goal**: Operators can register a spool from the catalog, load it onto a connected printer's Feed Slot,
and move it back to storage or mark it finished.

**Independent Test**: Given at least one Filament/variant exists (US1), add a spool, load it onto a
connected printer's Feed Slot, then remove it and mark it finished — verifying
InStorage → Using → Finished and that a finished spool is no longer offered as loadable inventory.

- [X] T058 [P] [US2] Create `RegisterSpoolCommand` + Validator in
  `api/src/OpenFarm.Application/FilamentTracking/RegisterSpoolCommand.cs` /
  `RegisterSpoolCommandValidator.cs` — `FilamentVariantId` required; `SpoolWeightGrams > 0`;
  `RemainingWeightGrams` defaults to `SpoolWeightGrams`, must be `> 0` and `≤ SpoolWeightGrams`
  (FR-001, FR-002, data-model.md's Validation rules)
- [X] T059 [P] [US2] Write `RegisterSpoolCommandValidator` weight-relationship tests (TDD, write first)
  in `api/tests/OpenFarm.Application.Tests/FilamentTracking/RegisterSpoolCommandValidatorTests.cs` — zero/
  negative/non-numeric `SpoolWeightGrams`/`RemainingWeightGrams` rejected; `RemainingWeightGrams` greater
  than `SpoolWeightGrams` rejected; a valid partially-used spool (`RemainingWeightGrams <
  SpoolWeightGrams`) accepted (spec.md's Edge Cases); confirm failing before T058's validator exists,
  then confirm passing
- [X] T060 [US2] Implement `RegisterSpoolCommandHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/RegisterSpoolCommandHandler.cs` — throws
  `FilamentVariantNotFoundException` on an unresolvable `FilamentVariantId`; creates the `Spool` in
  `InStorage`
- [X] T061 [P] [US2] Create `GetSpoolsQuery` + Handler in
  `api/src/OpenFarm.Application/FilamentTracking/GetSpoolsQuery.cs` / `GetSpoolsQueryHandler.cs` —
  filters by `state`, `filamentId`, `filamentVariantId`, `vendorId` (FR-015); joined read including
  `colorName`, `colorHex`, `material`, `vendorName`, `feedSlotLabel` (contracts/filament-tracking-api.md)
- [X] T062 [P] [US2] Create `GetFeedUnitsQuery` + Handler in
  `api/src/OpenFarm.Application/FilamentTracking/GetFeedUnitsQuery.cs` /
  `GetFeedUnitsQueryHandler.cs` — per-printer, nested `feedSlots[]`; derives `displayLabel` via
  `FeedUnitKindCatalog`'s brand-terminology resolution for `MultiMaterialUnit`, fixed `"External
  Spool"` for `ExternalSpool`, operator `Name` for `AutomaticFilamentReloader` (FR-019, research.md §5);
  derives `currentSpool` and (for any kind with live telemetry) `detectedMaterialType`/
  `detectedColorHex`/`detectedSubBrand` at read time (FR-021, research.md §8); computes
  `supersededByFeedUnitId` for an `ExternalSpool` unit purely from the FR-023/§11 auto-derived source
  (an active non-deactivated `MultiMaterialUnit` on the same printer, gated on
  `PrinterModel.FilamentInletCount == 1`, FR-023a) since no `AutomaticFilamentReloader` exists yet in
  this story; applies the hide-by-default filter (omit an `ExternalSpool` unit when superseded, not
  revealed, and nothing loaded on it — FR-022/FR-025 safety net still applies even though `IsRevealed`
  can't yet be toggled until US4 adds `AddFeedUnitCommand`)
- [X] T063 [P] [US2] Create `ListFeedUnitsEndpoint` in `api/src/OpenFarm.Api/Endpoints/FeedUnits/` —
  `GET /api/v1/printers/{printerId}/feed-units`, `[Permission("feed-units:list")]`, 404 on an unresolved
  `printerId`
- [X] T064 [US2] Create `LoadSpoolOntoFeedSlotCommand` + Handler in
  `api/src/OpenFarm.Application/FilamentTracking/LoadSpoolOntoFeedSlotCommand.cs` /
  `LoadSpoolOntoFeedSlotCommandHandler.cs` — pre-checks the spool is `InStorage`, the target `FeedSlot`
  isn't `DeactivatedAtUtc`-set, and no other spool is currently `Using` it (ahead of the partial unique
  index, FR-005/FR-005b); reads the target slot's live telemetry (`AmsUnits[...]`/`ExternalSpoolTray`,
  regardless of `Kind`) and sets a non-blocking `rfidMismatchWarning` on a detected-material mismatch,
  skipping the comparison when nothing is detected (FR-021, research.md §8); sets a `portSharingNote`
  when loading onto a superseded `ExternalSpool` slot (FR-022, informational only); catches the unique
  index violation and rethrows as `SpoolConcurrencyConflictException` (409)
- [X] T065 [US2] Create `UnloadSpoolCommand` + Handler in
  `api/src/OpenFarm.Application/FilamentTracking/UnloadSpoolCommand.cs` /
  `UnloadSpoolCommandHandler.cs` — `ReturnToStorage` (keeps `RemainingWeightGrams`) or `MarkFinished`
  action (FR-007); requires the spool `Using`; 409 on a concurrent conflicting change
  (`SpoolConcurrencyConflictException`)
- [X] T066 [P] [US2] Create `RegisterSpoolEndpoint`, `ListSpoolsEndpoint`, `LoadSpoolEndpoint`,
  `UnloadSpoolEndpoint` in `api/src/OpenFarm.Api/Endpoints/Spools/` — `POST /api/v1/spools`
  (`[Permission("spools:register")]`), `GET /api/v1/spools` (`[Permission("spools:list")]`), `PUT
  /api/v1/spools/{id}/load` (`[Permission("spools:load")]`), `PUT /api/v1/spools/{id}/unload`
  (`[Permission("spools:unload")]`)
- [X] T067 [P] [US2] Integration test: registering a spool creates it `InStorage` with
  `RemainingWeightGrams` defaulted to `SpoolWeightGrams`; registering with a lower `RemainingWeightGrams`
  creates it partially-used, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/RegisterSpoolEndpointTests.cs` (FR-001,
  FR-002, US2 Acceptance Scenarios 1–2)
- [X] T068 [P] [US2] Integration test: loading a spool onto a never-connected printer's Feed Slot is
  rejected (no Feed Slot exists), loading onto a connected printer's populated Feed Slot succeeds and
  moves the spool to `Using`, and loading onto an already-occupied slot 409s, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/LoadSpoolEndpointTests.cs` (FR-005, FR-005b,
  US2 Acceptance Scenario 3, spec.md's Edge Cases)
- [X] T069 [P] [US2] Integration test: unloading with `MarkFinished` moves the spool to `Finished` and
  it no longer appears in `GET /spools?state=InStorage` or as a loadable option; unloading with
  `ReturnToStorage` reverts to `InStorage` and retains `RemainingWeightGrams`, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/UnloadSpoolEndpointTests.cs` (FR-007, US2
  Acceptance Scenarios 4–5)
- [X] T070 [P] [US2] Integration test: two near-simultaneous unload calls against the same spool — one
  `200`, one `409` — in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/SpoolConcurrencyConflictTests.cs` (spec.md's
  concurrency edge case)
- [X] T071 [P] [US2] Integration test: `GET .../feed-units` omits a superseded, unrevealed
  `ExternalSpool` unit by default when an active `MultiMaterialUnit` exists on a single-inlet printer
  model, but includes it once a spool is loaded onto it (FR-023, FR-025 safety net), in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/FeedUnitsListingTests.cs`
- [X] T072 [P] [US2] Integration test: loading a spool onto a Feed Slot whose live telemetry reports
  detected material returns `rfidMismatchWarning: true` on a mismatch and completes the load regardless;
  a slot with no detected data skips the comparison with no warning either way, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/DetectedMaterialWarningTests.cs` (FR-021)
- [X] T073 [P] [US2] Integration test: every spool state transition writes an `ActivityLogEntry`
  (register, load, unload), in
  `api/tests/OpenFarm.Api.IntegrationTests/ActivityLog/SpoolStateActivityLogTests.cs` (FR-014, SC-005)
- [X] T074 [P] [US2] Regenerate the Orval-generated API client for the Feed Units and Spools endpoints in
  `web/src/lib/api/generated/`
- [X] T075 [US2] Create `AddSpoolDialog.tsx` in `web/src/features/spools/components/` — Filament/variant
  `Select` sourced from `GET /filaments`, Spool Weight (predefined-size list or custom) and Remaining
  Weight fields (react-hook-form + zod)
- [X] T076 [US2] Create `LoadSpoolDialog.tsx` in `web/src/features/spools/components/` — Feed Slot
  `Select` sourced from `GET /printers/{id}/feed-units` (flattened from the nested
  `feedUnits[].feedSlots[]` shape), shows `rfidMismatchWarning`/`portSharingNote` after a successful load
- [X] T077 [P] [US2] Create `UnloadSpoolDialog.tsx` in `web/src/features/spools/components/` —
  Return-to-storage vs. mark-finished choice (FR-007)
- [X] T078 [US2] Rewire `SpoolsTable.tsx` and `SpoolsPage.tsx` in `web/src/features/spools/` to the real
  Orval-generated hooks, wiring in `AddSpoolDialog`/`LoadSpoolDialog`/`UnloadSpoolDialog`, replacing the
  mock-data prototype
- [X] T079 [US2] Create `FeedUnitsCard.tsx` in `web/src/features/printers/components/` — printer-detail
  page card showing the grouped Feed Unit → Feed Slot list (`displayLabel`, detected material,
  deactivated state, `currentSpool`); Add/Remove Feed Unit actions deferred to US4
- [X] T080 [P] [US2] Delete `web/src/features/spools/data/mockSpools.ts` and `mockSpoolsStore.ts`,
  updating `SpoolsPage.test.tsx` to cover the real API-backed table instead of the mock prototype
- [X] T081 [P] [US2] Vitest + RTL tests for `AddSpoolDialog.tsx`, `LoadSpoolDialog.tsx`,
  `UnloadSpoolDialog.tsx`, and `FeedUnitsCard.tsx` in their respective component directories (register →
  load → unload flow; a printer with no Feed Slots yet shows an empty/disabled load picker)

**Checkpoint**: US1 and US2 both work independently — a spool moves InStorage → Using → Finished against
a connected printer's real Feed Slots.

---

## Phase 5: User Story 3 - Automatic remaining-weight estimation (Priority: P2)

**Goal**: A loaded spool's Remaining Weight is estimated automatically from recorded print consumption,
with a manual-override escape hatch.

**Independent Test**: Load a spool of known Remaining Weight, record usage against it, confirm the
displayed remaining weight decreases accordingly, and confirm a manual correction overrides the estimate.

- [X] T082 [P] [US3] Create `RecordSpoolUsageCommand` + Validator in
  `api/src/OpenFarm.Application/FilamentTracking/RecordSpoolUsageCommand.cs` /
  `RecordSpoolUsageCommandValidator.cs` (`GramsConsumed > 0`)
- [X] T083 [US3] Implement `RecordSpoolUsageCommandHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/RecordSpoolUsageCommandHandler.cs` — requires the spool
  `Using` (409 otherwise); writes a `SpoolUsageRecord`, calls `Spool.RecordUsage` (T017's floor-at-zero/
  overrun-flag behavior); when `RemainingWeightGrams` reaches exactly zero, defers to the
  auto-continuation check (a no-op stub returning `occurred: false` in this story — US4 replaces the
  stub with the real policy-driven branch)
- [X] T084 [P] [US3] Create `CorrectRemainingWeightCommand` + Validator in
  `api/src/OpenFarm.Application/FilamentTracking/CorrectRemainingWeightCommand.cs` /
  `CorrectRemainingWeightCommandValidator.cs` — non-negative, `≤ SpoolWeightGrams` (FR-012)
- [X] T085 [US3] Implement `CorrectRemainingWeightCommandHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/CorrectRemainingWeightCommandHandler.cs` — overwrites
  `RemainingWeightGrams` and resets `HasUsageOverrun` to `false`
- [X] T086 [P] [US3] Create `RecordSpoolUsageEndpoint` and `CorrectRemainingWeightEndpoint` in
  `api/src/OpenFarm.Api/Endpoints/Spools/` — `POST /api/v1/spools/{id}/usage`
  (`[Permission("spools:record-usage")]`), `PUT /api/v1/spools/{id}/remaining-weight`
  (`[Permission("spools:override-weight")]`)
- [X] T087 [P] [US3] Integration test: recording usage against a `Using` spool decrements
  `remainingWeightGrams` by the recorded amount on the next `GET /spools`, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/RecordSpoolUsageEndpointTests.cs` (FR-006,
  US3 Acceptance Scenario 1)
- [X] T088 [P] [US3] Integration test: cumulative usage exceeding the spool's remaining weight floors
  `remainingWeightGrams` at zero and sets `hasUsageOverrun: true` rather than going negative; recording
  usage against a non-`Using` spool 409s, in the same test file (spec.md's Edge Case)
- [X] T089 [P] [US3] Integration test: a manual `PUT .../remaining-weight` overrides the estimate and
  resets `hasUsageOverrun`, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/CorrectRemainingWeightEndpointTests.cs`
  (FR-012, US3 Acceptance Scenario 2)
- [X] T090 [P] [US3] Regenerate the Orval-generated API client for the usage/correction endpoints in
  `web/src/lib/api/generated/`
- [X] T091 [US3] Create `RecordUsageDialog.tsx` in `web/src/features/spools/components/` — records
  consumption against a `Using` spool and lets an operator manually correct `remainingWeightGrams`,
  wired into `SpoolsTable.tsx`
- [X] T092 [P] [US3] Vitest + RTL test for `RecordUsageDialog.tsx` in
  `web/src/features/spools/components/` (recording usage updates the displayed remaining weight; a
  manual correction overrides it)

**Checkpoint**: US1, US2, and US3 all work independently — remaining weight tracks recorded usage and
supports manual correction.

---

## Phase 6: User Story 4 - Automatic continuation on Multi-Material Units and Automatic Filament Reloaders (Priority: P3)

**Goal**: A depleted spool automatically hands off to another already-loaded spool without operator
action, per the depleted spool's Feed Unit's kind-driven reload policy; operators can manually declare
and remove an Automatic Filament Reloader, and reveal/re-hide a superseded External Spool position.

**Independent Test**: Load two same-Filament/color spools into different positions of a connected
printer's Multi-Material Unit, deplete the active one via recorded usage, and confirm the system
auto-continues from the other; confirm a non-matching (or absent) second spool does *not* auto-continue;
separately declare an Automatic Filament Reloader and confirm its unconditional continuation, including
its own mismatch-warning path.

- [X] T093 [US4] Write `SpoolUsageRecordedHandler` auto-continuation policy tests (TDD, write first) in
  `api/tests/OpenFarm.Application.Tests/FilamentTracking/SpoolUsageRecordedHandlerTests.cs` — a
  `MultiMaterialUnit` with a matching sibling `Using` spool auto-continues (lowest `SlotPosition` wins
  ties on multiple matches); a `MultiMaterialUnit` with a non-matching or absent sibling does not
  auto-continue; an `AutomaticFilamentReloader` continues onto the next `Using` sibling regardless of
  material match, raising `materialMismatch: true` on a mismatch; an `AutomaticFilamentReloader` with no
  sibling loaded does not auto-continue; an `ExternalSpool` never auto-continues; confirm all failing
  before implementation (data-model.md's algorithm, FR-009–FR-011a)
- [X] T094 [US4] Implement `SpoolUsageRecordedHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/SpoolUsageRecordedHandler.cs` to make T093 pass —
  resolves `ReloadPolicy` via `FeedUnitKindCatalog.Resolve(feedUnit.Kind)`; on `MatchRequired`, finds a
  `Using` sibling `FeedSlot` sharing the depleted spool's `FilamentVariantId`; on `Unconditional`, finds
  the lowest-`SlotPosition` `Using` sibling regardless; marks the depleted spool `Finished`, does **not**
  call `LoadOntoFeedSlot` on the continuation target (it's already `Using` its own slot); logs
  `ActivityLogEntry` (`FeedUnitAutoContinued`, `FeedUnitMaterialMismatch` on an AFR mismatch,
  `FeedUnitNoQualifyingReload` — informational for `MatchRequired`'s no-match case, a warning for
  `Unconditional`'s no-sibling case, per data-model.md); publishes `FeedUnitAutoContinued` via
  `IFilamentTrackingEventPublisher`
- [X] T095 [US4] Replace `RecordSpoolUsageCommandHandler`'s US3 no-op continuation stub in
  `api/src/OpenFarm.Application/FilamentTracking/RecordSpoolUsageCommandHandler.cs` (existing file) with
  a real call to `SpoolUsageRecordedHandler` when `RemainingWeightGrams` reaches zero, surfacing its
  result as the endpoint's `autoContinuation` response field
- [X] T096 [P] [US4] Create `AddFeedUnitCommand` + Validator in
  `api/src/OpenFarm.Application/FilamentTracking/AddFeedUnitCommand.cs` /
  `AddFeedUnitCommandValidator.cs` — `Kind`-discriminated: `AutomaticFilamentReloader` requires `Name`
  (non-empty, unique per printer) + `slotCount ≥ 1` + optional `ReplacesFeedUnitId` (must reference an
  `ExternalSpool`-kind unit on the same printer, 422 otherwise); `ExternalSpool` accepts no extra fields
  (data-model.md's Validation rules, research.md §13)
- [X] T097 [US4] Implement `AddFeedUnitCommandHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/AddFeedUnitCommandHandler.cs` — `Kind =
  AutomaticFilamentReloader` creates new `FeedUnit`/`FeedSlot` rows (`slotCount` slots, `SlotPosition`
  0..N-1), throws `DuplicateAutomaticFilamentReloaderNameException` on a name collision (409); `Kind =
  ExternalSpool` finds the printer's existing `ExternalSpool`-kind `FeedUnit` and sets `IsRevealed =
  true` idempotently, 404 if none exists at all (FR-026 — nothing to reveal)
- [X] T098 [P] [US4] Create `RemoveFeedUnitCommand` + Handler in
  `api/src/OpenFarm.Application/FilamentTracking/RemoveFeedUnitCommand.cs` /
  `RemoveFeedUnitCommandHandler.cs` — dispatches on the target row's own `Kind`: `AutomaticFilamentReloader`
  deactivates the unit and its slots (`DeactivatedAtUtc`, spool state/history undisturbed);
  `ExternalSpool` sets `IsRevealed = false` idempotently; `MultiMaterialUnit` throws
  `NotAnAutomaticFilamentReloaderException` (422 — connector-derived, FR-005a)
- [X] T099 [US4] Extend `GetFeedUnitsQueryHandler` in
  `api/src/OpenFarm.Application/FilamentTracking/GetFeedUnitsQueryHandler.cs` (existing file from US2) —
  `supersededByFeedUnitId` now also checks the explicit `ReplacesFeedUnitId` declaration first (priority
  over the auto-derived MMU-presence source added in US2), per research.md §9/§11's priority order
- [X] T100 [P] [US4] Create `AddFeedUnitEndpoint` and `RemoveFeedUnitEndpoint` in
  `api/src/OpenFarm.Api/Endpoints/FeedUnits/` — `POST /api/v1/printers/{printerId}/feed-units`
  (`[Permission("feed-units:add")]`), `DELETE /api/v1/feed-units/{id}`
  (`[Permission("feed-units:remove")]`); response for both branches implements `IHasEntityIds` only where
  it creates new rows (`AutomaticFilamentReloader`) — otherwise the default single-`Id` activity-log path
  applies
- [X] T101 [P] [US4] Integration test: a Multi-Material Unit with two matching-Filament/color `Using`
  spools auto-continues on depletion (lowest `SlotPosition` tiebreak), a non-matching or absent second
  spool does not, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/MultiMaterialUnitAutoContinuationTests.cs`
  (FR-009, FR-010, US4 Acceptance Scenarios 1–2)
- [X] T102 [P] [US4] Integration test: declaring an Automatic Filament Reloader with two loaded spools
  auto-continues regardless of material match, raising `materialMismatch: true` on a mismatch without
  blocking; no second spool loaded yields `occurred: false` plus a `FeedUnitNoQualifyingReload` warning
  log entry, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/AutomaticFilamentReloaderTests.cs` (FR-011,
  US4 Acceptance Scenarios 3–4, spec.md's Edge Case)
- [X] T103 [P] [US4] Integration test: `POST /feed-units` creates an Automatic Filament Reloader (with
  `replacesFeedUnitId` when declared) and `DELETE /feed-units/{id}` deactivates it while leaving any
  `Using` spool's own state undisturbed; the same `DELETE` against a `MultiMaterialUnit` unit's id 422s,
  in `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/AddRemoveFeedUnitTests.cs` (FR-005c,
  spec.md's Edge Case)
- [X] T104 [P] [US4] Integration test: `POST /feed-units` with `kind: "ExternalSpool"` reveals a
  superseded position (idempotent), `DELETE /feed-units/{id}` re-hides it but FR-025's safety net keeps
  it visible while a spool is still `Using` it, and revealing a printer's never-reported External Spool
  404s, in
  `api/tests/OpenFarm.Api.IntegrationTests/FilamentTracking/RevealExternalSpoolTests.cs` (FR-022,
  FR-024, FR-025, FR-026)
- [X] T105 [P] [US4] Integration test: `FeedUnitAutoContinued`, `FeedUnitMaterialMismatch`, and
  `FeedUnitNoQualifyingReload` activity-log entries all appear where triggered, in
  `api/tests/OpenFarm.Api.IntegrationTests/ActivityLog/FeedUnitAutoContinuationActivityLogTests.cs`
  (FR-014, SC-005)
- [X] T106 [P] [US4] Regenerate the Orval-generated API client for the Add/Remove Feed Unit endpoints in
  `web/src/lib/api/generated/`
- [X] T107 [US4] Extend `FeedUnitsCard.tsx` in `web/src/features/printers/components/` (existing file
  from US2) with a single "Add a Feed Unit" action (kind picker: Automatic Filament Reloader with
  name/slot count/optional supersedes-selector, or reveal External Spool with no extra fields) and a
  "Remove" action per added/revealed unit; subscribes to `FeedUnitsChanged` for live refresh
- [X] T108 [P] [US4] Vitest + RTL tests for `FeedUnitsCard.tsx`'s Add/Remove actions in
  `web/src/features/printers/components/` (declare AFR → appears with its slots; reveal External Spool →
  appears; remove either → disappears, unless a spool is loaded on the External Spool)

**Checkpoint**: All four user stories are independently functional — a depleted spool auto-continues per
its Feed Unit's kind, and operators can declare/remove an Automatic Filament Reloader or reveal/re-hide a
superseded External Spool position.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T109 [P] Run `dotnet format api/OpenFarm.slnx --verify-no-changes` and `bun run lint` / `bun run
  format` across all new/changed files, fixing any violations — `bun run lint`/`format` done, all
  clean on every hand-written file touched by this feature (`biome check` reports 0 errors/warnings
  for that file set; remaining repo-wide warnings are pre-existing, in Orval-generated files and
  `main.tsx`, untouched by this feature). `dotnet format` could not run: this sandbox only has SDK
  10.0.110 installed via apt while `api/global.json` pins 10.0.301 with `rollForward: latestFeature`
  (forward-only, can't resolve to the lower installed version), and fetching a matching SDK installer
  is blocked by the environment's egress policy (403 from the proxy on dot.net) — a known, previously
  documented limitation of this sandbox, not a code issue. Did not hand-edit `global.json` per
  CLAUDE.md's explicit instruction.
- [ ] T110 Run the full `quickstart.md` validation (Scenarios 1–7 plus the concurrency edge case and the
  activity-log coverage check) against a local `docker compose up` stack with the virtual/simulator
  printer — blocked in this sandbox for the same reason as T111 (no matching dotnet SDK to run the
  API, and Testcontainers/Docker registry pulls are blocked by the egress policy); not run.
- [X] T111 Run `dotnet test api/OpenFarm.slnx` and `bun run test` end-to-end and confirm the full suite
  passes — `bun run test`: 127/127 passing across 39 test files (full frontend suite, including this
  feature's new component/dialog/page tests). `dotnet test` could not run in this sandbox: same SDK
  10.0.110-vs-10.0.301 mismatch as T109 blocks `dotnet build`/`test`/`run` entirely here. The backend
  test suite (Domain/Application unit tests + Api integration tests) was written test-first per the
  constitution and confirmed red→green during implementation earlier in this session, before the SDK
  became unavailable; it has not been re-run since.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Stories (Phase 3+)**: All depend on Foundational completion.
  - US1 (Phase 3) has no dependency on any other story — pure catalog CRUD.
  - US2 (Phase 4) depends only on Foundational (needs `GetFeedUnitsQuery`'s connector-derived data and
    the `Spool` entity); it also needs US1's `Filament`/`FilamentVariant` catalog to exist for
    `RegisterSpoolCommand` to reference, but not any of US1's command/query *code* directly — both can
    be built in parallel once Foundational lands, exercised together only at the integration-test level.
  - US3 (Phase 5) depends on US2's `Spool` state machine and `LoadSpoolOntoFeedSlotCommand` (T064)
    existing, since usage recording only applies to a `Using` spool.
  - US4 (Phase 6) depends on US3's `RecordSpoolUsageCommandHandler` (T083) existing, since it replaces
    that handler's no-op continuation stub (T095), and on US2's `GetFeedUnitsQueryHandler` (T062),
    since it extends that same handler's `supersededByFeedUnitId` derivation (T099).
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Within Each User Story

- TDD tasks (T010 catalog, T012/T013 transitions, T016/T017 usage math, T059 validator, T093/T094
  continuation policy) are written test-first, confirmed failing, then implemented.
- Test-after tasks (parser fixture-replay, integration tests, component tests) are implemented first,
  then verified.
- Entities/interfaces before handlers; handlers before endpoints; endpoints before frontend hooks;
  frontend hooks before components.

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel.
- Within Foundational: T003–T005 (catalog entities), T006–T009 (reload-policy types), T011/T014/T015
  (enum/exceptions), T018–T019 (independent extras), T021–T024 (EF configs), T027 (repository
  interfaces), T028 (repository implementations) are each parallel groups; T025/T026 (migrations) need
  their respective entities/configs first; T030 (sync handler) needs T029 (contract) first; T032
  (`ParseExternalSpool` impl) needs T031 (its tests) first; T033 needs T030/T032.
- Once Foundational completes, US1 and US2's backend tasks can largely proceed in parallel (different
  files) — US2's `Spool`/`FeedUnit` code doesn't touch anything US1's `Vendor`/`Filament` code touches.
  US3 must wait on US2's T064. US4 must wait on US3's T083 and US2's T062.
- Within US1: T040/T042/T043/T045/T047 (commands/queries) and T048–T049 (endpoints, once their handlers
  exist) are parallel groups.
- Within US2: T058/T061/T062 (commands/queries) and T066/T067–T073 (endpoints/integration tests, once
  implementation exists) are parallel groups.
- Within US4: T096/T098 (Add/Remove Feed Unit command scaffolding) and T101–T105 (integration tests,
  once their implementation exists) are parallel groups.

---

## Parallel Example: User Story 2

```bash
# Once Foundational and US1 are complete, launch these together:
Task: "Create RegisterSpoolCommand + Validator in api/src/OpenFarm.Application/FilamentTracking/RegisterSpoolCommand.cs"
Task: "Create GetSpoolsQuery + Handler in api/src/OpenFarm.Application/FilamentTracking/GetSpoolsQuery.cs"
Task: "Create GetFeedUnitsQuery + Handler in api/src/OpenFarm.Application/FilamentTracking/GetFeedUnitsQuery.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (blocks all stories).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart.md Scenario 1 independently.
5. Deploy/demo if ready — a working Vendor/Filament catalog with no spool tracking yet.

### Incremental Delivery

1. Setup + Foundational → foundation ready (connector-derived Feed Units already syncing).
2. US1 → test independently (quickstart Scenario 1) → deploy/demo (MVP!).
3. US2 → test independently (quickstart Scenarios 2–3) → deploy/demo.
4. US3 → test independently (usage-recording flow, no dedicated quickstart scenario beyond what US4's
   Scenario 4 exercises) → deploy/demo.
5. US4 → test independently (quickstart Scenarios 4–5d) → deploy/demo.
6. Polish → full quickstart + full suite run.

### Parallel Team Strategy

With multiple developers, after Foundational completes:
- Developer A: US1 (independent).
- Developer B: US2 (independent of US1's code, needs its catalog data at integration-test time).
- Once US2 lands, Developer A or C: US3.
- Once US3 lands, Developer B or C: US4.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- [Story] label maps a task to its user story for traceability; Setup/Foundational/Polish carry no
  label.
- Permission strings (`vendors:create`, `vendors:list`, `filaments:create`, `filaments:add-variant`,
  `filaments:list`, `feed-units:list`, `feed-units:add`, `feed-units:remove`, `spools:register`,
  `spools:list`, `spools:load`, `spools:unload`, `spools:record-usage`, `spools:override-weight`) follow
  the existing `printers:*`/`profiles:*` naming convention.
- No `StandbySpoolId`, no per-slot AFR-enable flag, no `PUT .../automatic-filament-reloader` "configure"
  endpoint, no toolhead/nozzle field on `FeedUnit`, no broader Feed-Unit-to-Feed-Unit wiring graph beyond
  `AutomaticFilamentReloader → ExternalSpool` — all deliberately cut per plan.md's Complexity Tracking; do
  not add them back without a new FR/SC driving it.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
