# Implementation Plan: Fleet- and Inventory-Aware Filament Matching

**Branch**: `010-filament-matching` | **Date**: 2026-07-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/010-filament-matching/spec.md`

## Summary

Adding an STL part fails today with a "Generic PLA not found" error because the STL fallback threads an
engine-specific literal profile name (`"Generic PLA"`/`"Bambu PLA Basic"`) through a resolution path
(`RepresentativeFilamentResolver`, from `009-process-profiles`) that expects a real material type like
`"PLA"`. The fix is a one-value configuration change (`research.md` R1). Beyond that bug fix, this feature
replaces that resolver's arbitrary "any System catalog profile" pick with a deterministic, tiered match
against what the print farm actually owns — a spool loaded on the target printer, then loaded anywhere in
the fleet, then the filament library (storage), and only then the generic System catalog — reusing the
already-existing but previously-unconsumed `Filament.FilamentProfileId` link between the filament-tracking
domain (`006-filament-tracking`) and the profiles domain (`003`/`009`). The result is surfaced on a part's
estimate as a visible source label, riding the same hand-off path the estimate values themselves already
use. The matching capability is built as a standalone Application-layer class so a future job-queuing
feature can depend on it unchanged.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend); TypeScript (strict) / React 19 (frontend label rendering
only — no new frontend flow)

**Primary Dependencies**: FastEndpoints (unchanged endpoint surface — no new endpoints), EF Core (one new
migration), existing repository interfaces (`ISpoolRepository`, `IFilamentRepository`,
`IFeedUnitRepository`, `IFilamentProfileRepository`) — no new packages

**Storage**: PostgreSQL via `OpenFarmDbContext` — one migration adding two nullable columns to the existing
`Parts` table (`data-model.md`); no new tables

**Testing**: xUnit + FluentAssertions for the new matching resolver (`Application` layer, test-after per
Constitution Principle II — this is orchestration over repositories, not a `Domain` state machine or
weight-tracking math); xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql for the STL-add
regression and end-to-end tiered-match scenarios; Vitest + React Testing Library for the part detail page's
new label rendering

**Target Platform**: Linux server (existing deployment target) — no platform-specific change

**Project Type**: Web application (existing `api/` + `web/` monorepo structure) — this feature touches
`OpenFarm.Domain`, `OpenFarm.Application`, `OpenFarm.Infrastructure`, and one frontend display component; no
new project

**Performance Goals**: No new performance target beyond existing slicing/estimate latency; the matching
capability's in-memory joins (spools × filaments × feed units) operate at self-hosted single-tenant fleet
scale (tens of printers/spools, not thousands) — consistent with every other in-memory LINQ join already in
`ResolveSlicingProfileSelection`/`RepresentativeFilamentResolver`

**Constraints**: Must not change any existing public method signature's meaning for callers outside this
feature's touched files; must not introduce a new "printer availability" concept (`research.md` R4); must
never override a manually narrowed filament requirement (`FR-011`, already structurally guaranteed)

**Scale/Scope**: Single feature, four user stories, one migration, no new endpoints, one existing resolver
class replaced in place, three existing hand-off points (job → part → read model) extended with two parallel
fields each

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture** — PASS. The matching capability lives in `OpenFarm.Application.Profiles`,
  depends only on repository interfaces (never `Infrastructure` or `FastEndpoints` types), matching the
  existing `RepresentativeFilamentResolver` it replaces. `FilamentMatchSource` is a `Domain.Profiles` enum,
  framework-agnostic.
- **II. Test Discipline Where It Pays** — PASS. The matching resolver is orchestration over repositories
  (Application-layer), not a `Domain` state machine or filament weight-tracking math — test-after is the
  correct discipline here, consistent with how `RepresentativeFilamentResolver`/`ResolveSlicingProfileSelection`
  are already tested today. No new FluentValidation gate is introduced.
- **III. Consistent UX Across the Fleet** — PASS. The new `estimatedFilamentMatchLabel` reuses the existing
  `"System: {vendor}"` / `"User"` origin-labeling idiom from `ProfilePickerCombobox.tsx`, extended
  consistently rather than inventing a new labeling convention.
- **IV. Build for Expansion, Not Speculation** — PASS. The matching capability is reusable by construction
  (no `Parts`-specific dependency) for the future job-queuing consumer the spec names, without speculatively
  building job-queuing itself. Every repository call already flows through tenant-scoped repositories — no
  new tenant-isolation surface.
- **V. Performance and Real-Time Responsiveness** — PASS. No new list rendering, no new long-running/polling
  work; the in-memory joins are proportionate to existing patterns in the same resolution path (see Scale/Scope
  above). No `BackgroundService` changes.
- **Durable Recovery of In-Flight Background Work** — PASS, unchanged. The matching capability is called
  synchronously within the existing `PartFileProcessor.ResolveEstimateAsync`/`SliceAsync` call chain, which
  already routes every non-cancellation failure to a terminal job transition (`SlicingFailedException`/
  `SlicingProfileUnconfiguredException`) per the existing guardrail — this feature adds no new failure path
  that needs its own reconciliation handling; a matching failure (e.g., genuinely no PLA anywhere) already
  surfaces through that same existing terminal-transition machinery.

No violations. Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/010-filament-matching/
├── plan.md              # This file
├── research.md           # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/
│   └── part-estimate-filament-source-delta.md
└── tasks.md              # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
api/
├── src/
│   ├── OpenFarm.Domain/
│   │   ├── Profiles/FilamentMatchSource.cs           # NEW — enum
│   │   └── Parts/PartProcessingJob.cs                # CHANGED — two new preview fields + ResolveEstimate signature
│   │   └── Parts/Part.cs                             # CHANGED — two new fields + Create/ReplaceFile signature
│   ├── OpenFarm.Application/
│   │   ├── Profiles/RepresentativeFilamentResolver.cs # CHANGED — tiered matching, new return type/signature
│   │   ├── Profiles/FilamentMatchResult.cs            # NEW — record
│   │   ├── Parts/ResolveSlicingProfileSelection.cs     # CHANGED — thread printerId + match source through
│   │   ├── Parts/ISlicerDefaultsProvider.cs            # CHANGED — GetFallbackMaterialType() replaces GetGenericFallbackMaterialProfile(engine)
│   │   ├── Parts/AddPartCommandHandler.cs              # CHANGED — call site for the renamed provider method
│   │   ├── Parts/ReplacePartFileCommandHandler.cs      # CHANGED — same
│   │   ├── Parts/ConfirmPartProcessingJobCommandHandler.cs # CHANGED — pass through new fields at existing hand-off points
│   │   └── Parts/GetPartQuery.cs                       # CHANGED — two new PartDetail fields
│   └── OpenFarm.Infrastructure/
│       ├── Parts/SlicerOptions.cs                      # CHANGED — FallbackMaterialType replaces GenericFallbackMaterialProfile
│       ├── Parts/SlicerDefaultsProvider.cs             # CHANGED — matches new interface
│       └── Persistence/Migrations/                     # NEW migration — two nullable columns on Parts
└── tests/
    ├── OpenFarm.Application.Tests/Profiles/            # CHANGED/NEW — matching-resolver tier tests
    └── OpenFarm.Api.IntegrationTests/Parts/             # CHANGED/NEW — STL-add regression + tiered end-to-end

web/
└── src/features/parts/
    └── components/…                                    # CHANGED — render estimatedFilamentMatchLabel on part detail
```

**Structure Decision**: Existing Clean Architecture layout (`OpenFarm.Domain` / `Application` / `Infrastructure`
/ `Api` + `web/`) is reused as-is — this feature adds no new project and no new top-level folder; every
touched file already exists except the two new small types (`FilamentMatchSource`, `FilamentMatchResult`)
and the one new migration.

## Complexity Tracking

*No Constitution Check violations — table intentionally omitted.*
