# Implementation Plan: Printer Fleet Management Refinements

**Branch**: `005-printer-fleet-refinements` | **Date**: 2026-07-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-printer-fleet-refinements/spec.md`

## Summary

Four follow-up refinements to fleet management, all scoped to the existing `Printer` aggregate: (1)
replace the connection-only edit endpoint with a single unified edit covering name + IP address +
access code, with brand/model/serial number enforced immutable server-side even against a
direct-API-bypassing-the-UI request; (2) relocate Edit/Remove off the fleet table/grid rows onto the
printer detail page only; (3) expose the existing in-code `PrinterModelCatalog` through a new read
endpoint so the Add-printer dialog stops hardcoding brand/model options; (4) audit the SignalR
printer-event contract and its frontend consumer for brand-specific assumptions, generalizing only if
one is actually found. Stories 1-3 are additive/consolidating changes within the established
Clean Architecture layering; story 4 is a verification pass that is expected to conclude with no
contract changes (see research.md §6).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`api/`); TypeScript 5 / React 19 (`web/`)

**Primary Dependencies**: FastEndpoints, EF Core, Finbuckle.MultiTenant, ASP.NET Core SignalR,
`IDataProtector` (backend); React Router v7, TanStack Query, TanStack Table, Orval-generated API
client, react-hook-form + zod, shadcn/ui, `@microsoft/signalr` (frontend) — all pre-existing, no new
dependency introduced by this feature.

**Storage**: PostgreSQL via EF Core. No schema change — no new table and no new column. The printer
catalog stays an in-code lookup (`PrinterModelCatalog`), not a database table (see research.md §4).

**Testing**: xUnit + FluentAssertions (`OpenFarm.Domain.Tests`, `OpenFarm.Application.Tests`); xUnit +
`WebApplicationFactory` + Testcontainers.PostgreSql (`OpenFarm.Api.IntegrationTests`); Vitest + React
Testing Library (`web/src`).

**Target Platform**: Linux server (API container) + browser SPA — unchanged.

**Project Type**: Web application (frontend + backend monorepo, matches existing `api/` + `web/` split).

**Performance Goals**: None new. Explicitly must **not** introduce polling — story 4 exists precisely
to confirm the existing SignalR push path stays the sole update mechanism (constitution Principle V).

**Constraints**: The immutability check for brand/model/serial number (FR-010) must reject a
mismatched value outright rather than silently drop it, without adding a dedicated audit-log path
(clarification, 2026-07-17). No new permission may be introduced (spec Assumptions) — the broadened
edit and the new catalog-read endpoint reuse `printers:edit-connection` and `printers:register`
respectively. No status-based blocking on edit or remove, matching FR-006/FR-001's "always allowed
regardless of status" clarification.

**Scale/Scope**: One endpoint replaced (`PUT .../connection` → `PATCH /api/v1/printers/{id}`), one
endpoint added (`GET /api/v1/printers/catalog`), one relocated UI action (Remove, onto the detail
page), two UI surfaces with an action removed (fleet table row menu, fleet grid card overlay), one
audit with an expected zero-diff outcome (SignalR contract). No new domain entities.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture, Enforced by the Compiler and CI** — PASS. The unified edit stays a thin
  `OpenFarm.Api` endpoint over a `OpenFarm.Application` command handler over `OpenFarm.Domain` methods
  (`Printer.Rename`, existing `Printer.EditConnection`), exactly like today's connection-only edit. The
  new catalog endpoint is the same shape (`Application` query over `Domain`'s existing
  `PrinterModelCatalog`, no `Infrastructure`/EF Core involvement since the catalog is in-code). No
  FastEndpoints/EF Core reference is added to `Domain` or `Application`.
- **II. Test Discipline Where It Pays, Not Uniformly** — PASS, with one deliberate TDD carve-out: the
  brand/model/serial-number mismatch rejection (FR-010) is small, deterministic, well-specified logic
  ("given a mismatched value, reject") — TDD applies, same bucket as existing `PrinterModelCatalog`
  and status-transition tests. The command handler's orchestration (load → verify → mutate → save →
  reconnect) and the relocated React Remove action are test-after, matching how
  `EditPrinterConnectionCommandHandler` and `PrinterDetailActions` are tested today.
- **III. Consistent User Experience Across the Fleet** — PASS. Consolidating Edit/Remove onto one
  surface (the detail page) is this principle in action — one place, one behavior, instead of two
  UIs that could drift. The catalog fetch goes through TanStack Query like every other server-state
  read, with an explicit loading/error state (US3 AC3) rather than a silent empty list. Errors continue
  to surface as `ProblemDetails` via the existing `DomainExceptionHandler`.
- **IV. Build for Expansion, Not Speculation** — PASS. The catalog is exposed via an endpoint but
  deliberately **not** promoted to a database table — `specs/002-printer-integration/data-model.md`
  already reasoned through this (no `printer_profiles` extraction until a second real need exists);
  005 doesn't relitigate that, it just adds a read path over the same in-code source. No new
  permission is introduced (reuses two existing permission strings). Story 4 explicitly declines to
  restructure the SignalR contract unless the audit finds a concrete brand-specific assumption —
  no speculative generalization.
- **V. Performance and Real-Time Responsiveness by Construction** — PASS. No polling introduced;
  story 4's entire purpose is confirming the push-only SignalR path has no hidden gap as brands scale
  up. Removing two UI action surfaces and adding one detail-page action has no bearing on the job
  queue/virtualization concerns this principle otherwise targets.

No violations — Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/005-printer-fleet-refinements/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/
│   └── printers-api.md   # Phase 1 output — documents only the endpoints this feature adds/replaces
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by /speckit-plan)
```

### Source Code (repository root)

```text
api/
├── src/
│   ├── OpenFarm.Domain/Printers/
│   │   ├── Printer.cs                          # add Rename(name); EditConnection(ip) already exists
│   │   ├── PrinterModelCatalog.cs              # add an enumerable accessor over existing entries
│   │   └── ImmutablePrinterFieldMismatchException.cs   # new, FR-010
│   ├── OpenFarm.Application/Printers/
│   │   ├── EditPrinterCommand.cs               # replaces EditPrinterConnectionCommand.cs
│   │   ├── EditPrinterCommandHandler.cs        # replaces EditPrinterConnectionCommandHandler.cs
│   │   ├── EditPrinterCommandValidator.cs      # replaces EditPrinterConnectionCommandValidator.cs
│   │   └── GetPrinterCatalogQuery.cs           # new
│   └── OpenFarm.Api/Endpoints/Printers/
│       ├── EditPrinterEndpoint.cs              # replaces EditPrinterConnectionEndpoint.cs, PATCH /api/v1/printers/{id}
│       └── GetPrinterCatalogEndpoint.cs        # new, GET /api/v1/printers/catalog
└── tests/
    ├── OpenFarm.Domain.Tests/Printers/PrinterTests.cs                       # + Rename, + mismatch rejection cases
    ├── OpenFarm.Application.Tests/Printers/EditPrinterCommandHandlerTests.cs # replaces EditPrinterConnection... tests
    └── OpenFarm.Api.IntegrationTests/Printers/
        ├── EditPrinterEndpointTests.cs         # replaces the connection-only edit's endpoint tests
        └── GetPrinterCatalogEndpointTests.cs   # new

web/
└── src/features/printers/
    ├── components/
    │   ├── EditPrinterDialog.tsx        # broadened: name + optional connection fields
    │   ├── AddPrinterDialog.tsx         # drop hardcoded PRINTER_BRANDS/MODELS_BY_BRAND, fetch catalog
    │   ├── PrintersTable.tsx            # remove Edit/Remove from the actions column
    │   ├── PrintersGrid.tsx             # remove Edit/Remove from the card overlay
    │   └── PrinterDetailActions.tsx     # wire a real Remove action alongside existing Settings
    ├── PrintersPage.tsx                 # drop editingPrinterId/removeMutation (moves to detail page)
    └── PrinterDetailPage.tsx            # owns edit + remove state; navigates to fleet list on remove
```

**Structure Decision**: Existing `api/` (Clean Architecture: Domain/Application/Infrastructure/Api) +
`web/` (feature-folder SPA) structure, unchanged. This feature touches only the `Printers` slice of
each project — no new project, no new top-level folder.

## Complexity Tracking

*Not applicable — no Constitution Check violations.*
