# Implementation Plan: Phase 1 — Fleet Management + Live Bambu Integration

**Branch**: `002-printer-integration` | **Date**: 2026-07-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-printer-integration/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Register Bambu Lab printers with verify-before-save connection gating, track each printer's status through an explicit `Idle`/`Printing`/`AwaitingClear` domain state machine (strict manual transitions, lenient connector-reconciled ones), and push live status/telemetry/AMS-slot/camera data into a fleet list and printer detail page via SignalR — no polling, no print-job dispatch this phase. A real `BambuLanConnector` (MQTTnet over TLS, port 8883) implements the `IPrinterConnector` contract already anticipated by the architecture, built from fixtures captured during a throwaway hardware spike and developed against a `FakePrinterConnector` first. Protocol reference material (there is no official Bambu API) is vendored as a git submodule at `third_party/printers/bambulab/`, itself wrapping a nested submodule (`references/ha-bambulab`, the `pybambu` reverse-engineered client) that must be cloned recursively — done as the first step of this plan — so protocol claims in `research.md` cite real source, not only the curated summary docs.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`api/`), TypeScript 5 / React 19 (`web/`) — unchanged from Phase 0.

**Primary Dependencies**: MQTTnet (new, `OpenFarm.Infrastructure`) for Bambu LAN MQTT-over-TLS (port 8883, topic `device/<serial>/report`) — already the named intent in `docs/FEATURE_PLAN.md`, confirmed rather than re-litigated in `research.md`. Camera transport (binary chamber-image push vs. RTSP, and how it's relayed to the browser) is resolved in `research.md` from the now-recursively-cloned `pybambu` source. `@microsoft/signalr` (frontend) is an existing unused dependency, now wired up for real.

**Storage**: Existing Postgres via `OpenFarmDbContext`. New `Printers` table (org id, name, brand + model code, ip, serial, status, reachability/last-seen, inline telemetry + AMS-slot JSONB — not historical, per spec's Key Entities). `Model` is **not** free text — it's validated at the Application layer against a `PrinterModel` value object backed by a small in-code catalog (`PrinterModelCatalog`), so the connector can derive protocol-relevant static facts (e.g. camera transport) from `(Brand, ModelCode)` instead of trusting an arbitrary string; only `Brand`/`ModelCode` are persisted, `PrinterModel`'s derived facts are resolved at read/connect time, not stored redundantly (see `data-model.md`). This stays a compile-time catalog, not a full extracted `printer_profiles` table — that extraction is still deliberately deferred to Phase 4 per spec Assumptions and the constitution's anti-speculation principle; today's catalog only needs to answer "what does this Bambu model support," not become a self-service, operator-editable schema. `ConnectionSecret` (`api/src/OpenFarm.Domain/ConnectionSecrets/ConnectionSecret.cs`) stays keyed by `(organizationId, printerSerialNumber)` as a natural-key lookup rather than gaining a new FK column — `Printer.SerialNumber` is already unique per org (FR-001b), so this is zero-change to the Phase 0 schema. Status *history* reuses the existing `ActivityLogEntries` table as-is.

**Testing**: TDD (xUnit + FluentAssertions) for the `PrinterStatus` state machine in `OpenFarm.Domain.Tests` — the one deterministic piece this phase adds. Test-after/fixture-replay for `BambuLanConnector` against JSON captured during the M0.1 hardware spike, per the constitution's named exemption. `WebApplicationFactory` + Testcontainers.PostgreSql for API integration tests. Vitest + RTL for `web/src/features/printers`. Physical-hardware validation stays manual, never a CI gate.

**Target Platform**: Linux containers (Docker Compose), ASP.NET Core host, Vite-built SPA — unchanged from Phase 0.

**Project Type**: Web application — existing `api/` + `web/` monorepo, extended in place, no new project.

**Performance Goals**: SC-002 ≤5s status/telemetry propagation (MQTT message → parse → SignalR push → cache update); SC-005 ≤3s camera visibility from opening the detail page; SC-006 fleet list stays responsive with ≥20 concurrently connected printers.

**Constraints**: FR-005's ~30s unreachable timeout (last-message-timestamp + periodic sweep, not a single fixed timer race); SC-006 rules out a blocking-thread-per-connection design — connections must be async I/O so 20 concurrent MQTT + camera streams don't saturate the host's thread pool.

**Scale/Scope**: Single self-hosted organization, up to ~20 printers, Bambu Lab X1/P1/A1-series only this phase (per spec Assumptions) — architecture must not block a second brand later (FR-012).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture**: `IPrinterConnector` (Application-defined contract: connect/verify/telemetry+status+camera event stream) is implemented by `BambuLanConnector` in `Infrastructure`; `Domain`/`Application` never reference MQTTnet directly. The connector interface is designed around domain-shaped events (parsed telemetry/status DTOs), not raw MQTT JSON. **Pass.**
- **II. Test Discipline**: `PrinterStatus` transition logic (`TransitionTo` strict / `ObserveStatus` lenient) gets TDD in `OpenFarm.Domain.Tests`. `BambuLanConnector` is test-after/fixture-replay per the constitution's named exemption — sequencing follows `docs/FEATURE_PLAN.md`'s M0.1 (spike, capture fixtures) → M0.5 (real connector, test against fixtures) order, never TDD against a live printer. **Pass.**
- **III. Consistent UX**: Fleet list and printer detail page both subscribe to the same `/hubs/farm` events for the same printer — no separate polling path once SignalR wiring exists (per constitution, `docs/FEATURE_PLAN.md`'s M0.4 "plain TanStack Query polling, no SignalR yet" is scaffolding fully replaced by M0.6, not a fallback shipped in the final PR). Idle/Printing/AwaitingClear + separate reachability render through one shared label/action mapping. **Note**: the existing `web/src/components/printer-tile/status.ts` union (`printing/attention/complete/idle/offline`) is Phase-0 scaffold UI that does **not** match the Domain's `Idle/Printing/AwaitingClear` + reachability split — this mapping is designed fresh in this phase's `data-model.md`/UI work, not reused as-is. **Pass, with explicit remapping.**
- **IV. Build for Expansion**: `third_party/printers/bambulab/` + `IPrinterConnector` are this principle in practice — a second brand gets its own submodule/docs subtree and connector implementation without touching `Printer`/`PrinterStatus`. `organization_id` present on `Printers` from the first migration. **Pass.**
- **V. Performance/Real-Time**: `PrinterConnectionBackgroundService` manages MQTT connection lifecycle per the constitution's explicit example ("Bambu LAN connections... run as BackgroundServices inside the API host"), async I/O per connection (not blocking threads) to meet SC-006. No polling fallback once SignalR is wired. Fleet list itself is small (≤20 printers) so TanStack Virtual isn't required here — only the job queue is called out for unbounded growth (Phase 9), confirmed as a non-violation. **Pass.**
- **Security & Multi-Tenancy**: `ConnectionSecret` reuses the existing `IDataProtector`-encrypted pattern via natural-key lookup (see Storage above) rather than inventing a second secret mechanism. Finbuckle's global query filter covers `Printers` the same way it covers `ConnectionSecrets`. **Activity-log coverage — real design decision, not an oversight**: HTTP-triggered transitions (Register/Edit/Remove/Mark Ready) go through the existing `ActivityLogPostProcessor`, which is bound to `IPostProcessorContext`/`HttpContext` (`api/src/OpenFarm.Api/Processors/ActivityLogPostProcessor.cs`) and only fires on HTTP requests. Connector-reported reconciliation happens inside `PrinterConnectionBackgroundService`, not an HTTP request, so it calls `IActivityLogWriter.WriteAsync` directly with `ActorType.System`/`actorId: null` (an already-supported case) to satisfy FR-010/SC-003's 100%-logged requirement. **Pass, with explicit non-HTTP logging path.**

No Constitution Check violations. One new external dependency (MQTTnet) is recorded in Complexity Tracking below for visibility, not because it's a violation.

**Post-Phase-1-design re-check** (per `research.md`/`data-model.md`/`contracts/`): confirms all of the above with no new violations, plus one new item surfaced by research that Complexity Tracking now records — X1-family camera support (`CAMERA_RTSP`) needs an FFmpeg subprocess per active view, since `pybambu` itself has no RTSP client and relies on FFmpeg/GStreamer via Home Assistant's platform (research.md §4). P1/A1's `CAMERA_IMAGE` binary-JPEG path needs no such dependency and alone satisfies SC-005 for at least one printer family, so FFmpeg is scoped as an optional fast-follow within this phase, not a hard blocker. `IPrinterConnector`, the `PrinterStatus` transition table, and the `ConnectionSecret` natural-key reuse all held up unchanged through Phase 1 design — no rework of the Constitution Check's Phase 0 conclusions was needed.

**Post-implementation re-check** (tasks.md T069, all 69 tasks complete except the deliberately-skipped
X1 RTSP fast-follow T052): every item above holds against the actual code. `IPrinterCameraRelay`
(camera's own contract, mirroring `IPrinterConnector`'s split) keeps Domain/Application free of any
`TcpClient`/`SslStream`/MQTTnet reference — verified by grep, not just design intent. TLS trust logic
was deduplicated into `BambuCertificateTrust.cs`, shared by both the MQTT connector and the camera
relay, rather than copy-pasted (Principle IV in miniature). `CameraView.tsx` joined the same
`useFarmHub`-owned `/hubs/farm` connection and the same TanStack-Query-cache-as-real-time-store
pattern as status/telemetry — no second SignalR connection, no polling (Principle III/V). Activity-log
coverage was live-verified end-to-end this phase (not just unit-tested): a "Mark ready" click produced
a `PrinterMarkedReady` row with `actorType: "Operator"` via the HTTP post-processor path, and the
existing `ConnectorReportedStatusChange_ReconcilesAndWritesActivityLogEntry` test covers the
non-HTTP `ActorType.System` path — both halves of FR-010/SC-003's 100%-logged requirement confirmed,
not assumed. One real bug was found and fixed via this live verification, outside any Constitution
Check item but worth recording: `PrinterConnectionBackgroundService.ExecuteAsync` let
`PeriodicTimer.WaitForNextTickAsync`'s cancellation exception escape uncaught, which combined with
the default `BackgroundServiceExceptionBehavior.StopHost` turned every graceful shutdown into a fatal
host crash — fixed by catching the expected `OperationCanceledException`. No Constitution Check
violations found. No new external dependencies were added beyond MQTTnet (the camera relay reuses
`System.Net.Security.SslStream` from the BCL, not a new package).

## Project Structure

### Source Code (repository root)

```text
api/src/OpenFarm.Domain/Printers/
  Printer.cs                          # entity: name, brand+modelCode, ip, serial, ConnectionSecret lookup key, status, reachability, inline telemetry/AMS
  PrinterStatus.cs                    # enum: Idle, Printing, AwaitingClear
  PrinterStatusTransitions.cs         # TransitionTo (strict, throws) / ObserveStatus (lenient, reconciles)
  InvalidPrinterStatusTransitionException.cs
  PrinterModel.cs                     # value object: Brand, ModelCode, CameraTransport
  PrinterModelCatalog.cs              # compile-time (Brand, ModelCode) -> PrinterModel lookup; unrecognized combo -> validation failure
  UnknownPrinterModelException.cs

api/src/OpenFarm.Application/Printers/
  RegisterPrinterCommand.cs / Handler / Validator     # includes verify-before-add call to IPrinterConnector
  EditPrinterConnectionCommand.cs / Handler / Validator
  RemovePrinterCommand.cs / Handler
  MarkPrinterReadyCommand.cs / Handler                # AwaitingClear -> Idle via TransitionTo
  GetFleetQuery.cs / Handler
  GetPrinterDetailQuery.cs / Handler
  IPrinterConnector.cs                # Connect/VerifyAsync/telemetry+status+camera event contract
  IPrinterRepository.cs

api/src/OpenFarm.Infrastructure/Printers/
  Bambu/
    BambuLanConnector.cs              # MQTTnet-based, implements IPrinterConnector
    BambuMqttMessageParser.cs         # raw JSON -> domain telemetry/status/AMS DTOs
    BambuCameraRelay.cs               # transport (RTSP port 322 vs binary-image port 6000) selected via Printer.Model's PrinterModelCatalog lookup, not hardcoded per printer
  FakePrinterConnector.cs             # test double + dev "simulated printer" fixture
  PrinterConnectionBackgroundService.cs   # per-printer connection lifecycle + reachability sweep
  PrinterRepository.cs
  Persistence/Configurations/PrinterConfiguration.cs
  Persistence/Migrations/<timestamp>_AddPrinters.cs   # single migration, per constitution's one-per-PR rule

api/src/OpenFarm.Api/
  Endpoints/Printers/
    RegisterPrinterEndpoint.cs
    VerifyPrinterConnectionEndpoint.cs   # pre-flight verify the add/edit form calls before enabling submit
    EditPrinterConnectionEndpoint.cs
    RemovePrinterEndpoint.cs
    MarkPrinterReadyEndpoint.cs
    ListFleetEndpoint.cs
    GetPrinterDetailEndpoint.cs
  Hubs/FarmHub.cs                     # /hubs/farm, tenant-scoped groups, pushes status/telemetry/camera events

web/src/features/printers/
  PrintersPage.tsx                    # replaces the current placeholder stub
  PrinterDetailPage.tsx
  components/AddPrinterDialog.tsx / EditPrinterDialog.tsx   # react-hook-form + zod, verify-before-save gates submit
  components/TelemetryPanel.tsx, AmsSlotGrid.tsx, CameraView.tsx
  hooks/useFarmHub.ts                 # SignalR connection -> TanStack Query cache wiring

web/src/components/printer-tile/status.ts   # remapped to Idle/Printing/AwaitingClear + reachability (see Constitution Check)
```

### Documentation (this feature)

```text
specs/002-printer-integration/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by this command)
```

**Structure Decision**: Existing four-project Clean Architecture backend (`OpenFarm.Domain`/`Application`/`Infrastructure`/`Api`) + single-SPA frontend (`web/`), extended in place — no new top-level project. `third_party/printers/bambulab/` stays the reference root; a hypothetical `third_party/printers/<brand2>/` sibling later is exactly what FR-012 protects.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New MQTTnet dependency in `Infrastructure` | Bambu LAN protocol is MQTT-over-TLS; a real MQTT client is required for the P1 "live status" story | Hand-rolling raw TLS+MQTT framing is strictly more code/risk for no benefit; MQTTnet is the established .NET choice, matching existing prior art (`Bambu.NET`) cited in `docs/FEATURE_PLAN.md` |
| One `BackgroundService`-managed connection per registered printer (up to ~20 concurrent) | SC-006 requires ~20 concurrent live connections; constitution mandates BackgroundService hosting for this class of work | A shared polling loop would violate Principle V (no polling fallback once real-time wiring exists) and doesn't match the constitution's explicit example |
| FFmpeg subprocess per active X1-family camera view (surfaced by `research.md` §4, optional fast-follow) | X1/X1C/X1E cameras only expose an RTSPS URL; `pybambu` itself has no RTSP client and relies on FFmpeg/GStreamer via Home Assistant's platform, so relaying to the same `CameraFrameReceived` SignalR contract needs an equivalent transcode step | A native .NET RTSP-decoding library was considered and rejected as unnecessary complexity `pybambu` itself doesn't use; P1/A1's simpler `CAMERA_IMAGE` binary-JPEG path alone satisfies SC-005 for at least one printer family, so this can ship as a fast-follow within the phase rather than blocking it |
