# Implementation Plan: Multi-Engine, Multi-Version Slicer Management

**Branch**: `008-orca-slicer-multi-version` | **Date**: 2026-07-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/008-orca-slicer-multi-version/spec.md`

## Summary

Phase 4 shipped in-app slicing but wired the OrcaSlicer/Bambu Studio CLI to a single hard-coded
filesystem path (`Slicing:OrcaSlicerCliPath`) with no notion of a *version* and no SuperSlicer support,
so a real deployment cannot reproduce or audit a slice, cannot run two versions side by side, and does
not work on Windows/macOS. This feature replaces the hard-coded path with a **backend-discovered registry
of bundled `(engine, version, platform)` slicers**: a startup scanner reads a bundled slicer directory
into an in-memory catalog; the org picks a default `(engine, version)` and an operator may override
per-part; the concrete engine+version used is recorded verbatim on both the `PartProcessingJob` and the
`Part`. A per-engine CLI-invocation strategy replaces the single argument string so SuperSlicer's
divergent CLI is handled alongside the PrusaSlicer-derived OrcaSlicer/Bambu Studio form. A **dev/build-time
provisioning tool** (a .NET console app driven by a JSON manifest) fetches the declared builds from each
engine's GitHub releases into the bundle; the running app never contacts GitHub.

Technical approach: extend the `SlicerEngine` enum (add `SuperSlicer`) and carry an engine+version pair
through domain/application/persistence; introduce `IAvailableSlicerCatalog` (Application contract) backed
by a `BundledSlicerScanner` (Infrastructure) eager-initialized at startup; introduce an
`ISlicerInvocation` strategy resolved per engine; store the org default as a nullable `(engine, version)`
with newest-available fallback resolved at read time; add columns + one hand-authored EF migration; add a
settings UI for the default and a per-part engine+version picker; add `tools/provision-slicers` +
`slicers.manifest.json`; and replace the Dockerfile's single-version install with the bundle layout.

## Technical Context

**Language/Version**: C# 13 on .NET 10 (backend + provisioning tool); TypeScript 5 (`strict`) on React 19,
built with Bun + Vite (frontend).

**Primary Dependencies**: FastEndpoints + FluentValidation (HTTP/validation), EF Core 10 + Npgsql
(persistence), Finbuckle.MultiTenant (tenant scoping), ASP.NET Core `BackgroundService` + `IServiceScopeFactory`
(existing part-processing worker), SignalR (existing farm hub). Frontend: React Router v7, TanStack Query,
Orval-generated API client, react-hook-form + zod, shadcn/ui. Provisioning tool: `System.Net.Http` +
`System.Text.Json` + `System.Security.Cryptography` (SHA-256), GitHub REST releases API.

**Storage**: PostgreSQL via `OpenFarmDbContext`. Schema deltas: new `SlicerVersionUsed` columns on
`PartProcessingJobs` and `Parts`; a `DefaultSlicerVersion` column on `Organizations` (the existing
`DefaultSlicerEngine` becomes nullable-semantically the "engine half" of an optional pair). The bundled
slicer set itself lives on the filesystem (install-level), **not** in the database.

**Testing**: xUnit + FluentAssertions (Domain/Application unit), `WebApplicationFactory` +
Testcontainers.PostgreSql (API integration), Vitest + React Testing Library (frontend). The real CLI
invocation is validated manually against actual binaries (same carve-out as the existing `CliSlicerEngine`);
`FakeSlicerEngine` remains the automated seam. The provisioning tool's pure logic (manifest parse,
idempotency, integrity check, the non-macOS `.dmg` fallback) is unit-tested; live GitHub download and
real macOS `hdiutil` extraction are manual.

**Target Platform**: Linux, Windows, and macOS self-hosted hosts (FR-009). The shipped artifact is
platform-specific; the running app resolves and invokes only the bundled build whose platform matches the
host.

**Project Type**: Web application (ASP.NET Core API + React SPA) in an existing monorepo, plus a new
dev/build-time CLI tool.

**Performance Goals**: Startup discovery scan completes well within existing boot budget for a realistic
bundle (tens of engine×version leaves) — a directory walk, no process execution. Slice latency is
unchanged from Phase 4 (bounded by `MaxConcurrentSlicingOperations`). Default resolution and availability
checks are in-memory catalog lookups (sub-millisecond).

**Constraints**: The running application MUST make **zero** network calls to obtain slicers (FR-018) —
provisioning is exclusively dev/build-time. No user-facing concept of a binary, path, or PATH may exist
anywhere (FR-001/FR-020). The supported `(engine, version)` set is append-only (FR-008a), so no
"recorded version disappeared" path is built. No slicer is validated by execution at discovery time
(FR-002) — presence is availability; a broken binary surfaces as a normal slice failure.

**Scale/Scope**: Three engines (OrcaSlicer, Bambu Studio, SuperSlicer), each multi-version/multi-platform;
a single self-hosted org today but org-scoped default by construction. Expected bundle size on the order
of a handful of versions per engine; the catalog and picker must stay usable as combinations grow.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — still passing; no violations.*

- **I. Clean Architecture, enforced.** `SlicerEngine` (+ `SuperSlicer`), the `SlicerPlatform` enum, and
  the `SlicerRef`/`AvailableSlicer` value types live in `Domain`; the `IAvailableSlicerCatalog` and
  `ISlicerInvocation` contracts + the extended `ISlicerEngine` live in `Application`; the
  `BundledSlicerScanner`, per-engine invocation strategies, and host-platform resolution live in
  `Infrastructure`; `Api` only adds thin endpoints/DTOs. No `EF Core`/`FastEndpoints` leaks into
  `Domain`/`Application`. The provisioning tool is a standalone project with no dependency edge from the
  runtime layers. **PASS.**
- **II. Test discipline where it pays.** TDD (red-green-refactor) for the deterministic new logic: the
  cross-engine version comparator (`SlicerVersionComparer`), the newest-available default resolution, the
  select-as-default / slice-with availability+platform validation gates, and the manifest→asset resolution
  logic. Test-after / fake-based for the disk scanner, the real CLI invocation (manual against binaries,
  never a CI gate — mirrors `CliSlicerEngine` today), the EF config/migration, and React components.
  **PASS.**
- **III. Consistent UX.** New settings page + per-part picker gate on the `permissions` catalog
  (`settings:slicing-defaults:update`, `parts:create`), never role names; all new errors surface as
  RFC 7807 `ProblemDetails`; forms use react-hook-form + zod + shadcn/ui primitives. The catalog is
  install-static (no real-time data), so no SignalR path is owed here. **PASS.**
- **IV. Build for expansion, not speculation.** The model is engine-generic — engine is a first-class
  attribute beside version, so adding a fourth engine is teaching the provisioning tool a release source
  and the invocation layer a CLI form, not a redesign (FR-022). The default is org-scoped
  (`organization_id` already present) even though one org exists today; the bundle is install-level and
  shared. No premature abstraction beyond the two swap points the spec names. **PASS.**
- **V. Performance & durable recovery.** No new non-terminal background state is introduced — slicing
  already routes every failure to `SlicingFailedException` → `MarkFailed`, and the existing startup
  reconciliation pass still covers orphaned jobs. The in-memory catalog's source of truth is the
  filesystem bundle (rebuilt every boot), not a durable queue, so the durability guardrail does not
  regress. A slice against a missing/unavailable `(engine, version)` fails **promptly and terminally**
  (FR-006), never hangs. **PASS.**

No entries required in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/008-orca-slicer-multi-version/
├── plan.md              # This file (/speckit-plan command output)
├── spec.md              # Feature specification (input)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── slicer-management-api.md    # HTTP surface (settings default + available-slicers + part override)
│   └── slicer-manifest.schema.json # Provisioning manifest JSON Schema
├── checklists/          # existing
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created here)
```

### Source Code (repository root)

```text
api/
├── src/
│   ├── OpenFarm.Domain/
│   │   ├── Parts/
│   │   │   ├── SlicerEngine.cs           # + SuperSlicer
│   │   │   ├── SlicerPlatform.cs         # NEW enum: Linux | Windows | MacOs
│   │   │   ├── SlicerRef.cs              # NEW value: (engine, version) selection identity
│   │   │   ├── AvailableSlicer.cs        # NEW value: (engine, version, platform) discovered entry
│   │   │   └── SlicerVersionComparer.cs  # NEW: tolerant cross-engine "newest" ordering
│   │   └── Organizations/Organization.cs # default becomes an optional (engine, version) pair
│   ├── OpenFarm.Application/Parts/
│   │   ├── ISlicerEngine.cs              # SliceAsync now takes a resolved SlicerRef
│   │   ├── ISlicerInvocation.cs          # NEW: per-engine CLI command builder contract
│   │   ├── IAvailableSlicerCatalog.cs    # NEW: discovered-slicer registry contract
│   │   ├── ResolveSlicerSelection...cs   # NEW: default/override → validated SlicerRef
│   │   ├── AddPartCommand*.cs            # override carries version
│   │   ├── UpdateSlicingDefaults*.cs     # command/handler take (engine, version)
│   │   └── GetSlicingDefaults*.cs        # returns default + full available list
│   ├── OpenFarm.Infrastructure/Parts/
│   │   ├── BundledSlicerScanner.cs       # NEW: startup filesystem scan → catalog
│   │   ├── AvailableSlicerCatalog.cs     # NEW: in-memory singleton registry
│   │   ├── HostPlatform.cs               # NEW: RuntimeInformation → SlicerPlatform
│   │   ├── SlicerInvocation/             # NEW: PrusaSliceFamilyInvocation + SuperSlicerInvocation
│   │   ├── CliSlicerEngine.cs            # resolves entry from catalog + strategy by engine
│   │   ├── SlicerBundleOptions.cs        # replaces path-based SlicerOptions (RootDirectory only)
│   │   └── Persistence/Migrations/2026..._AddSlicerVersionTracking.cs  # NEW hand-authored migration
│   └── OpenFarm.Api/Endpoints/
│       ├── Settings/UpdateSlicingDefaultsEndpoint.cs   # (engine, version) body
│       ├── Settings/GetSlicingDefaultsEndpoint.cs      # default + available list
│       ├── Settings/ListAvailableSlicersEndpoint.cs    # NEW (or folded into GetSlicingDefaults)
│       └── Parts/CreatePartEndpoint.cs + ReplacePartFileEndpoint.cs  # + SlicerVersion field
└── tests/  # unit (Domain/Application), integration (Api), infra (scanner/invocation) mirrors above

tools/
└── provision-slicers/           # NEW .NET console tool (cross-platform, dev/build-time only)
    ├── ProvisionSlicers.csproj
    ├── Program.cs               # read manifest → download (manifest-supplied URL) → verify → extract → report
    └── (unit tests alongside)

slicers.manifest.json           # NEW repo-root manifest: desired (engine, version, assets: {platform: url})
slicers.lock.json               # NEW generated: resolved asset URL + sha256 per entry (reproducibility)

web/src/features/
├── settings/SettingsPage.tsx    # + link/card to Slicing Defaults
├── settings/SlicingDefaultsPage.tsx  # NEW: pick default (engine, version) from available list
└── parts/components/AddOrReplacePartDialog.tsx  # NEW optional engine+version override control

infra/docker/api.Dockerfile      # bundle layout via provisioning tool, not a single hard-coded install
```

**Structure Decision**: Retain the existing four-project Clean Architecture backend + React SPA monorepo.
All new runtime contracts follow the established layering (value types/enums in `Domain`, catalog +
invocation contracts in `Application`, scanner + strategies in `Infrastructure`, thin endpoints in `Api`).
The provisioning tool is a **separate `tools/provision-slicers` .NET console project** deliberately kept
out of the runtime dependency graph, so nothing in the running app can reach GitHub-fetching code
(FR-018). `slicers.manifest.json`/`slicers.lock.json` live at the repo root as the maintainer-authored
bundling declaration.

## Complexity Tracking

> No Constitution Check violations — this section is intentionally empty.
