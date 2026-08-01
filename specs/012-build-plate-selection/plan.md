# Implementation Plan: Build Plate Selection — Per-Printer Physical Plate, Configured Alongside Machine/Process Profile

**Branch**: `012-build-plate-selection` | **Date**: 2026-07-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/012-build-plate-selection/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Every printer already selects a machine profile and process profile on its own page (`009-process-profiles`); this feature adds the missing third selection — which physical build plate is currently installed — configured the same way, and reads it at slice time so the bed temperature actually matches the plate on the machine instead of one silently fixed value. Build plate is deliberately *not* a fourth ingested profile catalog (FR-004): it is a small, fixed, hand-authored enumeration (`Cool Plate`, `Engineering Plate`, `High Temp Plate`, `Textured PEI Plate`, `Textured Cool Plate`, `Supertack Plate`) whose compatible subset is gated by each printer's own immutable registered Brand/ModelCode (`PrinterModelCatalog`, `005-printer-fleet-refinements`), independent of whichever machine profile is linked. Directly inspecting ten real OrcaSlicer-bundled vendor profile sets (not just this repo's narrow test fixtures) confirmed the plate-type↔JSON-key schema is vendor-agnostic and that `PrinterModelCatalog`'s existing per-entry extension pattern already accommodates a future non-Bambu brand with no structural change (`research.md` R9). Technical approach: extend the existing `Printer` entity with a nullable `BuildPlateType` enum column (mirroring how `PrinterStatus` is already persisted, not a new FK/table); extend `PrinterModelCatalog`'s static `PrinterModel` record with a hand-authored `CompatiblePlateTypes` set per catalog entry; fold the selection into the existing single `EditPrinterCommand`/`EditPrinterEndpoint` that already carries `MachineProfileId`/`ProcessProfileId`; and extend `ResolveSlicingProfileSelection` to require the printer's plate be set, pre-validate the resolved filament preset declares a real (non-`"0"`) temperature for that plate type, and inject the matching `curr_bed_type` value into the merged process-settings JSON so OrcaSlicer/Bambu Studio's own CLI resolves the correct bed temperature — the exact mechanism already proven correct by the existing golden-file test suite's `WithBedTypeOverride` helper.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, `api/`); TypeScript (`strict: true`) / React 19 (frontend, `web/`)

**Primary Dependencies**: FastEndpoints, EF Core 10 + Npgsql, FluentValidation (backend); React Router v7, TanStack Query, react-hook-form + zod, shadcn/ui, Orval-generated API client (frontend) — all pre-existing, no new dependency introduced by this feature

**Storage**: PostgreSQL — one new nullable `text` column (`BuildPlateType`) on the existing `Printers` table; no new tables

**Testing**: xUnit + FluentAssertions (`Application`/`Domain` unit tests, TDD for the plate↔temp-key mapping and the compatibility gate per `research.md` R8); xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql (`Api` integration tests, extending `EditPrinterEndpointTests.cs`); Vitest + React Testing Library (`PrinterFormPage.test.tsx`, `PrinterDetailPage.test.tsx`, test-after)

**Target Platform**: Linux server (API host) + browser SPA — unchanged; no new platform surface

**Project Type**: Web application (existing `api/` + `web/` monorepo split) — this feature adds no new project/service

**Performance Goals**: No new perf-sensitive path — this is a single additional field on an existing low-frequency edit command and a constant-time JSON-key lookup/patch inside the existing slicing-resolution path; no measurable impact on the job-queue virtualization or SignalR real-time paths

**Constraints**: Must not introduce a fourth ingested profile catalog (FR-004/FR-010 — hard constraint from the spec itself, not just a preference); must not gate build plate compatibility on the linked machine profile (FR-002, per clarification); must not read live connection telemetry for plate detection (FR-011)

**Scale/Scope**: One new nullable enum column + one static catalog extension (backend); one extended command/endpoint/query; one new domain exception; two new granular activity-log event types; one new frontend `<Select>` control plus a read-only display row — no new endpoints, no new database tables

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture, Enforced by the Compiler and CI** — PASS. `BuildPlateType` enum and
  `PrinterModelCatalog.CompatiblePlateTypes` live in `Domain` (framework-agnostic); the compatibility
  gate and slicing-resolution changes live in `Application`; only the endpoint/request-DTO/response-DTO
  additions touch `Api`. No FastEndpoints/EF Core reference crosses into `Domain`/`Application`.
- **II. Test Discipline Where It Pays, Not Uniformly** — PASS. `research.md` R8 explicitly scopes TDD
  to the two genuinely deterministic pieces (temp-key mapping + "no value recorded" detection;
  compatibility gate) and test-after to everything else (EF config, read-side query, React), matching
  the constitution's own list of what TDD is/isn't required for.
  Data-populating `PrinterModelCatalog.CompatiblePlateTypes` for `X1`/`X1E`/`P1P`/`P1S` from the
  real vendor bundle (research.md R2) is a `tasks.md`-level data task, not a design gap — flagged here
  so it isn't silently dropped before implementation.
- **III. Consistent User Experience Across the Fleet** — PASS. Uses the existing shadcn/ui primitives
  (`<Select>`), the existing RFC 7807 `ProblemDetails` error shape (one more `switch` arm in
  `DomainExceptionHandler`), and does not touch any SignalR/real-time surface (build plate is a
  configuration field, not fleet telemetry) — no new pattern introduced.
- **IV. Build for Expansion, Not Speculation** — PASS. `organization_id` is untouched (build plate
  selection lives on the already-tenant-scoped `Printers` table); no speculative extraction — the
  feature deliberately reuses the existing `EditPrinterCommand`/`EditPrinterEndpoint` rather than
  inventing a parallel "link build plate" endpoint, and reuses `PrinterModelCatalog`'s existing static
  extension pattern rather than building a new catalog abstraction for a six-item enum, and defers a
  dynamic ingestion-time compatibility-derivation pipeline (viable per `research.md` R9, but
  speculative while `PrinterModelCatalog` has exactly one real brand) until a second brand is actually
  onboarded.
- **V. Performance and Real-Time Responsiveness by Construction** — PASS. No polling, no unbounded
  list, no `BackgroundService` involved; the JSON-key lookup/patch added to
  `ResolveSlicingProfileSelection` is O(1) against an already-in-memory JSON blob on an already
  request-scoped, non-hot-loop path.
- **Security & Multi-Tenancy Isolation** — PASS. No new tenant-crossing surface; `EditPrinterEndpoint`
  keeps its existing `[Permission("printers:edit-connection")]` gate; the new
  `PrinterBuildPlateSelected`/`Cleared` events flow through the same `IActivityLogWriter` every other
  granular event in this handler already uses.

No violations requiring justification — **Complexity Tracking is empty** (see below).

## Project Structure

### Documentation (this feature)

```text
specs/012-build-plate-selection/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
api/
├── src/
│   ├── OpenFarm.Domain/
│   │   └── Printers/
│   │       ├── Printer.cs                              # + BuildPlateType property, Select/ClearBuildPlate methods
│   │       ├── PrinterModel.cs                          # + BuildPlateType enum, + CompatiblePlateTypes field
│   │       ├── PrinterModelCatalog.cs                    # + CompatiblePlateTypes per Entries row
│   │       └── IncompatibleBuildPlateException.cs        # new
│   ├── OpenFarm.Application/
│   │   ├── Printers/
│   │   │   ├── EditPrinterCommand.cs                     # + BuildPlateType? field
│   │   │   ├── EditPrinterCommandHandler.cs               # + compatibility gate + granular activity-log events
│   │   │   └── GetPrinterDetailQuery.cs                   # + BuildPlateType, CompatibleBuildPlateTypes
│   │   └── Parts/
│   │       └── ResolveSlicingProfileSelection.cs           # + plate-required checks, temp-key validation, curr_bed_type injection
│   ├── OpenFarm.Infrastructure/
│   │   └── Persistence/
│   │       ├── Configurations/PrinterConfiguration.cs      # + BuildPlateType HasConversion<string>()
│   │       └── Migrations/<timestamp>_AddPrinterBuildPlate.cs   # new
│   └── OpenFarm.Api/
│       ├── Endpoints/Printers/
│       │   ├── EditPrinterEndpoint.cs                     # + BuildPlateType request field
│       │   └── PrinterDetailResponse.cs                   # + buildPlateType, compatibleBuildPlateTypes
│       └── ExceptionHandling/DomainExceptionHandler.cs      # + IncompatibleBuildPlateException mapping
└── tests/
    ├── OpenFarm.Domain.Tests/Printers/                    # PrinterModelCatalog compatibility tests (TDD)
    ├── OpenFarm.Application.Tests/
    │   ├── Printers/EditPrinterCommandHandlerTests.cs        # + compatibility-gate cases (TDD)
    │   └── Parts/ResolveSlicingProfileSelectionTests.cs      # + plate-required/temp-key cases (TDD), SC-003 case
    └── OpenFarm.Api.IntegrationTests/Printers/EditPrinterEndpointTests.cs   # + build-plate cases (test-after)

web/
└── src/
    ├── lib/api/generated/
    │   ├── endpoints.schemas.ts                          # orval-regenerated: BuildPlateType, request/response fields
    │   └── printers/printers.ts                           # orval-regenerated (no new hook, existing mutation gains a field)
    └── features/printers/
        ├── PrinterFormPage.tsx                            # + build plate <Select> rendering backend labels, buffered like machine/process; no local enum→label map (FR-012)
        ├── PrinterFormPage.test.tsx                        # + build plate cases (test-after)
        ├── PrinterDetailPage.tsx                            # + read-only "Build plate" row rendering buildPlateTypeLabel as-is
        └── PrinterDetailPage.test.tsx                       # + build plate display case (test-after)
```

**Structure Decision**: Existing web-application structure (`api/` four-project Clean Architecture +
`web/` SPA) is unchanged — this feature extends files in place rather than adding new
projects/features/folders, following the same "extend, don't parallel-build" approach
009-process-profiles used to add process profile onto the machine-profile surface.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*No violations — table intentionally empty.*
