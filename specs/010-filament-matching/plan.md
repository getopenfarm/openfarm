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
domain (`006-filament-tracking`) and the profiles domain (`003`/`009`). The matching capability is built as a
standalone Application-layer class so a future job-queuing feature can depend on it unchanged. Surfacing
*which* tier produced a match (an audit trail / live matching view) is explicitly deferred — this iteration
only makes the estimate itself more realistic, not its provenance visible; see `research.md` R6.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend only — no frontend change in this feature)

**Primary Dependencies**: FastEndpoints (unchanged endpoint surface — no new endpoints, no changed response
shapes), existing repository interfaces (`ISpoolRepository`, `IFilamentRepository`, `IFeedUnitRepository`,
`IFilamentProfileRepository`) — no new packages

**Storage**: PostgreSQL via `OpenFarmDbContext` — no schema change, no migration; this feature reads
existing tables only

**Testing**: xUnit + FluentAssertions for the new matching resolver (`Application` layer, test-after per
Constitution Principle II — this is orchestration over repositories, not a `Domain` state machine or
weight-tracking math); xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql for the STL-add
regression and end-to-end tiered-match scenarios

**Target Platform**: Linux server (existing deployment target) — no platform-specific change

**Project Type**: Web application (existing `api/` + `web/` monorepo structure) — this feature touches only
`OpenFarm.Application` and `OpenFarm.Infrastructure`; no `Domain`, `Api`, or frontend changes, no new project

**Performance Goals**: No new performance target beyond existing slicing/estimate latency; the matching
capability's in-memory joins (spools × filaments × feed units) operate at self-hosted single-tenant fleet
scale (tens of printers/spools, not thousands) — consistent with every other in-memory LINQ join already in
`ResolveSlicingProfileSelection`/`RepresentativeFilamentResolver`

**Constraints**: Must not change any existing public method signature's meaning for callers outside this
feature's touched files; must not introduce a new "printer availability" concept (`research.md` R4); must
never override a manually narrowed filament requirement (`FR-011`, already structurally guaranteed); must not
persist or expose which match tier was used this iteration (deferred per `research.md` R6)

**Scale/Scope**: Single feature, three user stories, no migration, no new/changed endpoints, one existing
resolver class replaced in place, two call sites updated to match its new signature

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture** — PASS. The matching capability lives in `OpenFarm.Application.Profiles`,
  depends only on repository interfaces (never `Infrastructure` or `FastEndpoints` types), matching the
  existing `RepresentativeFilamentResolver` it replaces. `FilamentMatchSource`/`FilamentMatchResult` are
  plain Application-layer types, not persisted, so no `Domain` entity changes at all.
- **II. Test Discipline Where It Pays** — PASS. The matching resolver is orchestration over repositories
  (Application-layer), not a `Domain` state machine or filament weight-tracking math — test-after is the
  correct discipline here, consistent with how `RepresentativeFilamentResolver`/`ResolveSlicingProfileSelection`
  are already tested today. No new FluentValidation gate is introduced.
- **III. Consistent UX Across the Fleet** — N/A this iteration. No UI or API-visible change; nothing new for
  an operator to see, so no labeling-consistency question arises. Revisit when the deferred visibility
  feature (`research.md` R6) is actually built.
- **IV. Build for Expansion, Not Speculation** — PASS, and this cuts both ways here: the matching capability
  is reusable by construction (no `Parts`-specific dependency) for the future job-queuing consumer the spec
  names, without speculatively building job-queuing itself — and, per the same principle, this plan
  deliberately does **not** build match-source persistence/UI ahead of a confirmed second consumer for it
  (the earlier draft of this plan did; trimmed after review). Every repository call already flows through
  tenant-scoped repositories — no new tenant-isolation surface.
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
└── tasks.md              # Phase 2 output (/speckit-tasks — not created here)
```

No `contracts/` directory — this feature changes no request/response shape (see Technical Context).

### Source Code (repository root)

```text
api/
├── src/
│   ├── OpenFarm.Application/
│   │   ├── Profiles/RepresentativeFilamentResolver.cs # CHANGED — tiered matching, new return type/signature
│   │   ├── Profiles/FilamentMatchResult.cs            # NEW — record (Profile + Source), request-scoped only
│   │   ├── Profiles/FilamentMatchSource.cs             # NEW — enum (FleetLoaded/FilamentLibrary/SystemCatalog)
│   │   ├── Parts/ResolveSlicingProfileSelection.cs     # CHANGED — thread printerId through, unwrap .Profile
│   │   ├── Parts/ISlicerDefaultsProvider.cs            # CHANGED — GetFallbackMaterialType() replaces GetGenericFallbackMaterialProfile(engine)
│   │   ├── Parts/AddPartCommandHandler.cs              # CHANGED — call site for the renamed provider method
│   │   └── Parts/ReplacePartFileCommandHandler.cs      # CHANGED — same
│   └── OpenFarm.Infrastructure/
│       ├── Parts/SlicerOptions.cs                      # CHANGED — FallbackMaterialType replaces GenericFallbackMaterialProfile
│       └── Parts/SlicerDefaultsProvider.cs             # CHANGED — matches new interface
└── tests/
    ├── OpenFarm.Application.Tests/Profiles/            # CHANGED/NEW — matching-resolver tier tests
    └── OpenFarm.Api.IntegrationTests/Parts/             # CHANGED/NEW — STL-add regression + tiered end-to-end
```

No `OpenFarm.Domain`, `OpenFarm.Api`, or `web/` changes — nothing persisted, no new/changed endpoint, no UI.

**Structure Decision**: Existing Clean Architecture layout (`OpenFarm.Domain` / `Application` / `Infrastructure`
/ `Api` + `web/`) is reused as-is — this feature adds no new project, no new top-level folder, and touches
only `Application`/`Infrastructure`; every file listed already exists except the two new small
Application-layer types (`FilamentMatchSource`, `FilamentMatchResult`).

## Complexity Tracking

*No Constitution Check violations — table intentionally omitted.*
