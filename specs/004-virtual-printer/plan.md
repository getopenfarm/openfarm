# Implementation Plan: Virtual Printer (Simulated Printer for UI Testing)

**Branch**: `004-virtual-printer` | **Date**: 2026-07-17 | **Spec**: [spec.md](./spec.md)

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Re-plan note (read first)

This plan **replaces** an earlier version built and fully implemented around IP-address-based connector
selection (a printer at `192.0.2.0/24` was silently routed to the simulator). A 2026-07-17 clarification
follow-up reversed that decision: a simulated printer must now be a real, user-visible Brand ("System") /
Model ("Simulation") catalog entry, selectable from the Add-printer dialog, not inferred from an
undiscoverable IP convention. **The already-shipped implementation does not match this plan** — it needs
targeted rework, not a from-scratch build. See "What already exists vs. what changes" below.

## Summary

Add a `SimulatorPrinterConnector` (+ `SimulatorPrinterCameraRelay`) as a second `IPrinterConnector`/
`IPrinterCameraRelay` implementation alongside the existing `BambuLanConnector`/`BambuCameraRelay` — this
part is already built and unaffected by the re-plan. What changes is *how a printer opts into it*: instead
of a reserved IP range, the simulator is now reached through an ordinary Domain-catalog entry
(`PrinterBrand.System`, `PrinterModelCatalog` entry `ModelCode: "Simulation"`), selectable via a new Brand
selector in the Add-printer dialog (today's dialog has none — Brand is hardcoded to `'Bambu'`). The
connector-selection seam (`IPrinterConnectorFactory`, Infrastructure-implemented) now routes on the
printer's own Brand/ModelCode rather than parsing `IpAddress`; `PrinterConnectionDetails` gains `Brand`/
`ModelCode` fields to carry that signal through to the factory without changing the factory's own method
signatures. IP address, serial number, and access code stay required fields for a simulated printer,
identical in shape to a real one — only Brand/Model selection differs, and Brand/Model are intentionally
visible everywhere they already display today (fleet list, printer detail), dropping the earlier
"fully indistinguishable" goal in favor of "identical behavior, visibly-labeled brand." No print-job-
dispatch command exists anywhere in the product yet, so User Story 2's job-start trigger remains
reachable only from test code holding the concrete `SimulatorPrinterConnector` instance, unchanged from
the original plan. No database schema changes beyond the ordinary catalog-entry addition (which touches
no table — `PrinterModelCatalog` is a compile-time dictionary, not persisted data).

## What already exists vs. what changes

| Area | Already built (from the superseded plan) | Change needed now |
|---|---|---|
| `SimulatorPrinterConnector`, `SimulatorPrinterCameraRelay`, `SimulatedPrinterState` | Fully built and tested (Idle→Printing→AwaitingClear lifecycle, AMS fixture, camera frames) | None — behavior is unaffected by the selection-mechanism change |
| `SimulatedPrinterAddressRange` (`192.0.2.0/24` check) | Built, unit-tested | **Delete outright** (research.md §5) |
| `IPrinterConnectorFactory` / `PrinterConnectorFactory` | Built, routes on `IpAddress` | **Rework routing to Brand/ModelCode** (research.md §2); interface method signatures stay the same |
| `PrinterConnectionDetails` | `(IpAddress, SerialNumber, AccessCode)` | **Add `Brand`, `ModelCode` fields** |
| `PrinterBrand` / `PrinterModelCatalog` (Domain) | Bambu-only | **Add `System` brand + `(System, "Simulation")` catalog entry** |
| `VerifyPrinterConnectionEndpoint` / `VerifyPrinterConnectionRequest` | No Brand/ModelCode fields | **Add `Brand`, `ModelCode` to the request + validator** |
| `RegisterPrinterCommandHandler`, `EditPrinterConnectionCommandHandler`, `PrinterConnectionBackgroundService` | Build `PrinterConnectionDetails` from IP/serial/access-code only | **Also pass the already-in-scope `Brand`/`ModelCode` when constructing `details`** |
| `AddPrinterDialog.tsx` | No Brand selector; Model list is Bambu-only; Brand hardcoded `'Bambu'` | **Add a Brand selector (Bambu/System), scope the Model dropdown per brand, send the selected Brand** |
| Orval-generated API client | Matches today's `VerifyPrinterConnectionRequest` shape | **Regenerate** after the request DTO changes |
| Existing tests using `192.0.2.0/24` addresses (`PrinterConnectorFactoryTests`, `RegisterPrinterEndpointTests`, `MarkPrinterReadyEndpointTests`, `RemovePrinterEndpointTests`, `SimulatedPrintLifecycleTests`) | Pass today under IP-based routing | **Migrate to `Brand: "System", ModelCode: "Simulation"`** (any IP literal now works); the one "real printer still uses BambuLanConnector" regression test migrates to a Bambu brand/model instead of "any non-reserved IP" |

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`api/`); TypeScript / React 19 (`web/`) — this re-plan is the first
version of this feature to touch `web/` (a Brand selector is new frontend surface; the original plan was
backend-only).

**Primary Dependencies**: No new NuGet or npm packages. The simulator remains pure BCL (unchanged). The
frontend change is a `<select>` element plus a brand→model lookup table, using the existing
react-hook-form + zod + shadcn/ui stack already in place — no new frontend dependency.

**Storage**: Existing Postgres via `OpenFarmDbContext` — still no schema change. `PrinterModelCatalog` is
a compile-time dictionary, not a table; adding an entry touches no migration.

**Testing**: xUnit + FluentAssertions (Infrastructure unit tests, migrated off IP literals to Brand/Model);
xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql (integration tests, same migration). No new
frontend test framework — Vitest + React Testing Library covers the new Brand-selector interaction if a
component test is warranted (test-after, per constitution's React-component category).

**Target Platform**: Linux server (`api/`) + browser SPA (`web/`) — unchanged.

**Project Type**: Web application (existing `api/` + `web/` monorepo) — this re-plan touches both, unlike
the original backend-only plan.

**Performance Goals**: Unchanged — SC-003 (state change within ~2s via SignalR) and SC-007 (job reaches
Awaiting Clear within ~30–60s).

**Constraints**: Brand/ModelCode must be the *only* signal the connector layer uses to select the
simulator (FR-003) — no IP-address involvement at all, not even as a fallback. A real printer's connection
must remain unaffected by the selection-mechanism swap (SC-005). No new database migration. The
`IPrinterConnectorFactory`/`IPrinterConnector`/`IPrinterCameraRelay` interfaces' own method signatures
must not change (FR-011/SC-006) — only the `PrinterConnectionDetails` data record they already pass
around gains fields.

**Scale/Scope**: One Domain catalog entry (brand + model), one Application-record field addition (+ its
already-built call sites updated), one Infrastructure routing-rule rewrite, one deleted class
(`SimulatedPrinterAddressRange`), one HTTP request DTO field addition + validator rule, one frontend Brand
selector + brand-scoped Model list, one Orval regeneration, and migration of the ~5 existing test
files/classes that depended on the IP-range convention. No new API endpoints, no new DB tables, no new
frontend routes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture, Enforced by the Compiler and CI** — PASS. The Domain catalog entry
  (`PrinterBrand.System` / `PrinterModelCatalog`'s new row) is an ordinary, additive use of a Domain
  module explicitly designed for this ("adding a brand/model is a new entry here, never a rewrite" — its
  own doc comment). `PrinterConnectionDetails` gaining two fields is a passive-data-record change, not a
  new framework dependency. `IPrinterConnectorFactory`'s interface — and every `IPrinterConnector`/
  `IPrinterCameraRelay` implementation's methods — are byte-for-byte unchanged; only their existing data
  parameter carries more information callers already had in scope.
- **II. Test Discipline Where It Pays, Not Uniformly** — PASS. No new Domain state machine is introduced
  (the catalog entry is data, not logic). `PrinterConnectorFactory`'s routing-rule test and the migrated
  integration tests remain test-after Infrastructure/orchestration coverage, the same category as before.
  The frontend Brand selector is ordinary React-component surface — test-after, not TDD, per the
  constitution's own carve-out for React components/pages.
- **III. Consistent User Experience Across the Fleet** — PASS, with one explicit, deliberate exception:
  Brand/Model now visibly read "System / Simulation" for a simulated printer (2026-07-17 clarification
  drops the earlier full-indistinguishability goal in favor of this). Status semantics, available actions,
  and panel layout remain identical to a real printer's — the *behavior* consistency this principle is
  really about is untouched; only a label differs, and that label was already a visible field for every
  printer before this feature existed.
- **IV. Build for Expansion, Not Speculation** — PASS. The catalog-entry mechanism is exactly the
  designed extension point for a new brand/model — using it for the simulator is not speculative, it's
  the system's own established pattern applied to a new case. `IPrinterConnectorFactory` remains the
  minimal seam that lets two connectors coexist; nothing new is spec'd for a hypothetical third connector
  beyond what the catalog and factory already generalize to.
- **V. Performance and Real-Time Responsiveness by Construction** — PASS, unchanged. No polling
  introduced; the routing-rule change is a synchronous string comparison replacing a synchronous IP-parse
  comparison — no measurable performance delta.

No violations. Complexity Tracking table below is not needed.

**Post-Phase-1 re-check**: `data-model.md` confirms the only Domain change is the one catalog entry (no
schema/migration change); `contracts/IPrinterConnectorFactory.md` confirms `IPrinterConnectorFactory`'s
own interface is unchanged, and the one HTTP-facing change (`VerifyPrinterConnectionRequest` gaining two
fields) is a normal, additive DTO change with a corresponding Orval regeneration step — not a new
endpoint or a new architectural seam. `quickstart.md` confirms every simulated event still flows through
the existing `PrinterConnectionBackgroundService` → `IPrinterEventPublisher` → SignalR → TanStack Query
path, untouched by this re-plan. All five gates still PASS after design; no new violations were
introduced.

## Project Structure

### Documentation (this feature)

```text
specs/004-virtual-printer/
├── plan.md              # This file (/speckit-plan command output) — re-plan, supersedes prior version
├── research.md          # Phase 0 output — supersedes prior version
├── data-model.md        # Phase 1 output — supersedes prior version
├── quickstart.md         # Phase 1 output — supersedes prior version
├── contracts/           # Phase 1 output — supersedes prior version
└── tasks.md             # Phase 2 output (/speckit-tasks command) — needs regeneration against this plan
```

### Source Code (repository root)

Existing web-application layout (`api/` + `web/`), unchanged structurally. Unlike the original
(backend-only) plan, this re-plan touches `web/` as well as `api/`.

```text
api/
├── src/
│   ├── OpenFarm.Domain/
│   │   └── Printers/
│   │       ├── PrinterModel.cs                       # CHANGED — PrinterBrand gains System
│   │       └── PrinterModelCatalog.cs                # CHANGED — one new (System, Simulation) entry
│   ├── OpenFarm.Application/
│   │   └── Printers/
│   │       ├── IPrinterConnector.cs                  # CHANGED — PrinterConnectionDetails gains Brand/ModelCode
│   │       ├── IPrinterConnectorFactory.cs           # unchanged (method signatures)
│   │       ├── RegisterPrinterCommandHandler.cs      # CHANGED — pass Brand/ModelCode into PrinterConnectionDetails
│   │       └── EditPrinterConnectionCommandHandler.cs # CHANGED — same
│   ├── OpenFarm.Infrastructure/
│   │   └── Printers/
│   │       ├── Bambu/                                # unchanged
│   │       ├── Simulator/
│   │       │   ├── SimulatorPrinterConnector.cs      # unchanged
│   │       │   ├── SimulatorPrinterCameraRelay.cs    # unchanged
│   │       │   ├── SimulatedPrinterState.cs          # unchanged
│   │       │   └── SimulatedPrinterAddressRange.cs   # DELETED — superseded by catalog-based selection
│   │       ├── PrinterConnectorFactory.cs            # CHANGED — routes on Brand/ModelCode, not IP
│   │       └── PrinterConnectionBackgroundService.cs # CHANGED — pass printer.Brand/ModelCode into details
│   └── OpenFarm.Api/
│       └── Endpoints/Printers/
│           └── VerifyPrinterConnectionEndpoint.cs    # CHANGED — request gains Brand/ModelCode + validator rules
└── tests/
    ├── OpenFarm.Infrastructure.Tests/
    │   └── Printers/Simulator/
    │       └── PrinterConnectorFactoryTests.cs       # CHANGED — Brand/ModelCode cases, not IP literals
    └── OpenFarm.Api.IntegrationTests/
        └── Printers/
            ├── RegisterPrinterEndpointTests.cs        # CHANGED — Brand: System/ModelCode: Simulation
            ├── MarkPrinterReadyEndpointTests.cs        # CHANGED — same
            ├── RemovePrinterEndpointTests.cs           # CHANGED — same
            └── SimulatedPrintLifecycleTests.cs         # CHANGED — same

web/
└── src/
    ├── features/printers/components/
    │   └── AddPrinterDialog.tsx                       # CHANGED — Brand selector, brand-scoped Model list
    └── lib/api/generated/                             # REGENERATED — Orval, VerifyPrinterConnectionRequest shape
```

**Structure Decision**: Existing web-application structure (Option 2) reused as-is. Unlike the original
plan, this feature's implementation now spans both `api/` (Domain catalog entry, connection-details
shape, connector-factory routing, one HTTP DTO) and `web/` (the new Brand selector) — a direct, minimal
consequence of the 2026-07-17 clarification's decision to make the simulator user-selectable rather than
IP-inferred.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

N/A — no Constitution Check violations (see gates above, both pre- and post-Phase-1).
