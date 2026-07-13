# Tasks: Phase 1 — Fleet Management + Live Bambu Integration

**Input**: Design documents from `/specs/002-printer-integration/` (plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md)

**Prerequisites**: plan.md, spec.md ✅ present. research.md, data-model.md, contracts/, quickstart.md ✅ present.

**Tests**: Included, matching the constitution's per-layer testing rules — TDD (RED-first) for `Domain` state machine/catalog logic, fixture-replay (test-after, not RED-first) for `BambuLanConnector`/`BambuCameraRelay`, and test-after coverage for `Application` validators/handlers and one activity-log integration test. React component tests follow the existing `*.test.tsx` convention already used across `web/src/features/*`.

**Organization**: Tasks are grouped by user story (US1–US5, priorities from spec.md) to enable independent implementation and testing of each. `FR-002`'s edit/remove requirements ride along with US1's phase, since spec.md ties no separate Acceptance Scenario/Independent Test to them and they only make sense once US1's registration flow exists.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5); omitted for Setup/Foundational/Polish
- File paths are exact and repo-relative

---

## Phase 1: Setup

**Purpose**: Environment prerequisites — no product code yet.

- [X] T001 Verify the Bambu reference submodule is populated recursively: run `git submodule update --init --recursive` from repo root and confirm `third_party/printers/bambulab/references/ha-bambulab/custom_components/bambu_lab/pybambu/` contains real source files (already done once this session — this documents the reproducible step for a fresh clone)
- [X] T002 [P] Add the MQTTnet package reference to `api/src/OpenFarm.Infrastructure/OpenFarm.Infrastructure.csproj`
- [X] T003 [P] Create a new `OpenFarm.Infrastructure.Tests` xUnit project at `api/tests/OpenFarm.Infrastructure.Tests/OpenFarm.Infrastructure.Tests.csproj` (mirroring `api/tests/OpenFarm.Domain.Tests`'s setup) and register it under `<Folder Name="/tests/">` in `api/OpenFarm.slnx` — needed because no Infrastructure-layer test project exists yet, and the constitution requires fixture-replay tests for `BambuLanConnector`
- [X] T004 Run the throwaway Bambu MQTT spike (`docs/FEATURE_PLAN.md` M0.1, no product code): connect a raw MQTTnet/mosquitto client to a physical or Bambu-Studio-simulated printer in LAN-only + Developer Mode, capture real JSON payloads for `report`/`get_version`/`pushall`/camera frames, and save them under `api/tests/OpenFarm.Infrastructure.Tests/Printers/Fixtures/*.json` for later fixture-replay tests (T028, T037, T045) — **deviation**: no physical/simulated printer reachable from this sandbox; used real `pybambu` test-corpus fixtures (`third_party/printers/bambulab/references/ha-bambulab/.../pybambu/tests/MOCK-*.json`) instead, copied into `Printers/Fixtures/` with provenance documented in that folder's `README.md`. Physical-hardware validation remains a required manual step (quickstart.md's own separate section).

**Checkpoint**: Repo ready for Foundational work. T004 requires physical (or simulated) hardware access — if unavailable, `FakePrinterConnector` (T013) unblocks every user story's frontend/Application work regardless; only `BambuLanConnector`'s own fixture-replay tests (T028) are blocked on T004.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain/Application/Infrastructure/Api scaffolding every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain (`OpenFarm.Domain`) — TDD required per constitution

- [X] T005 [P] Write `PrinterModelCatalogTests` (RED) in `api/tests/OpenFarm.Domain.Tests/Printers/PrinterModelCatalogTests.cs` — resolves all 7 catalog entries from `data-model.md`'s `PrinterModel` table (`X1`, `X1C`, `X1E` → `Rtsp`; `P1P`, `P1S`, `A1`, `A1MINI` → `BinaryImage`); an unrecognized `(Brand, ModelCode)` throws `UnknownPrinterModelException`
- [X] T006 Implement `PrinterModel`, `PrinterModelCatalog`, `UnknownPrinterModelException` in `api/src/OpenFarm.Domain/Printers/PrinterModel.cs`, `PrinterModelCatalog.cs`, `UnknownPrinterModelException.cs` to make T005 pass
- [X] T007 [P] Write `PrinterStatusTransitionsTests` (RED) in `api/tests/OpenFarm.Domain.Tests/Printers/PrinterStatusTransitionsTests.cs` — `TransitionTo(AwaitingClear → Idle)` succeeds; every other manual transition throws `InvalidPrinterStatusTransitionException`; `ObserveStatus` reconciles for every `gcode_state` mapping in `research.md` §2 regardless of current status, with no exception ever thrown
- [X] T008 Implement `PrinterStatus`, `PrinterStatusTransitions` (`TransitionTo`/`ObserveStatus`), `InvalidPrinterStatusTransitionException` in `api/src/OpenFarm.Domain/Printers/PrinterStatus.cs`, `PrinterStatusTransitions.cs`, `InvalidPrinterStatusTransitionException.cs` to make T007 pass
- [X] T009 [P] Write `PrinterTests` (RED) in `api/tests/OpenFarm.Domain.Tests/Printers/PrinterTests.cs` — `Create()` validation per `data-model.md` (`Name`/`IpAddress`/`SerialNumber` required; `(Brand, ModelCode)` must resolve via `PrinterModelCatalog`), no public setters/no `Update*` method (mirrors `ConnectionSecretTests.ConnectionSecret_HasNoSettersOrUpdateMethod`)
- [X] T010 Implement the `Printer` entity in `api/src/OpenFarm.Domain/Printers/Printer.cs` (fields per `data-model.md`: `Id`, `OrganizationId`, `Name`, `Brand`, `ModelCode`, `IpAddress`, `SerialNumber`, `Status`, `IsReachable`, `LastMessageUtc`, inline telemetry columns, `AmsUnits`, `CreatedAtUtc`) to make T009 pass — depends on T006, T008

### Application (`OpenFarm.Application`)

- [X] T011 [P] Define `IPrinterConnector` in `api/src/OpenFarm.Application/Printers/IPrinterConnector.cs` — `VerifyAsync` (short-lived connect+auth per `research.md` §7) plus a domain-shaped telemetry/status/camera event stream (never raw MQTT JSON, per plan.md's Clean Architecture gate)
- [X] T012 [P] Define `IPrinterRepository` in `api/src/OpenFarm.Application/Printers/IPrinterRepository.cs`

### Infrastructure (`OpenFarm.Infrastructure`)

- [X] T013 [P] Implement `FakePrinterConnector` in `api/src/OpenFarm.Infrastructure/Printers/FakePrinterConnector.cs` implementing `IPrinterConnector`, with test/dev-triggerable status/telemetry/camera events — depends on T011
- [X] T014 Implement `PrinterConfiguration` (EF Core entity config: composite unique index `(OrganizationId, SerialNumber)`, `AmsUnits` mapped as `jsonb`) in `api/src/OpenFarm.Infrastructure/Persistence/Configurations/PrinterConfiguration.cs` — depends on T010
- [X] T015 Implement `PrinterRepository` in `api/src/OpenFarm.Infrastructure/Persistence/PrinterRepository.cs` implementing `IPrinterRepository` — depends on T012, T014
- [X] T016 Add a `Printers` `DbSet` to `OpenFarmDbContext` and generate the single `AddPrinters` EF Core migration in `api/src/OpenFarm.Infrastructure/Persistence/Migrations/` (one migration per constitution) — depends on T014
- [X] T017 Implement `PrinterConnectionBackgroundService` in `api/src/OpenFarm.Infrastructure/Printers/PrinterConnectionBackgroundService.cs` — owns per-printer `IPrinterConnector` lifecycle, records `LastMessageUtc` on every message, runs the ~30s unreachable sweep on a 5s interval (`research.md` §6), calls `ObserveStatus` + `IActivityLogWriter.WriteAsync(ActorType.System, actorId: null)` directly on any connector-reported status change (since the HTTP-bound `ActivityLogPostProcessor` can't fire here, per plan.md's Security & Multi-Tenancy decision) — depends on T008, T011, T013
- [X] T018 [P] Write `PrinterConnectionBackgroundServiceTests` in `api/tests/OpenFarm.Infrastructure.Tests/Printers/PrinterConnectionBackgroundServiceTests.cs` using `FakePrinterConnector` — asserts the reachability sweep flips `IsReachable` to `false` after ~30s of silence and back to `true` on the next message, and that a connector-reported status mismatch on reconnect calls `ObserveStatus` and writes an activity-log entry — depends on T003, T017

### Api (`OpenFarm.Api`)

- [X] T019 Add `AddSignalR()` and implement `FarmHub` (tenant-scoped group-by-org join on connect, push-only, per `contracts/farm-hub.md`) in `api/src/OpenFarm.Api/Hubs/FarmHub.cs`, and map it via `MapHub<FarmHub>("/hubs/farm")` in `api/src/OpenFarm.Api/Program.cs`
- [X] T020 Register DI in `api/src/OpenFarm.Api/Program.cs`: `IPrinterRepository → PrinterRepository`, `IPrinterConnector → FakePrinterConnector` (dev/test default; swapped for `BambuLanConnector` per-printer once T027 lands), and `PrinterConnectionBackgroundService` as a hosted service — depends on T015, T013, T017

**Note**: `IPrinterEventPublisher` (Application) + `FarmHubPrinterEventPublisher` (Api, SignalR-backed) were added in this phase, ahead of Phase 3, so `PrinterConnectionBackgroundService` already emits `PrinterStatusChanged`/`PrinterTelemetryUpdated`/`CameraFrameReceived` from day one — T034 in Phase 3 is effectively already satisfied by this wiring; that task is marked done there with a pointer back here.

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 — Operator adds a printer and watches its live status (Priority: P1) 🎯 MVP

**Goal**: Register a printer with verify-before-add gating; it appears in the fleet list and its `Idle`/`Printing`/`AwaitingClear` status updates live via SignalR with no refresh. Includes FR-002's view/edit/remove lifecycle, which has no separate Acceptance Scenario of its own.

**Independent Test** (spec.md): Add one physical (or simulated) printer, change its state (e.g. start a print from the printer itself), confirm the fleet list reflects the new status without any manual refresh.

### Backend

- [X] T021 [P] [US1] Write `RegisterPrinterCommandValidatorTests` in `api/tests/OpenFarm.Application.Tests/Printers/RegisterPrinterCommandValidatorTests.cs` — `Name`/`IpAddress`/`SerialNumber`/`Brand`/`ModelCode` required, unknown `(Brand, ModelCode)` rejected
- [X] T022 [US1] Implement `RegisterPrinterCommand`, `RegisterPrinterCommandHandler`, `RegisterPrinterCommandValidator` in `api/src/OpenFarm.Application/Printers/RegisterPrinterCommand.cs` (+`Handler.cs`, `Validator.cs`) — calls `IPrinterConnector.VerifyAsync` server-side before persisting (FR-001a, never trusting only the client's earlier verify call), rejects a duplicate `SerialNumber` within the org with a clear error naming the conflicting printer (FR-001b) — depends on T010, T012, T021
- [X] T023 [P] [US1] Implement `EditPrinterConnectionCommand`, `Handler`, `Validator` in `api/src/OpenFarm.Application/Printers/EditPrinterConnectionCommand.cs` (+`Handler.cs`, `Validator.cs`) — same verify-before-save gate as registration (FR-002) — depends on T010, T012
- [X] T024 [P] [US1] Implement `RemovePrinterCommand`, `Handler` in `api/src/OpenFarm.Application/Printers/RemovePrinterCommand.cs` (+`Handler.cs`) — no status guard; removal is always permitted regardless of current `Status` (FR-002) — depends on T010, T012
- [X] T025 [P] [US1] Implement `GetFleetQuery`, `Handler` in `api/src/OpenFarm.Application/Printers/GetFleetQuery.cs` (+`Handler.cs`) — depends on T012
- [X] T026 [US1] Implement `VerifyPrinterConnectionEndpoint` (REPR, no persistence) in `api/src/OpenFarm.Api/Endpoints/Printers/VerifyPrinterConnectionEndpoint.cs` per `contracts/printers-api.md`'s `POST /api/v1/printers/verify-connection` — depends on T011
- [X] T027 [US1] Implement `RegisterPrinterEndpoint` in `api/src/OpenFarm.Api/Endpoints/Printers/RegisterPrinterEndpoint.cs` per `contracts/printers-api.md`'s `POST /api/v1/printers` (implements `IActivityLoggable`, 409 on duplicate serial, 422 on failed verification) — depends on T022
- [X] T028 [P] [US1] Implement `EditPrinterConnectionEndpoint` in `api/src/OpenFarm.Api/Endpoints/Printers/EditPrinterConnectionEndpoint.cs` per `contracts/printers-api.md`'s `PUT /api/v1/printers/{id}/connection` — depends on T023
- [X] T029 [P] [US1] Implement `RemovePrinterEndpoint` in `api/src/OpenFarm.Api/Endpoints/Printers/RemovePrinterEndpoint.cs` per `contracts/printers-api.md`'s `DELETE /api/v1/printers/{id}` — depends on T024
- [X] T030 [US1] Implement `ListFleetEndpoint` in `api/src/OpenFarm.Api/Endpoints/Printers/ListFleetEndpoint.cs` per `contracts/printers-api.md`'s `GET /api/v1/printers` — depends on T025
- [X] T031 [US1] Implement the real `BambuLanConnector` (MQTTnet: connect/subscribe `device/{serial}/report`/publish `device/{serial}/request`, `GET_VERSION`+`PUSH_ALL` on connect, custom TLS validation trusting Bambu's bundled per-device certs, per `research.md` §1) and `BambuMqttMessageParser`'s status parsing (`gcode_state` → `PrinterStatus` per `research.md` §2) in `api/src/OpenFarm.Infrastructure/Printers/Bambu/BambuLanConnector.cs`, `BambuMqttMessageParser.cs` — depends on T011, T017, fixtures from T004
- [X] T032 [P] [US1] Write `BambuLanConnectorTests` and `BambuMqttMessageParserTests` (fixture-replay against T004's captured JSON, test-after per constitution) in `api/tests/OpenFarm.Infrastructure.Tests/Printers/Bambu/` — depends on T003, T031
- [X] T033 [US1] Wire `BambuLanConnector` as the per-printer `IPrinterConnector` implementation used by `PrinterConnectionBackgroundService` for real registered printers (keep `FakePrinterConnector` as the dev/test default) in `api/src/OpenFarm.Api/Program.cs` / the connector-resolution point inside `PrinterConnectionBackgroundService` — depends on T031
- [X] T034 [US1] Emit `PrinterStatusChanged` from `FarmHub` whenever `PrinterConnectionBackgroundService` flips `Status` or `IsReachable`, per `contracts/farm-hub.md` — depends on T017, T019 (done ahead of schedule in Phase 2, see the note under T020)
- [X] T035 [P] [US1] Write an `api/tests/OpenFarm.Api.IntegrationTests/Printers/RegisterPrinterEndpointTests.cs` covering: valid registration succeeds (201), duplicate serial rejected (409) with duplicate IP permitted, unverifiable connection rejected (422) — depends on T027

### Frontend

- [X] T036 [P] [US1] Remap `web/src/components/printer-tile/status.ts`'s `PrinterStatus` union and `STATUS_META` from `printing/attention/complete/idle/offline` to `Idle`/`Printing`/`AwaitingClear`, and update `web/src/components/printer-tile/PrinterTile.tsx`'s props to take `isReachable` as a separate prop rather than an `offline` status value (per plan.md's Constitution Check note)
- [X] T037 [US1] Export `api/openapi.json` from the running API's Swagger endpoint and run `bunx orval` in `web/` to generate typed hooks for the register/verify/edit/remove/list-fleet endpoints into `web/src/lib/api/generated/` — depends on T026, T027, T028, T029, T030
- [X] T038 [P] [US1] Implement `AddPrinterDialog.tsx` in `web/src/features/printers/components/` (react-hook-form + zod; calls the verify-connection endpoint on field blur; submit stays disabled until verified, per FR-001a) — depends on T037
- [X] T039 [P] [US1] Implement `EditPrinterDialog.tsx` in `web/src/features/printers/components/` (same verify-before-save gating as `AddPrinterDialog`, per FR-002) — depends on T037
- [X] T040 [P] [US1] Implement `useFarmHub.ts` in `web/src/features/printers/hooks/` — opens the SignalR connection to `/hubs/farm`, writes `PrinterStatusChanged` payloads into TanStack Query's cache via `setQueryData` for the fleet-list query — depends on T037
- [X] T041 [US1] Replace the `PrintersPage.tsx` `ComingSoonPage` stub with the real fleet list (fetches via the Orval-generated fleet query, renders `PrinterTile` per printer, mounts `AddPrinterDialog`/`EditPrinterDialog`, a remove-confirmation action per printer, subscribes via `useFarmHub`) in `web/src/features/printers/PrintersPage.tsx`, updating `PrintersPage.test.tsx` accordingly — depends on T036, T038, T039, T040

**Checkpoint**: US1 fully functional and independently testable — SC-001, SC-002, SC-007 and `quickstart.md` Scenarios 1–3 all verifiable.

---

## Phase 4: User Story 2 — Operator monitors live telemetry and AMS slot contents (Priority: P1)

**Goal**: Printer detail page shows live nozzle/bed temperature, print progress, and AMS slot contents, updating without a refresh.

**Independent Test** (spec.md): Open a printer's detail page during an active print, confirm temperature/progress/AMS slot contents change on-screen as the physical print progresses.

- [X] T042 [US2] Extend `BambuMqttMessageParser` to also parse telemetry (`NozzleTempC`, `BedTempC`, `PrintProgressPercent`, `stg_cur` → `CurrentStageLabel`) and AMS slot fields into the `data-model.md` AMS JSONB shape (`research.md` §3) — depends on T031
- [X] T043 [P] [US2] Extend `BambuMqttMessageParserTests` with telemetry/AMS-parsing fixture cases — depends on T032, T042
- [X] T044 [US2] Update `PrinterConnectionBackgroundService` to persist parsed telemetry/AMS onto the `Printer`'s inline columns/`AmsUnits` and emit `PrinterTelemetryUpdated` via `FarmHub`, per `contracts/farm-hub.md` — depends on T017, T042
- [X] T045 [P] [US2] Implement `GetPrinterDetailQuery`, `Handler` in `api/src/OpenFarm.Application/Printers/GetPrinterDetailQuery.cs` (+`Handler.cs`) — depends on T012
- [X] T046 [US2] Implement `GetPrinterDetailEndpoint` in `api/src/OpenFarm.Api/Endpoints/Printers/GetPrinterDetailEndpoint.cs` per `contracts/printers-api.md`'s `GET /api/v1/printers/{id}` — depends on T045
- [X] T047 [US2] Regenerate the Orval client (`bunx orval` in `web/`, after refreshing `api/openapi.json`) for the new detail endpoint — depends on T046
- [X] T048 [P] [US2] Implement `TelemetryPanel.tsx` and `AmsSlotGrid.tsx` in `web/src/features/printers/components/` — depends on T047
- [X] T049 [US2] Implement `PrinterDetailPage.tsx` in `web/src/features/printers/` (fetches the detail query, renders `TelemetryPanel`/`AmsSlotGrid`, subscribes to `PrinterTelemetryUpdated` via `useFarmHub`) and register the `/printers/:id` route in `web/src/app/routes.tsx` — depends on T040, T048

**Checkpoint**: US1 and US2 both work independently — SC-002 verifiable for telemetry, `quickstart.md`'s telemetry scenario passes.

---

## Phase 5: User Story 3 — Operator views a live camera feed (Priority: P2)

**Goal**: Live camera view on the printer detail page; graceful "unavailable" degradation when the feed can't be shown.

**Independent Test** (spec.md): Open a printer's detail page and confirm a live video stream renders and updates.

- [X] T050 [US3] Implement `BambuCameraRelay`'s P1/A1 `CAMERA_IMAGE` path in `api/src/OpenFarm.Infrastructure/Printers/Bambu/BambuCameraRelay.cs` — `TcpClient`/`SslStream` reader on port 6000 mirroring `ChamberImageThread`'s exact framing (80-byte auth blob, 16-byte frame header, JPEG magic-byte validation, per `research.md` §4), selecting transport via `Printer.Model`'s `PrinterModelCatalog.CameraTransport` lookup rather than hardcoding — depends on T006, T031. Byte-framing extracted into `BambuCameraFrameCodec.cs` so it's fixture-replay testable without a socket; TLS trust logic shared with `BambuLanConnector` via a new `BambuCertificateTrust.cs`.
- [X] T051 [P] [US3] Write `BambuCameraRelayTests` (fixture-replay against captured frame bytes) in `api/tests/OpenFarm.Infrastructure.Tests/Printers/Bambu/BambuCameraRelayTests.cs` — depends on T003, T050. Implemented as `BambuCameraFrameCodecTests.cs` against `BambuCameraFrameCodec` (the pure, socket-free framing logic) — matches this codebase's existing parser-vs-connector test split (`BambuMqttMessageParserTests` vs. untested `BambuLanConnector` socket code). Frame fixture is hand-constructed synthetic bytes (`Fixtures/sample-camera-frame.jpg`) since pybambu's corpus has no binary camera samples — see `Fixtures/README.md`.
- [ ] T052 [US3] (Optional fast-follow, X1-family only, per plan.md's Complexity Tracking) Implement the FFmpeg-subprocess-based RTSP-to-JPEG relay for `CAMERA_RTSP` models in `BambuCameraRelay.cs`, feeding the same `CameraFrameReceived` contract — depends on T050. **Deliberately skipped this pass** per the approved implementation plan's scope decision (CAMERA_IMAGE only) — X1-family printers show the graceful "unavailable" state until this fast-follow lands, which FR-009 already requires as a valid state.
- [X] T053 [US3] Wire `BambuCameraRelay` into `PrinterConnectionBackgroundService` to emit `CameraFrameReceived` via `FarmHub`, per `contracts/farm-hub.md` — depends on T017, T050. Runs as a second concurrent loop (`RunCameraLoopAsync`) alongside the MQTT loop, in its own DI scope/tracked `Printer` instance to avoid two loops sharing one `DbContext`; gated on `PrinterModelCatalog.TryResolve(...).CameraTransport == CameraTransport.BinaryImage`.
- [X] T054 [P] [US3] Implement `CameraView.tsx` in `web/src/features/printers/components/` — subscribes to `CameraFrameReceived` via `useFarmHub`, shows an "unavailable" state after a short freshness-timeout with no recent frame (FR-009) — depends on T040
- [X] T055 [US3] Mount `CameraView.tsx` on `PrinterDetailPage.tsx` — depends on T049, T054

**Checkpoint**: US1–US3 all independently functional — SC-005 and `quickstart.md`'s camera scenario verifiable (at minimum for P1/A1 models; X1 RTSP depends on T052).

---

## Phase 6: User Story 4 — Operator clears a finished printer and every change is recorded (Priority: P2)

**Goal**: "Mark ready" moves `AwaitingClear → Idle`; every manual and connector-reported transition is recorded in the activity history.

**Independent Test** (spec.md): Complete a print, confirm `Awaiting Clear`, perform "Mark ready", confirm `Idle`, confirm both transitions appear in the activity history.

- [X] T056 [P] [US4] Write `MarkPrinterReadyCommandHandlerTests` in `api/tests/OpenFarm.Application.Tests/Printers/MarkPrinterReadyCommandHandlerTests.cs` — succeeds from `AwaitingClear`, rejects from `Idle`/`Printing` surfacing `InvalidPrinterStatusTransitionException` as a clear error (FR-003, SC-004)
- [X] T057 [US4] Implement `MarkPrinterReadyCommand`, `Handler` in `api/src/OpenFarm.Application/Printers/MarkPrinterReadyCommand.cs` (+`Handler.cs`) — calls `Printer.TransitionTo` — depends on T010, T012, T056
- [X] T058 [US4] Implement `MarkPrinterReadyEndpoint` in `api/src/OpenFarm.Api/Endpoints/Printers/MarkPrinterReadyEndpoint.cs` per `contracts/printers-api.md`'s `POST /api/v1/printers/{id}/mark-ready` (`IActivityLoggable`, 409 on illegal transition) — depends on T057
- [X] T059 [P] [US4] Write `MarkPrinterReadyEndpointTests` (integration) in `api/tests/OpenFarm.Api.IntegrationTests/Printers/MarkPrinterReadyEndpointTests.cs` confirming a successful mark-ready call produces an `ActivityLogEntries` row (SC-003) — depends on T058
- [X] T060 [US4] Regenerate the Orval client (`bunx orval`) for the mark-ready endpoint — depends on T058
- [X] T061 [P] [US4] Add a "Mark ready" action to `PrinterTile.tsx`/`PrinterDetailPage.tsx`, shown only when `Status === AwaitingClear` — depends on T041, T049, T060. Live-verified end-to-end (screenshot + activity-log API check); this also surfaced and fixed a real bug in `web/src/lib/api/httpClient.ts` — bodyless POST/PUT/PATCH mutations (no request-body fields left after Orval excludes route-bound/read-only ones) never sent a `Content-Type` header, and ASP.NET Core's minimal-API body binding rejected them with 415. The mutator now defaults such requests to a `{}` JSON body.
- [X] T062 [US4] Extend `PrinterConnectionBackgroundServiceTests` (T018) to assert a connector-reported reconciliation also produces an `ActivityLogEntries` row (the implementation already exists from T017; this closes out SC-003's "100% of printer status changes... appear in the activity history" for the connector-reported half) — depends on T018. Test already existed (`ConnectorReportedStatusChange_ReconcilesAndWritesActivityLogEntry`); updated its DI setup to register `IPrinterCameraRelay` since Phase 5's concurrent camera loop now also starts for its `P1S` test printer.

**Checkpoint**: US1–US4 independently functional — SC-003, SC-004 verifiable end-to-end.

---

## Phase 7: User Story 5 — Operator distinguishes "offline" from print status (Priority: P3)

**Goal**: Fleet list and detail page clearly separate `IsReachable` from `Status` — never conflated into one value.

**Independent Test** (spec.md): Disconnect a printer from the network, confirm the UI shows it as unreachable/offline while its last-known print status remains visible and unchanged.

- [X] T063 [P] [US5] Update `PrinterTile.tsx` to render an explicit offline/unreachable indicator distinct from the `Idle`/`Printing`/`AwaitingClear` status badge (the underlying `IsReachable` data is already pushed via `PrinterStatusChanged` since T034) — depends on T036, T041. Already implemented in Phase 3 (`OFFLINE_META` rendered separately from `STATUS_META`); no change needed this phase.
- [X] T064 [US5] Update `PrinterDetailPage.tsx` to show the same reachability indicator distinct from status — depends on T049, T063. Already implemented in Phase 4; live-verified repeatedly across Phases 4-6's screenshots (e.g. "Offline" + "Awaiting Clear" shown together).
- [X] T065 [P] [US5] Add a `PrintersPage.test.tsx` case asserting the fleet list shows `Idle` status and an "offline" indicator simultaneously (not conflated) when `IsReachable === false` and `Status === Idle` — depends on T063. Already present (`shows an offline indicator distinct from status when unreachable`); confirmed passing.

**Checkpoint**: All 5 user stories independently functional and testable.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T066 [P] Run `dotnet format api/OpenFarm.slnx --verify-no-changes` and `bun run lint`/`bun run format` in `web/`, fixing any drift
- [ ] T067 [P] Run `dotnet list api/OpenFarm.slnx package --vulnerable` to confirm the new MQTTnet dependency introduces no known vulnerabilities (constitution CI gate)
- [ ] T068 Walk through `quickstart.md` end-to-end against a running local stack (`infra && docker compose up`), confirming every scenario (verify-gating, register/list, live status, telemetry/AMS, camera, mark-ready) passes as written; update `quickstart.md` if any step drifted during implementation
- [ ] T069 Re-verify `plan.md`'s Constitution Check items against the final implementation (Clean Architecture boundary, SignalR-only real-time with no polling fallback anywhere, activity-log coverage for both HTTP and connector-reported transitions, Finbuckle tenant isolation on `Printers`) and note any drift

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately. T004 (hardware spike) can trail behind T001–T003 if physical/simulated hardware isn't immediately available.
- **Foundational (Phase 2)**: Depends on Setup (T002, T003) — BLOCKS all user stories. T004's fixtures are only needed once a user story reaches `BambuLanConnector` work (T031), not before.
- **User Stories (Phase 3+)**: All depend on Foundational completion.
  - US1 (Phase 3) has no dependency on other stories — it's the MVP.
  - US2 (Phase 4) depends on US1's `BambuLanConnector`/`BambuMqttMessageParser` (T031) and `useFarmHub`/`PrinterDetailPage` scaffolding (T040) existing, but is independently testable once built.
  - US3 (Phase 5) depends on US1's `BambuLanConnector` (T031) and US2's `PrinterDetailPage.tsx` (T049) as a mount point.
  - US4 (Phase 6) depends on US1's fleet UI (T041) and detail page (T049) as mount points for the "Mark ready" action, and Foundational's state machine (T008).
  - US5 (Phase 7) depends on US1's `PrinterTile`/fleet list (T041) and US2's detail page (T049) as the surfaces it adds an indicator to.
- **Polish (Phase 8)**: Depends on all desired user stories being complete.

### Within Each User Story

- Domain/Application tests (where TDD applies) before their implementation.
- Models/commands before endpoints; endpoints before Orval regeneration; Orval regeneration before the frontend components that consume the generated client.
- Backend real-time wiring (`FarmHub` events) before the frontend hook that subscribes to them.

### Parallel Opportunities

- All Setup tasks marked `[P]` (T002, T003) can run in parallel; T004 can run in parallel with T002/T003 if hardware is available.
- Foundational `[P]` tasks: Domain test-writing tasks (T005, T007, T009) can be written in parallel since they touch different files; `IPrinterConnector`/`IPrinterRepository` (T011, T012) are independent; `FakePrinterConnector` (T013) and `PrinterConnectionBackgroundServiceTests` (T018) are independent of the EF persistence chain (T014–T016).
- Within US1: T021, T023, T024, T025 (Application layer) can be written in parallel; T028, T029 (endpoints) can be built in parallel once their commands exist; T038, T039, T040 (frontend) can be built in parallel once T037's Orval regen lands.
- Different user stories' backend work (once each story's own upstream dependency, e.g. T031, is done) can proceed in parallel by different developers, per the Dependencies notes above.

---

## Parallel Example: User Story 1

```bash
# Application-layer commands/queries, once Foundational is done:
Task: "Write RegisterPrinterCommandValidatorTests in api/tests/OpenFarm.Application.Tests/Printers/RegisterPrinterCommandValidatorTests.cs"
Task: "Implement EditPrinterConnectionCommand, Handler, Validator in api/src/OpenFarm.Application/Printers/EditPrinterConnectionCommand.cs"
Task: "Implement RemovePrinterCommand, Handler in api/src/OpenFarm.Application/Printers/RemovePrinterCommand.cs"
Task: "Implement GetFleetQuery, Handler in api/src/OpenFarm.Application/Printers/GetFleetQuery.cs"

# Frontend components, once T037's Orval regen lands:
Task: "Implement AddPrinterDialog.tsx in web/src/features/printers/components/"
Task: "Implement EditPrinterDialog.tsx in web/src/features/printers/components/"
Task: "Implement useFarmHub.ts in web/src/features/printers/hooks/"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run `quickstart.md` Scenarios 1–3 against `FakePrinterConnector` (or a real printer if T031/T033 are done)
5. Deploy/demo if ready — this alone delivers "register a printer, see it in the fleet list, watch its status change live," which is the spec's stated foundational value

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. US1 → test independently → deploy/demo (MVP)
3. US2 → test independently → deploy/demo (adds live telemetry/AMS)
4. US3 → test independently → deploy/demo (adds camera)
5. US4 → test independently → deploy/demo (adds Mark Ready + full activity-history coverage)
6. US5 → test independently → deploy/demo (adds explicit offline/status separation)
7. Polish

### Suggested MVP Scope

**User Story 1 alone** (Phases 1–3, ~41 tasks) is the suggested MVP — it is the only P1 story that is fully self-contained (US2 is also P1 but depends on US1's connector work).

---

## Notes

- `[P]` tasks touch different files with no unmet dependency.
- `[Story]` labels map every user-story-phase task to spec.md's US1–US5 for traceability.
- `BambuLanConnector`/`BambuCameraRelay` tests are fixture-replay (test-after), never TDD against a live printer, per the constitution's named exemption — only Domain state-machine/catalog tests are RED-first.
- Commit after each task or logical group; stop at any Checkpoint to validate a story independently before continuing.
- Avoid: vague tasks, same-file conflicts marked `[P]`, cross-story dependencies that would break independent testability beyond what's documented above.
