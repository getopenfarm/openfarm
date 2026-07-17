---

description: "Task list for Virtual Printer (Simulated Printer for UI Testing)"
---

# Tasks: Virtual Printer (Simulated Printer for UI Testing)

**Input**: Design documents from `/specs/004-virtual-printer/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/IPrinterConnectorFactory.md, quickstart.md

**Re-plan note**: This tasks list **replaces** an earlier version built against IP-address-based connector
selection (already fully implemented and merged). The 2026-07-17 clarification follow-up reversed that
decision in favor of a Domain-catalog-based Brand/Model selection. Every task below describes **rework of
existing, working code** — see plan.md's "What already exists vs. what changes" table — not a
from-scratch build. The simulator's own behavior (`SimulatorPrinterConnector`, `SimulatorPrinterCameraRelay`,
`SimulatedPrinterState`, the Idle→Printing→AwaitingClear lifecycle, the AMS fixture) is unaffected and
needs no task here.

**Tests**: Test migrations are included per-story since the plan's own Project Structure section lists
the exact test files affected as in-scope deliverables (test-after, per CLAUDE.md's testing strategy —
this is Infrastructure/orchestration rework, not a Domain state machine).

**Organization**: Tasks are grouped by user story (US1 → US3, in spec.md priority order) to enable
independent implementation and testing of each story. Foundational work spans both `api/` and `web/`
this round, unlike the original (backend-only) implementation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Every task lists its exact file path(s), grounded in the current repo layout.

## Path Conventions

Existing `api/` Clean Architecture layout + `web/` SPA, unchanged structurally. This rework touches
`OpenFarm.Domain` (one catalog entry), `OpenFarm.Application` (one record field addition + its call
sites), `OpenFarm.Infrastructure` (the factory's routing rule + one file deletion), `OpenFarm.Api` (one
request DTO), and `web/` (the Add-printer dialog's new Brand selector + a Orval regeneration) — the first
time this feature has needed a frontend change.

---

## Phase 1: Setup

**Purpose**: No new project, package, directory, or tooling is needed — every file this rework touches
already exists (created by the original implementation). Proceed directly to Foundational.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The connector-selection mechanism itself must be reworked before any user story's
independent test is meaningful again — US1's own independent test *is* "register via the Brand/Model
catalog," so the catalog entry, the `PrinterConnectionDetails` shape change, and the factory's routing
rule are prerequisites for every story, not story-specific work.

**⚠️ CRITICAL**: T001-T011 must all complete before any User Story phase's tasks can be verified —
registering *any* printer (real or simulated) flows through `PrinterConnectionDetails`/
`PrinterConnectorFactory`, so a half-migrated state breaks every registration, not just simulated ones.

- [X] T001 [P] Add a `System` member to the `PrinterBrand` enum in `api/src/OpenFarm.Domain/Printers/PrinterModel.cs` (alongside the existing `Bambu` member).
- [X] T002 Add a `(PrinterBrand.System, "Simulation")` entry to `PrinterModelCatalog`'s `Entries` dictionary in `api/src/OpenFarm.Domain/Printers/PrinterModelCatalog.cs`, mapping to `new PrinterModel(PrinterBrand.System, "Simulation", CameraTransport.BinaryImage)` — `BinaryImage` (not a new transport) so the existing camera-loop gate in `PrinterConnectionBackgroundService` triggers unmodified, giving every simulated printer camera fidelity unconditionally (data-model.md, research.md §3) — depends on T001.
- [X] T003 [P] Extend the `PrinterConnectionDetails` record in `api/src/OpenFarm.Application/Printers/IPrinterConnector.cs` with two new leading fields: `PrinterConnectionDetails(string Brand, string ModelCode, string IpAddress, string SerialNumber, string AccessCode)` (research.md §2). `IPrinterConnectorFactory`'s own method signatures do not change.
- [X] T004 Rewrite `PrinterConnectorFactory.IsSimulated` in `api/src/OpenFarm.Infrastructure/Printers/PrinterConnectorFactory.cs` to route on `string.Equals(details.Brand, "System", StringComparison.OrdinalIgnoreCase) && string.Equals(details.ModelCode, "Simulation", StringComparison.OrdinalIgnoreCase)`, removing the `IPAddress.TryParse`/`AddressFamily`/`SimulatedPrinterAddressRange.Contains` logic and the now-unused `System.Net`/`System.Net.Sockets` usings — depends on T003.
- [X] T005 Delete `api/src/OpenFarm.Infrastructure/Printers/Simulator/SimulatedPrinterAddressRange.cs` outright (research.md §5) — depends on T004 (the factory must stop referencing it first).
- [X] T006 [P] Update `RegisterPrinterCommandHandler` in `api/src/OpenFarm.Application/Printers/RegisterPrinterCommandHandler.cs` to construct `PrinterConnectionDetails` as `new PrinterConnectionDetails(command.Brand, command.ModelCode, command.IpAddress, command.SerialNumber, command.AccessCode)` — depends on T003.
- [X] T007 [P] Update `EditPrinterConnectionCommandHandler` in `api/src/OpenFarm.Application/Printers/EditPrinterConnectionCommandHandler.cs` to construct `PrinterConnectionDetails` as `new PrinterConnectionDetails(printer.Brand, printer.ModelCode, command.IpAddress, printer.SerialNumber, command.AccessCode)` (the already-loaded `printer` entity carries `Brand`/`ModelCode`; a printer's brand/model cannot be edited, so these come from the loaded entity, not the command) — depends on T003.
- [X] T008 [P] Update `PrinterConnectionBackgroundService.RunConnectionLoopAsync` in `api/src/OpenFarm.Infrastructure/Printers/PrinterConnectionBackgroundService.cs` to construct `details` as `new PrinterConnectionDetails(printer.Brand, printer.ModelCode, printer.IpAddress, printer.SerialNumber, secretProtector.Unprotect(secret.EncryptedAccessCode))` (the already-loaded `printer` entity carries `Brand`/`ModelCode`) — depends on T003.
- [X] T009 Add `Brand`/`ModelCode` string properties to `VerifyPrinterConnectionRequest` and matching `RuleFor(x => x.Brand).NotEmpty()`/`RuleFor(x => x.ModelCode).NotEmpty()` rules to `VerifyPrinterConnectionRequestValidator` in `api/src/OpenFarm.Api/Endpoints/Printers/VerifyPrinterConnectionEndpoint.cs`; update `HandleAsync` to construct `details` as `new PrinterConnectionDetails(req.Brand, req.ModelCode, req.IpAddress, req.SerialNumber, req.AccessCode)` — depends on T003.
- [X] T010 Rewrite `api/tests/OpenFarm.Infrastructure.Tests/Printers/Simulator/PrinterConnectorFactoryTests.cs`'s five test cases off IP literals onto Brand/ModelCode combinations: `CreateConnector`/`CreateCameraRelay` return the simulator singleton for `PrinterConnectionDetails("System", "Simulation", <any IP>, "SN-1", "12345678")`; return a fresh `BambuLanConnector`/`BambuCameraRelay` each call for `PrinterConnectionDetails("Bambu", "P1S", <any IP>, "SN-2", "12345678")`; a lower-case `"system"`/`"simulation"` combo still routes to the simulator (case-insensitivity); an empty or unrecognized Brand/ModelCode combo never throws and routes to Bambu (replaces the retired IPv6/malformed-IP theory cases, since IP no longer affects routing) — depends on T004, T005.
- [X] T011 `dotnet build api/OpenFarm.slnx --configuration Release` and `dotnet test api/tests/OpenFarm.Infrastructure.Tests/OpenFarm.Infrastructure.Tests.csproj` to confirm the foundational rework compiles and the factory unit tests pass — depends on T001-T010.

**Checkpoint**: The connector-selection mechanism is fully reworked and unit-tested against Brand/ModelCode — every user story's registration/verify path now routes correctly.

---

## Phase 3: User Story 1 - Register a simulated printer through the normal flow (Priority: P1) 🎯 MVP

**Goal**: A developer registers a printer via the standard Add-printer flow, selecting Brand "System" and
Model "Simulation" from a real dropdown (not an invisible IP convention); it verifies, connects, and
appears in the fleet exactly like real hardware, visibly labeled "System / Simulation," coexisting with
real printers.

**Independent Test**: Register one simulated printer through the Add-printer flow by selecting Brand
System/Model Simulation with no physical hardware present; confirm it appears in the fleet list with a
live `Idle`/reachable status and its Brand/Model columns show "System / Simulation".

### Implementation for User Story 1

- [X] T012 [US1] Add a Brand selector to `web/src/features/printers/components/AddPrinterDialog.tsx`: a `PRINTER_BRANDS = ['Bambu', 'System'] as const` list, a `MODELS_BY_BRAND: Record<(typeof PRINTER_BRANDS)[number], readonly string[]>` map (`Bambu` → the existing 7 codes, `System` → `['Simulation']`), a `brand` field added to the zod `schema` (`z.enum(PRINTER_BRANDS)`, default `'Bambu'`), and a new `<select>` `FormField` for Brand rendered above the Model field. The Model `<select>`'s options become `MODELS_BY_BRAND[form.watch('brand')]` instead of the flat `PRINTER_MODELS` constant; changing Brand resets `modelCode` to that brand's first entry via `form.setValue`. `onSubmit` sends `values.brand` instead of the hardcoded `'Bambu'`.
- [X] T013 [US1] In the same file, update `handleConnectionFieldBlur`'s `verifyMutation.mutate({ data: {...} })` payload to include `brand`/`modelCode` alongside `ipAddress`/`serialNumber`/`accessCode`; add an `onChange` handler to both the Brand and Model `<select>` elements that calls `setVerified(false)` and re-runs the same verify call (mirroring the existing blur-triggered behavior), so switching Brand/Model after already verifying doesn't leave a stale `verified: true` pointed at the wrong connector — depends on T012, T009.
- [X] T014 [US1] Run the API locally, export its generated OpenAPI document over `api/openapi.json`, then run `bunx orval` inside `web/` to regenerate `web/src/lib/api/generated/` against `VerifyPrinterConnectionRequest`'s new `brand`/`modelCode` fields — depends on T009.
- [X] T015 [US1] Migrate `api/tests/OpenFarm.Api.IntegrationTests/Printers/RegisterPrinterEndpointTests.cs`'s `ValidRequest` helper to default `Brand: "System", ModelCode: "Simulation"` (dropping the `Bambu`/`P1S` default and the `192.0.2.0/24` addresses — IP literals become arbitrary well-formed placeholders, e.g. `10.0.0.50`/`10.0.0.51`/`10.0.0.99`, since they no longer affect routing) so all three existing assertions (created, duplicate-serial-409, unverifiable-422) keep passing without real hardware — depends on T004-T009.
- [X] T016 [US1] `cd web && bun run build` (runs `tsc -b && vite build`) to confirm the dialog's Brand-selector change compiles against the regenerated Orval client — depends on T012, T013, T014.
- [X] T017 [US1] `dotnet build api/OpenFarm.slnx --configuration Release` and `dotnet test api/tests/OpenFarm.Api.IntegrationTests/OpenFarm.Api.IntegrationTests.csproj --filter "FullyQualifiedName~RegisterPrinterEndpointTests"` to confirm US1's registration flow passes end-to-end under Brand/ModelCode-based selection — depends on T015.

**Checkpoint**: User Story 1 fully functional — a printer registered with Brand System/Model Simulation routes to the simulator, verifies, connects, and appears Idle/reachable in the fleet, visibly labeled "System / Simulation"; a Bambu-brand printer is unaffected (SC-001, SC-004, SC-005).

---

## Phase 4: User Story 2 - Drive the simulated printer's state to test the UI (Priority: P2)

**Goal**: A connected simulated printer (now selected via Brand/Model instead of IP) still drives the
complete Idle → Printing → AwaitingClear → Idle lifecycle exactly as before — this story only needed its
test fixtures' *registration* payload updated, since `SimulatorPrinterConnector`'s own lifecycle logic is
untouched by the selection-mechanism rework.

**Independent Test**: Register a simulated printer with Brand System/Model Simulation, call
`SimulatorPrinterConnector.StartSimulatedJob` directly (research.md §6 — no live UI trigger exists yet)
and confirm `Printer.Status` transitions Idle → Printing → AwaitingClear with SignalR broadcasts, then
confirm "Mark ready" returns it to Idle.

### Implementation for User Story 2

- [X] T018 [P] [US2] Migrate `api/tests/OpenFarm.Api.IntegrationTests/Printers/MarkPrinterReadyEndpointTests.cs`'s two `RegisterPrinterRequest` literals (`RegisterAwaitingClearPrinterAsync`'s and `MarkReady_FromIdle_Returns409...`'s) from `Brand: "Bambu", ModelCode: "P1S", IpAddress: "192.0.2.70"/"192.0.2.71"` to `Brand: "System", ModelCode: "Simulation"` with any placeholder IP — depends on T004-T009.
- [X] T019 [P] [US2] Migrate `api/tests/OpenFarm.Api.IntegrationTests/Printers/SimulatedPrintLifecycleTests.cs`'s two `RegisterPrinterRequest` literals the same way (`Brand: "System", ModelCode: "Simulation"`, any placeholder IP instead of `192.0.2.90`/`192.0.2.91`) — depends on T004-T009.
- [X] T020 [US2] `dotnet test api/tests/OpenFarm.Api.IntegrationTests/OpenFarm.Api.IntegrationTests.csproj --filter "FullyQualifiedName~MarkPrinterReadyEndpointTests|FullyQualifiedName~SimulatedPrintLifecycleTests"` to confirm the full Idle→Printing→AwaitingClear→Idle lifecycle and Mark Ready still pass under Brand/ModelCode-based registration — depends on T018, T019.

**Checkpoint**: User Stories 1 AND 2 both work independently under the new selection mechanism — the lifecycle and Mark Ready paths are proven unaffected by the rework (SC-003, SC-007).

---

## Phase 5: User Story 3 - Populate AMS and camera panels (Priority: P3)

**Goal**: A connected simulated printer still reports full-fidelity default AMS contents and produces a
camera image, exactly as before — and, as a direct improvement from the catalog-based selection, camera
fidelity is now guaranteed unconditionally (the one Simulation model always carries
`CameraTransport.BinaryImage`), rather than depending on which Bambu model code a tester happened to pick.

**Independent Test**: Open a connected simulated printer's detail view (Brand System/Model Simulation)
and confirm the AMS panel shows four populated slots and the camera panel shows a live-looking image.

### Implementation for User Story 3

- [X] T021 [P] [US3] Verification-only: run `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~Simulator"` and confirm `SimulatorPrinterConnectorTests`/`SimulatorPrinterCameraRelayTests` still pass unmodified — the AMS fixture and camera-frame generation are unaffected by the selection-mechanism rework, so no production code changes here — depends on Phase 2 completing (build must succeed).
- [X] T022 [US3] Follow quickstart.md's "User Story 3 — AMS and camera panels populate" steps against a locally running stack: register a simulated printer via the Add-printer dialog's Brand System/Model Simulation selector, open its detail view, and confirm the AMS panel renders four populated slots and the camera panel renders a placeholder image instead of "unavailable" (SC-002) — depends on T012, T013, T014.

**Checkpoint**: All three user stories independently functional under Brand/ModelCode-based selection — full-fidelity telemetry, status, AMS, and camera all populate on a simulated printer registered through the catalog.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final architecture-conformance checks spanning all three stories (SC-004, SC-006) and the
regression guarantee that the selection-mechanism swap didn't silently break real printers (SC-005) or
leave dead IP-range code behind.

- [X] T023 [P] Migrate `api/tests/OpenFarm.Api.IntegrationTests/Printers/RemovePrinterEndpointTests.cs`'s `RegisterPrinterRequest` literal from `Brand: "Bambu", ModelCode: "P1S", IpAddress: "192.0.2.80"` to `Brand: "System", ModelCode: "Simulation"` with any placeholder IP, so registration doesn't attempt a real MQTT connection in CI — depends on T004-T009.
- [X] T024 [P] Run quickstart.md's "Architecture review checks": `grep -ri "virtual\|simulat" api/src/OpenFarm.Domain api/src/OpenFarm.Application web/src` and confirm every match is either the ordinary `"System"`/`"Simulation"` Brand/ModelCode catalog value or an explanatory comment — no branch, flag, or special-cased behavior keyed on it exists above the connector boundary (SC-004).
- [X] T025 [P] Diff review confirming all production changes are confined to: the one Domain catalog entry (`PrinterModel.cs`, `PrinterModelCatalog.cs`), `PrinterConnectionDetails`'s field addition and its four call sites (T003, T006-T009), `PrinterConnectorFactory`'s routing rule (T004) and the deleted `SimulatedPrinterAddressRange.cs` (T005), `VerifyPrinterConnectionRequest`'s shape change (T009), `AddPrinterDialog.tsx`'s Brand selector (T012-T013), and the regenerated Orval client (T014) — nothing else in `OpenFarm.Domain`, no new endpoint, no other frontend file changed (SC-006).
- [X] T026 Confirm no `192.0.2.0/24` or `SimulatedPrinterAddressRange` reference remains anywhere in `api/src` or `api/tests` — depends on T005, T010, T015, T018, T019, T023.
- [X] T027 `dotnet format api/OpenFarm.slnx --verify-no-changes`, `cd web && bun run lint`, and a final full `dotnet test api/OpenFarm.slnx` run across all four test projects to confirm the complete rework (Phases 2-6) passes CI's lint and test gates together — depends on all prior tasks.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: None — no scaffolding needed this round.
- **Foundational (Phase 2)**: BLOCKS all user stories — the Brand/ModelCode-based `PrinterConnectionDetails` shape and `PrinterConnectorFactory` routing rule (T001-T010) must exist and compile before any story's registration flow works at all, real or simulated.
- **User Story 1 (Phase 3)**: Depends on Foundational. Delivers the MVP — a Brand/Model-selectable, connectable, fleet-visible simulated printer.
- **User Story 2 (Phase 4)**: Depends on Foundational (the registration payload shape) but not on User Story 1's frontend/Orval tasks (T012-T014) — its integration tests build `RegisterPrinterRequest` literals directly, bypassing the dialog entirely.
- **User Story 3 (Phase 5)**: T021 depends only on Foundational (build must succeed); T022 (manual verification) depends on US1's dialog/Orval tasks since it drives the actual Add-printer UI.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: No dependency on US2/US3 — independently testable via the fleet-registration flow alone, once Foundational lands.
- **User Story 2 (P2)**: Independent of US1's frontend work — its tests construct HTTP requests directly, not through the dialog.
- **User Story 3 (P3)**: Its unit-test verification (T021) is independent of US1/US2; its manual-verification task (T022) reuses US1's dialog change.

### Within Each User Story

- Foundational connector/factory rework (T001-T011) before any story's registration payload is migrated.
- Test-fixture migration before the story's build+test checkpoint.

### Parallel Opportunities

- T001, T003 can run in parallel (distinct files, no dependencies among them).
- T006, T007, T008 can run in parallel once T003 lands (distinct files: register handler, edit handler, background service).
- T018, T019 can run in parallel (distinct test files, same migration pattern).
- T023, T024, T025 can run in parallel with each other.

---

## Parallel Example: Foundational Phase

```bash
# Launch independent Foundational tasks together (after T001/T003 land):
Task: "Update RegisterPrinterCommandHandler to pass Brand/ModelCode into PrinterConnectionDetails"
Task: "Update EditPrinterConnectionCommandHandler to pass Brand/ModelCode into PrinterConnectionDetails"
Task: "Update PrinterConnectionBackgroundService to pass Brand/ModelCode into PrinterConnectionDetails"
```

## Parallel Example: User Story 2 test migrations

```bash
Task: "Migrate MarkPrinterReadyEndpointTests.cs to Brand: System/ModelCode: Simulation"
Task: "Migrate SimulatedPrintLifecycleTests.cs to Brand: System/ModelCode: Simulation"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (none needed).
2. Complete Phase 2: Foundational — the Domain catalog entry, `PrinterConnectionDetails` shape change, and the factory's Brand/ModelCode routing rule, all unit-tested.
3. Complete Phase 3: User Story 1 — Add the frontend Brand selector, regenerate Orval, migrate the registration test fixture.
4. **STOP and VALIDATE**: Register a simulated printer through the live UI by selecting Brand System/Model Simulation with no physical hardware; confirm it appears in the fleet Idle/reachable labeled "System / Simulation," and that a Bambu-brand printer still resolves to `BambuLanConnector` (factory-level, T010).
5. Ship — this alone satisfies SC-001, SC-004, SC-005 and unblocks all UI testing that doesn't need job-lifecycle or AMS/camera coverage.

### Incremental Delivery

1. Setup + Foundational → connector-selection rework ready, fully unit-tested, nothing user-facing yet.
2. Add User Story 1 → fleet registration/connection works end-to-end via the new Brand selector → validate → this is the MVP.
3. Add User Story 2 → full print-lifecycle state machine re-validated against the new registration payload → validate via `dotnet test --filter "FullyQualifiedName~MarkPrinterReady|FullyQualifiedName~SimulatedPrintLifecycle"`.
4. Add User Story 3 → AMS/camera panels re-validated, both by unit test and one manual quickstart pass → validate in the browser.
5. Polish → architecture-conformance greps, full regression pass, `dotnet format`/`bun run lint` checks.

### Parallel Team Strategy

With multiple developers, after Foundational (Phase 2) completes:
- Developer A: User Story 1's frontend Brand selector + Orval regeneration + registration-test migration (T012-T017) — the critical path, since US3's manual verification depends on it.
- Developer B: User Story 2's lifecycle-test migrations (T018-T020) — fully independent of US1's frontend work.
- Developer C: User Story 3's verification-only unit-test check (T021) — fully independent until the manual verification step (T022).

---

## Notes

- No EF Core migration task appears anywhere — `PrinterModelCatalog` is a compile-time dictionary, not persisted data; this rework touches zero database schema.
- No new frontend route or component appears — `AddPrinterDialog.tsx` gains a selector, it isn't replaced.
- `SimulatorPrinterConnector`, `SimulatorPrinterCameraRelay`, and `SimulatedPrinterState` need no code changes anywhere in this tasks list — the entire rework is about *how a printer opts into the simulator*, not what the simulator does once selected.
- [P] tasks touch different files with no completion-order dependency between them.
- Commit after each task or logical group; stop at any Phase checkpoint to validate independently.
- Avoid: touching `SimulatorPrinterConnector`/`SimulatorPrinterCameraRelay`/`SimulatedPrinterState`, adding any new endpoint, or reintroducing any IP-address-based branch — any task that appears to require this is out of scope for this rework and should be flagged rather than done.
