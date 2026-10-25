# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

OpenFarm is a print-farm management webapp (printers, filament, maintenance, parts, assemblies/BOMs, print jobs), built open-source-first as a single-tenant, self-hosted app (one farm per install). **Phase 0 (Foundation) is delivered** — see `specs/001-foundation/` for its spec/plan/tasks. Real JWT login for a single seeded operator, a `FarmSettings` singleton row, encrypted/masked/immutable printer connection secrets, the structural activity log, and a base shadcn UI kit are all live; `api/src/OpenFarm.Infrastructure/Persistence/Migrations/` has the one `InitialCreate` migration. Phase 1 (Fleet management + live Bambu integration) is next and not yet started. One CI job still carries a `STUB` comment worth checking before assuming it's fully wired up:
- `.github/workflows/ci.yml`: `e2e` job stays disabled (`if: false`) until a real multi-step user flow exists to test (deferred to Phase 1+, per its own comment).

The product/feature roadmap lives in `docs/FEATURE_PLAN.md` (phases 0–24, MVP-first sequencing and dependency rationale) and the locked-in technology choices live in `docs/TECH_STACKS.md` (stack table, key technical decisions, testing tools, CI/CD, observability). **Read both before implementing anything nontrivial** — `TECH_STACKS.md` is the authoritative source for decisions summarized below, including rationale that matters for future work (e.g. why FastEndpoints over MediatR, why a plain SPA over Next.js/Remix). The non-negotiable engineering rules distilled from both docs are ratified in `.specify/memory/constitution.md`.

## Commands

### Backend (`api/`, .NET 10 / C# 13)
```
dotnet restore api/OpenFarm.slnx
dotnet build api/OpenFarm.slnx --configuration Release
dotnet test api/OpenFarm.slnx                                    # all tests
dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~PingEndpointTests"   # single test/class
dotnet format api/OpenFarm.slnx --verify-no-changes               # CI lint check
dotnet format api/OpenFarm.slnx                                   # apply formatting
```
Tests need `ConnectionStrings__Default` set (see `.env.example`) since integration tests spin up against Postgres. `api/global.json` pins the SDK version — don't hand-edit without updating CI.

### Frontend (`web/`, Bun + React 19 + Vite)
```
cd web
bun install
bun run dev             # vite dev server
bun run build            # tsc -b && vite build
bun run test             # vitest run (all tests)
bunx vitest run <path/to/File.test.tsx>   # single test file
bun run lint              # biome check .
bun run format             # biome format --write .
bunx tsc -b                # typecheck only
bun run icons              # regenerate favicon/PWA/iOS icons in public/ from public/favicon.svg
```

### Local dev — `./dev.sh` (native, against a locally installed Postgres)
```
./dev.sh            # prints the subcommand list
./dev.sh api        # backend on :6868/:8889 (auto-migrates + seeds on startup)
./dev.sh web        # vite dev server on :5173
./dev.sh up         # both in one terminal, Ctrl-C stops both
./dev.sh slicers    # provision slicer bundles into ./.slicers for this platform
./dev.sh ingest     # ingest system profiles from the provisioned slicers
./dev.sh setup      # bun install + slicers + ingest (first-time setup)
./dev.sh reset      # drop DB schema + local keys/data/.slicers/node_modules
./dev.sh test       # dotnet test + vitest
./dev.sh fmt        # dotnet format + biome
```
`dev.sh` exists because `dotnet run --project` sets the process CWD to the *project* directory,
so `appsettings.Development.json`'s relative `SlicerBundle:RootDirectory` (`./.slicers`) resolves
to `api/src/OpenFarm.Api/.slicers` and misses the repo-root `.slicers/` that
`tools/provision-slicers` fills — the API then boots with no slicers available. The script exports
`SlicerBundle__RootDirectory=$PWD/.slicers` to anchor it. **Run the backend via `./dev.sh api`, not
a bare `dotnet run`.** (Docker is unaffected — compose sets that env var explicitly.)

### Production deploy
Single image (API serves the built SPA) — `deploy/Dockerfile`, `deploy/docker-compose.prod.yml`, published to `ghcr.io/getopenfarm/openfarm` by `.github/workflows/docker-publish.yml` on `X.Y.Z` tags (amd64 only; slicers are x86-64). See `deploy/README.md`.

### Local full stack (Docker)
```
cp .env.example infra/.env   # adjust as needed
cd infra && docker compose up   # postgres + api (dotnet watch) + web (bun run dev)
```

## Architecture

Monorepo: `api/` (ASP.NET Core solution) + `web/` (React SPA) + `infra/` (docker-compose, Dockerfiles) + `docs/`.

### Backend — Clean Architecture, four projects

- **`OpenFarm.Domain`** — entities, value objects, domain events, state machines. Framework-agnostic, no FastEndpoints references.
- **`OpenFarm.Application`** — commands/queries, handlers, validators, DTOs. Also framework-agnostic.
- **`OpenFarm.Infrastructure`** — EF Core (`OpenFarmDbContext`), repositories, the printer clients (Bambu LAN; Snapmaker U1 over Moonraker, `docs/SNAPMAKER_U1.md`), encryption, background jobs. Business logic (`Domain`/`Application`) never depends on this layer directly — it's the swappable edge (e.g. printer connectivity is defined as an `IPrinterConnector` contract so a future "local agent" relay can replace the direct-LAN implementation without touching `Application`/`Domain`).
- **`OpenFarm.Api`** — the web host: FastEndpoints endpoints, SignalR hubs, composition root. Endpoints are a thin adapter over `Application` commands/queries — HTTP concerns never leak into business logic.

Key backend decisions (rationale in `docs/TECH_STACKS.md` § Key technical decisions):
- **FastEndpoints**, not MediatR or raw minimal-API route groups, for the HTTP layer — REPR pattern (Request DTO → Handler → Response DTO), one class per endpoint, native FluentValidation wiring, per-endpoint OpenAPI generation. Uses FastEndpoints' own lightweight command bus instead of adding MediatR.
- Two cross-cutting concerns are FastEndpoints pre/post-processors: FluentValidation (pre) and an **activity-log post-processor** that writes to the shared `activity_log` table after any state-mutating command succeeds — this is what makes "every status change is logged" structural rather than convention.
- Errors surface uniformly as RFC 7807 `ProblemDetails`; domain-level failures are typed domain exceptions caught by global exception-handling middleware.
- **Single-tenant**: one farm per install, so there is no tenant/organization dimension in the schema, JWT, or hub groups. Farm-wide settings live in a single `FarmSettings` row.
- **RBAC is permission-based, not role-based** — a `permissions` catalog maps to roles via `role_permissions`, seeded with four roles (Owner/Admin/Operator/Viewer). Both API policies and frontend nav/page visibility check permissions, not role names, so adding a role is a seed-data change, not a code change. `role_scopes` (tag-based scoping for Operator/Viewer) exists in schema but is **not enforced** yet — deferred by design.
- `printers.status` and `jobs.status` are Postgres enums with explicit state machines enforced in `Domain` (invalid transitions throw), not free-text columns.
- **System slicer profiles are per-version catalogs, resolved strictly per pinned slicer**: `ingest-system-profiles` stores each bundled slicer leaf's profiles as their own catalog (`SourceVersion` = leaf directory name = `SlicerRef.Version`; a System row always has one — a DB CHECK enforces it — and User rows never do). A part is pinned to an exact engine + version. Production resolves every stored choice (printer machine/process, filament assignment, estimation defaults) to **the pinned slicer's own copy** through `ProfileResolver` (same `setting_id` + vendor + name across engines; id match with rename tolerance across versions of one engine) and **blocks — never substitutes — when there is no twin** (`ProfileResolutionOutcome`: `Missing`, `Ambiguous`, `CatalogNotLoaded`, `NoPin`, `UnsupportedOrigin`, …). Everything goes through the single funnel `ResolveSlicingProfileSelection`; pickers list only the farm default engine's own catalog (`ProfileCatalogScope.NativeFormat`); `GET /api/v1/slicing/readiness` runs the same checks to warn in the UI. "Same profile" comparisons use `ProfileIdentity`, never row ids. OrcaSlicer's `OrcaFilamentLibrary` bases are stored as hidden closure rows so library-inheriting filaments resolve. Rules and rationale: `docs/FILAMENT_PROFILE_ASSIGNMENTS.md` (Slicer resolution).
- **Production dispatch is fail-closed**: one resolution per dispatch (`ProductionProfileReadiness.ResolveAsync`), the file is generated from exactly those profiles (`JobFileResolver`), the configuration stamp is compared again right before sending, and every failure becomes a persisted block (`PrintJob.LastBlockReason`, retry backoff) instead of an exception that stalls the queue. Never add a fallback to another engine, version or profile.
- **Printer hardware facts live only in `PrinterModelCatalog`** (driver, extruder labels, feed unit style, camera, plates); protocol details live only in the driver's adapter. No brand/model checks anywhere else — ask the model (`PrinterModelCatalog.Find`) — and the web renders per-model behavior from `PrinterModelResponse`, never from brand strings. Connectors are picked by `PrinterModel.Driver`; an unknown model is refused, never defaulted to Bambu. Domain/Application name feed positions only by `FeedSlotRef` — Bambu tray ids live only in the Bambu adapter — and read nozzles per extruder (`Printer.ConfirmedNozzleMm`/`ConfirmedNozzlesMm`), never `ReportedNozzleDiameterMm`, which is display-only. Rules and checklists: `docs/PRINTER_HARDWARE_MODEL.md`.
- **Printer time lives only in printer activity**: the open stretch on the `Printers` row (`Activity`, `ActivitySinceUtc`), closed ones in `PrinterActivitySpans`, appended by the `Printer` mutators. Stats take every hour from it and never extrapolate an open print. Awaiting clear is entered only on a reported-state *change* to FINISH/FAILED, since printers re-send their last state on every connect. A print the printer ended unseen (back to IDLE, or seen only after a silence) is closed by `JobTelemetrySync` with an inferred outcome and end. Definitions and rules: `docs/STATS.md`.
- Long-running concerns (Bambu LAN connections, auto-reload threshold detection, maintenance-threshold checks) run as `BackgroundService`s inside `OpenFarm.Api` for Phase 1 — designed so they can later be extracted into a standalone worker host (required before running multiple API replicas) without a rewrite, because they only talk to `Application` through the same command bus the API uses.
- Real-time: one SignalR hub (`/hubs/farm`), a single `farm` hub group, pushes printer/job status changes to the dashboard.

### Frontend — plain SPA (deliberately not Next.js/Remix/TanStack Start)

- **Routing**: React Router v7 in library/declarative mode (client-side only), route-per-feature under `src/app`.
- **Server state**: TanStack Query exclusively for API data — no separate client cache/store for it.
- **Typed API client**: generated from the backend's OpenAPI spec via Orval into `src/lib/api/generated` (`useGetPrinters`, `useCreateJob`, etc.) — **never hand-edit generated files**; regenerate from the spec instead.
- **Forms**: react-hook-form + zod.
- **Add/Edit flows** follow a **Page-first** rule (routed `/<feature>/new` and `/<feature>/:id/edit` pages by default; modals only for confirmations, quick actions, and in-form pickers) — see `docs/UI_CONVENTIONS.md` for the decision rule and the per-flow migration status.
- **UI kit**: Tailwind CSS + shadcn/ui primitives in `src/components/ui/`.
- **Tables**: TanStack Table + TanStack Virtual (the job queue needs virtualization for "unlimited depth").
- **Real-time**: `@microsoft/signalr` client wired directly into TanStack Query's cache (`setQueryData`/invalidation on hub events), not a separate state store.
- Feature code lives one folder per feature under `src/features/` (printers, filament, jobs, parts, assemblies, maintenance, dashboard, settings).

### Testing strategy (see `docs/FEATURE_PLAN.md` guiding principles and `docs/TECH_STACKS.md` § Testing tools for full detail)

TDD is required for `Domain` state machines, BOM decomposition (recursive CTE + consuming C#), filament weight-tracking math, and the trickier FluentValidation gates — these are deterministic, well-specified logic where red-green-refactor pays off. TDD is explicitly *not* required (test-after instead) for orchestration-only Application handlers, EF Core repository/config code, the real Bambu LAN client (test via a fake/mock; the real one is tested against hardware), and React components/pages. Don't apply TDD uniformly — that's called out as counterproductive in this codebase.

- Backend unit: xUnit + FluentAssertions, targeting `Domain`/`Application`.
- Backend integration: xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql.
- Frontend: Vitest + React Testing Library.
- E2E: Playwright (not yet started — deferred until a real user flow exists).

## Conventions

- Conventional Commits for commit messages; PR titles follow the same convention (they become the squash-merge message).
- `.editorconfig` at repo root; `dotnet format` and Biome are enforced in CI, not just locally — run them before committing.
- C#: nullable reference types on, warnings-as-errors in Release builds. TypeScript: `strict: true`.
- EF Core migrations: one per PR when the schema changes, reviewed like code.
