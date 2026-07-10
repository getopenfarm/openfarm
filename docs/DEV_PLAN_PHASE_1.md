# OpenFarm — Phase 1 Development Plan

Status: Draft · Scope: Phase 0 (Foundation) + Phase 1 (MVP) from `FEATURE_PLAN.md`

## 1. Purpose

This document translates `FEATURE_PLAN.md`'s Phase 0 and Phase 1 into an engineering plan: stack versions, system architecture, data model, coding/testing/security standards, and a sequenced build order with a definition of done per milestone. It is the reference contributors should follow so the codebase stays consistent as the team and feature set grow.

## 2. Tech stack

| Layer | Choice | Version (as of this writing) |
|---|---|---|
| Backend runtime | .NET / ASP.NET Core | .NET 10 (LTS, supported through Nov 2028) |
| Backend language | C# | 13, nullable reference types enabled |
| API endpoints | FastEndpoints | 7.x |
| Auth / identity | ASP.NET Core Identity | ships with .NET 10 |
| Multi-tenancy | Finbuckle.MultiTenant (+ `.AspNetCore`, `.EntityFrameworkCore`) | 10.x |
| Validation | FluentValidation | latest, via FastEndpoints' native integration |
| Database | PostgreSQL | 18.x |
| ORM / migrations | EF Core | 10.x |
| Frontend runtime & package manager | Bun | 1.3.x |
| Frontend framework | React | 19.2.x |
| Frontend build tool | Vite (via Bun) | latest |
| Frontend language | TypeScript | latest stable |
| Real-time | SignalR (server) + `@microsoft/signalr` (client) | matches .NET 10 |
| Containerization | Docker + Docker Compose | for local dev and self-hosted deploys |

Pin exact versions in `global.json` (.NET SDK) and `package.json`/`bun.lock` once the repo is scaffolded, and let Dependabot/Renovate keep them current — don't let this table go stale.

## 3. Repository structure

Single monorepo, two top-level app roots plus shared docs/infra:

```
openfarm/
  docs/
  api/                        # ASP.NET Core solution
    src/
      OpenFarm.Api/            # Web host: endpoints, SignalR hubs, composition root
      OpenFarm.Application/    # Use cases: commands/queries, handlers, validators, DTOs
      OpenFarm.Domain/         # Entities, value objects, domain events, state machines
      OpenFarm.Infrastructure/ # EF Core, repositories, Bambu client, encryption, background jobs
    tests/
      OpenFarm.Domain.Tests/
      OpenFarm.Application.Tests/
      OpenFarm.Api.IntegrationTests/
    OpenFarm.sln
  web/                         # React app
    src/
      app/                     # routing, providers, layout shell
      features/                # one folder per feature (printers, filament, jobs, parts, assemblies, maintenance, dashboard, settings)
      components/ui/           # shared design-system primitives (shadcn/ui)
      lib/
        api/generated/         # OpenAPI-generated types + React Query hooks (do not hand-edit)
        auth/
    tests/
    package.json
  infra/
    docker-compose.yml         # postgres, api, web for local dev
    docker/                    # Dockerfiles
  .github/workflows/
```

Rationale: separating `Domain`/`Application` from `Infrastructure` means the Bambu MQTT client, encryption, and EF Core can all be swapped or extracted into a standalone worker process later (see §6) without touching business logic — important given the plan's own multi-tenant SaaS trajectory in Phase 3.

Both `src/` folders are earning their keep, not just convention for its own sake: `api/src/` holds four separate projects (`Api`, `Application`, `Domain`, `Infrastructure`) as siblings of `api/tests/`, keeping solution-root files (`.sln`, `Directory.Build.props`, `nuget.config`) uncluttered — standard layout once a .NET solution has more than one project. `web/src/` is Vite/React's own convention (`index.html` and config live at `web/` root, application code under `src/`) — every tool in that ecosystem (path aliases, HMR, build config) assumes it's there, so it's not really optional.

## 4. Backend architecture

**Pattern:** Clean Architecture, with **FastEndpoints** as the HTTP layer instead of hand-rolled minimal API route groups. Raw minimal APIs get unwieldy once you're past a handful of routes — this project has 15+ entities each with CRUD plus custom actions ("mark ready," "queue job," "mark spool finished"). FastEndpoints gives each endpoint its own class (Request DTO → Handler → Response DTO, the REPR pattern), wires FluentValidation in natively, generates OpenAPI metadata per endpoint/group automatically, and ships integration-test helpers (`AppFixture`, typed HTTP client) that pair well with the TDD approach in §8a.

FastEndpoints also has its own lightweight command bus (`Command<TResult>` + handler), which is used here **instead of** MediatR — one less library doing an overlapping job. Two cross-cutting concerns are implemented as FastEndpoints pre/post-processors rather than MediatR pipeline behaviors:

1. **Validation** — FluentValidation validators run automatically before the endpoint handler; failures return a structured `ProblemDetails` (RFC 7807) response.
2. **Activity log post-processor** — after a command that mutates state succeeds, writes an entry to the shared `activity_log` table from the endpoint's declared entity/event metadata. This keeps activity logging centralized instead of scattered `_activityLog.Write(...)` calls, and guarantees Phase 0's "every status change is logged" requirement is structurally enforced rather than relied on by convention.

`Domain` and `Application` stay framework-agnostic (plain C#, no FastEndpoints references) — endpoints in `OpenFarm.Api` are a thin adapter over `Application` commands/queries, so the HTTP framework choice never leaks into business logic.

**API surface:** FastEndpoints groups mapped by feature (`/api/printers`, `/api/spools`, `/api/jobs`, …), versioned from day one (`/api/v1/...`) even though there's only one version now. OpenAPI spec generated via FastEndpoints' Swagger integration, served at `/swagger/v1/swagger.json`, and consumed by the frontend's codegen (§7).

**Error handling:** all errors surface as `ProblemDetails`. Domain-level failures (e.g., "printer must have a profile before receiving a job") are modeled as typed domain exceptions caught by a global exception-handling middleware and mapped to 4xx `ProblemDetails`; unexpected exceptions map to a generic 500 with no internal detail leaked, logged with full context server-side.

## 5. Data model & multi-tenancy

**Strategy:** shared database, shared schema, tenant discriminator — implemented with **Finbuckle.MultiTenant** rather than hand-rolled query filters. Finbuckle resolves the current tenant per-request (strategy: claim-based, reading `organization_id` off the auth token — Finbuckle supports composing additional strategies like host/header later if the hosted SaaS tier ever needs subdomain-per-tenant routing), and its EF Core integration (`Finbuckle.MultiTenant.EntityFrameworkCore`) applies a named global query filter to every multi-tenant entity automatically, plus stamps `organization_id` on insert. This is the standard, battle-tested library for this pattern in .NET — hand-rolling it is exactly the kind of thing that produces a subtle tenant-isolation bug, which here would mean one farm seeing another farm's printers. Postgres row-level security is still worth adding later as defense-in-depth for the hosted tier, but Finbuckle's filter is the primary enforcement mechanism for Phase 1.

Self-hosted installs get exactly one `Organization` row created at first boot, invisibly, so solo operators never see multi-tenant UI. Database-per-tenant is deliberately out of scope for now — premature operational overhead for an MVP; shared-schema-plus-filter satisfies "no redesign needed" without over-building.

**Roles & permissions:** built on **ASP.NET Core Identity** (`UserManager`/`RoleManager`), not implemented from scratch — but access is checked by **permission**, not by role name directly, so page- and action-level access is a data question, not a code question. A `permissions` catalog (e.g. `printers.manage`, `filament.manage`, `jobs.manage`, `maintenance.manage`, `parts.manage`, `settings.manage`, `users.manage`) maps to roles via a `role_permissions` join table, seeded at startup with FEATURE_PLAN.md's four roles:

| Role | Typical access |
|---|---|
| Owner | All permissions, incl. org settings, billing (Phase 3), user/role management |
| Admin | All operational permissions (fleet, filament, jobs, parts, maintenance); not org-level settings |
| Operator | Day-to-day operational actions: update printer/job status, load/unload spools, log maintenance; no profile/user management |
| Viewer | Read-only across all operational pages |

The user's permission set is resolved at login and exposed both to the API (policy-based authorization, `RequirePermission("jobs.manage")`) and to the frontend (`/me` returns the permission list, which drives which nav items/pages/buttons render — the frontend checks permissions, not role names, so it doesn't need updating if a role's permissions change). Because roles and permissions are data, not hardcoded checks, adding a role later (e.g. a distinct "Engineer" role with maintenance-only access) is a seed-data change, not a code change — relevant given Phase 3's plan to let hosted-tier organizations manage their own roles.

Finbuckle ships an Identity integration package so user stores are tenant-aware automatically — permissions are still evaluated within the current tenant.

**Deferred:** resource-level tag-scoping (restricting an Operator/Viewer to specific printer tags rather than the whole farm) is *not* built in M0. FEATURE_PLAN.md itself describes this as optional ("Operator and Viewer *can optionally* be scoped"), so it's designed in but not implemented yet — the `role_scopes` and `printer_tags` tables exist in the schema so it can be added later as an additional filter on top of the permission check above, without a data model change.

**Core Phase 0 tables:**

| Table | Notes |
|---|---|
| `organizations` | Auto-created singleton for self-hosted installs; Finbuckle's `TenantInfo` store |
| `users`, `roles`, `user_roles` | ASP.NET Core Identity tables, tenant-scoped via Finbuckle's Identity integration |
| `permissions`, `role_permissions` | Permission catalog and role → permission mapping; drives both API policies and frontend page/nav visibility (see Roles & permissions below) |
| `role_scopes` | **Deferred, schema only.** Would link Operator/Viewer assignments to specific printer tags; not enforced in M0 |
| `printer_tags` | Reused for scoping (once `role_scopes` is enforced), zones, and later circuit grouping |
| `connection_secrets` | Polymorphic (`owner_type`, `owner_id`, `brand`, encrypted payload) — encrypted via ASP.NET Core Data Protection API, keyed by an install-generated key ring for self-hosted (documented upgrade path to KMS/Key Vault for hosted, per FEATURE_PLAN's open decision) |
| `activity_log` | `entity_type`, `entity_id`, `event_type`, `actor_user_id`, `organization_id`, `timestamp`, `metadata jsonb` |

**Core Phase 1 tables** (one per FEATURE_PLAN.md section 1–12): `printers`, `printer_feed_configs`, `spools`, `filament_profiles`, `filament_vendors`, `printer_profiles`, `parts`, `part_revisions`, `sliced_files`, `assemblies`, `assembly_components` (self-referencing for nested BOMs), `jobs`, `job_sub_jobs`, `job_filament_usage`, `maintenance_task_templates`, `maintenance_logs`.

Notes worth calling out during implementation:

- `assembly_components` self-references `assemblies` to support nesting; BOM expansion for job decomposition uses a recursive CTE, not application-side recursion, to keep it correct and fast as nesting depth grows.
- `printers.status` and `jobs.status` are modeled as Postgres enums with an explicit state machine enforced in the `Domain` layer (invalid transitions throw a typed domain exception) — not just a free-text column.
- `job_filament_usage` is a separate table (job → spool → grams), not a single total on `jobs`, per the plan's multi-spool AMS attribution requirement.

**Migrations:** EF Core Migrations, one per PR when the schema changes, reviewed like code. CI runs `dotnet ef migrations has-pending-model-changes` to catch drift, and applies migrations against a throwaway Postgres container as part of the integration test run.

## 6. Background processing & real-time

Phase 1 needs three long-running concerns: Bambu LAN connections (per-printer, persistent), auto-reload weight-threshold detection, and maintenance-threshold checks. All three run as `IHostedService`/`BackgroundService` instances inside `OpenFarm.Api` for Phase 1 — no separate worker process yet. Because they live in `Infrastructure` and talk to `Application` only through the same command bus the API uses, extracting them into a standalone worker host later (needed once hosted multi-tenant SaaS requires per-tenant connection isolation, per §14) is a deployment change, not a rewrite.

**Printer connector boundary — designed now for the future local-agent split.** Self-hosted OpenFarm runs on the same LAN as the printers, so the connector can open a direct connection. A future hosted SaaS backend runs in the cloud and has no route into a customer's home/office LAN — it will need a **local connector agent** installed on the customer's network that dials outward to OpenFarm's backend (avoiding any inbound firewall/NAT configuration on the customer's side) and relays printer connectivity, the same pattern used by Tailscale, GitHub's self-hosted runners, or the Datadog agent. This isn't Phase 1 scope (it belongs in Phase 3, alongside full multi-tenant SaaS — see FEATURE_PLAN.md), but it changes how the printer connector should be shaped today so it isn't rebuilt later:

- Define printer connectivity as an internal contract (e.g. an `IPrinterConnector`/domain-event boundary: `PrinterStatusChanged`, `AmsSlotChanged`, `JobStartedDetected`, and outbound `SendJob`/`MarkReady` commands) that `Application`/`Domain` consume without knowing or caring where the event originated.
- Phase 1's `BambuLanConnector` fulfills that contract by talking directly to the printer over LAN — the only implementation needed now.
- A future agent-relayed implementation fulfills the *same* contract, except events arrive over a network channel from a remote agent instead of a local MQTT subscription, and commands are relayed outward the same way. Because `Application`/`Domain` only ever see the contract, not the transport, this is an additional `Infrastructure` implementation, not a rework of business logic.
- The natural transport for that future channel is another SignalR connection — the agent authenticates as a machine principal (an org-scoped API key, not a user JWT) and acts as a SignalR client, invoking hub methods to push telemetry and receiving invoked hub methods as commands. This reuses infrastructure already in the plan rather than introducing a second protocol (MQTT broker, gRPC, etc.) purely for this. Machine credentials and their (narrow) permission set are a Phase 3 addition to the permission catalog in §5, not built now.
- Practical implication for M3 (§13): implement the Bambu connector behind the contract above from the start, even though only the direct-LAN implementation exists in Phase 1.

**Real-time updates:** a single SignalR hub (`/hubs/farm`) pushes printer status changes, awaiting-clear transitions, and job status updates to the dashboard. Clients subscribe per organization (tenant-scoped hub groups).

## 7. Frontend architecture

**Deliberately a plain SPA, not a full-stack React framework** (Next.js, Remix, TanStack Start). OpenFarm is an auth-gated operational dashboard with no SEO or public-content surface, and it already has a real backend (§4–§6) — a full-stack framework's server layer would either sit unused or invite duplicating business logic outside the .NET API, undermining the single-typed-backend approach in this plan. A plain SPA also builds to static files, which keeps the self-hosting story simple (servable by nginx, or even by the API itself) and avoids adding a third, Node-SSR tier to the Kubernetes picture in §14. Revisit only if a genuinely public, SEO-relevant surface (marketing site, public status page) is ever needed — and even then, that would be a separate small site, not a rewrite of the app.

- **Routing:** React Router v7 in **library/declarative mode** (client-side only — not its "framework mode," which behaves like Remix with server loaders/actions), route-per-feature under `src/app`.
- **Server state:** TanStack Query exclusively for anything from the API — no bespoke Redux-style client cache for server data.
- **Typed API client:** generated from the backend's OpenAPI spec via Orval, producing typed TanStack Query hooks directly (`useGetPrinters`, `useCreateJob`, etc.) in `lib/api/generated`. This is regenerated as a CI/build step, never hand-edited, so backend and frontend types cannot silently drift.
- **Forms:** react-hook-form + zod, with zod schemas mirroring backend FluentValidation rules where practical.
- **UI kit:** Tailwind CSS + shadcn/ui primitives, given the app is fundamentally table/form/dashboard-heavy (printers, spools, parts, jobs, maintenance).
- **Tables:** TanStack Table for the printer fleet, filament inventory, part library, and the job queue — the queue in particular needs virtualization (TanStack Virtual) to stay fast at "unlimited depth."
- **Real-time:** `@microsoft/signalr` client wired into TanStack Query's cache (`queryClient.setQueryData`/invalidation on hub events) rather than a separate state store.

## 8. Testing strategy

| Layer | Tooling | What it covers |
|---|---|---|
| Backend unit | xUnit + FluentAssertions | Domain state machines, BOM decomposition, weight-tracking math, validators |
| Backend integration | xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql | Real HTTP → real Postgres, run in CI against an ephemeral container |
| Frontend unit/component | Vitest + React Testing Library | Feature components, forms, hooks |
| End-to-end | Playwright | Critical flows: create printer → add spool → create job → send to printer; org-scoped auth boundaries |
| Contract | CI check | Generated Orval client is regenerated and diffed against committed output — fails the build if the frontend types drifted from the API |

Target: meaningful coverage on `Domain` and `Application` (business rules), not a coverage percentage chased for its own sake.

### 8a. Where TDD applies, and where it doesn't

TDD is required where it pays off — deterministic, well-specified business logic — and treated as optional-to-counterproductive where it doesn't. Applying it uniformly everywhere (including simple CRUD and UI) tends to produce brittle, low-value tests written to satisfy the process rather than the design; the goal here is judgment, not dogma.

**TDD required (write the failing test first):**

- `Domain` state machines — printer status (`idle → printing → awaiting_clear → idle`), job status, spool lifecycle. Each valid/invalid transition is a natural test case to write before the transition method exists.
- BOM decomposition (recursive CTE + the C# that consumes it) — nesting depth, quantity multiplication, and cycle-prevention all have clear expected outputs, making red-green-refactor genuinely fast here.
- Filament weight-tracking math (remaining weight from cumulative job usage, auto-reload threshold detection, per-spool job usage attribution) — pure functions with unambiguous expected results.
- Validators (FluentValidation rules) for the trickier gates: "job requires a sliced file matching the printer profile," "assembly cannot reference itself," etc.

**Test-after, not TDD (write the implementation, then cover it):**

- Application-layer commands/handlers that are mostly orchestration (load entity, check permission, save) — TDD-ing a straightforward CRUD handler mostly produces a test that re-states the implementation.
- Infrastructure — EF Core repositories and configuration are covered by integration tests once written; TDD-ing an ORM mapping adds ceremony without catching more bugs.
- The Bambu LAN client — write a fake/mock implementation first (interface-first, which *is* a TDD-adjacent practice worth keeping) so `Application` and background services can be developed and tested against it, but the real client is necessarily tested against actual hardware/manually, not red-green.
- React components and pages — component tests are written alongside or just after the component, once its shape has settled; TDD-ing rapidly-iterated UI markup tends to churn tests as fast as markup.
- Recursive BOM *display* logic on the frontend (flattening a tree for the table view) is a reasonable exception worth TDD-ing, since it's pure logic with clear inputs/outputs, same reasoning as the backend BOM case.

**E2E** is written after a milestone's slice is functionally complete — it's a regression safety net over a finished flow, not a design tool.

## 9. CI/CD

GitHub Actions, one workflow per PR with parallel jobs:

1. **Lint** — `dotnet format --verify-no-changes`, Biome (lint + format) for the frontend.
2. **Backend build & test** — spins up a Postgres service container, runs unit + integration tests, applies migrations.
3. **Frontend build & test** — `bun install --frozen-lockfile`, Vitest, type-check (`tsc --noEmit`), Orval contract-drift check.
4. **E2E** — Playwright against a docker-compose stack, on PRs touching `api/` or `web/`.
5. **Security** — `dotnet list package --vulnerable`, `bun audit`, Gitleaks secret scan.

Required checks before merge: all of the above green + 1 approving review. `main` is always deployable; releases are tagged, triggering a Docker image build/push for both `api` and `web` images.

**Local dev:** `docker-compose up` brings up Postgres + API (`dotnet watch`) + web (`bun run dev`). `.env.example` documents required variables; backend secrets for local dev use `dotnet user-secrets`, never `appsettings.json`.

## 10. Security baseline

- HTTPS everywhere, including local dev certs.
- CORS locked to the known frontend origin(s); no wildcard.
- ASP.NET Core's built-in rate limiter on auth endpoints.
- All input validated server-side via FluentValidation regardless of frontend validation.
- EF Core parameterizes everything by default — no raw SQL string concatenation.
- Connection secrets encrypted at rest via Data Protection API (§5); never logged, never included in default backup exports (per FEATURE_PLAN.md §12).
- Least-privilege Postgres roles: a migration role (DDL) separate from the application's runtime role (DML only).
- Dependabot/Renovate for dependency updates; vulnerability scans in CI (§9).
- Gitleaks in CI to prevent committed secrets.

## 11. Coding standards & workflow

- `.editorconfig` at repo root; `dotnet format` and Biome enforced in CI, not just locally.
- C#: nullable reference types on, analyzers on, warnings as errors in Release builds.
- TypeScript: `strict: true`.
- Conventional Commits for commit messages; PR titles follow the same convention since they become the squash-merge commit message.
- Trunk-based development: short-lived feature branches off `main`, PRs required, no direct pushes to `main`.
- CODEOWNERS for `api/` vs `web/` review routing once the contributor base grows beyond one or two people.

## 12. Observability

- Structured logging via Serilog (JSON), with a request-correlation ID on every log line.
- Health check endpoints: `/health/live`, `/health/ready` (the latter checks DB connectivity) — needed for container orchestration health checks even in a single-node self-hosted deploy.
- OpenTelemetry instrumentation scaffolded from the start (traces/metrics exporters no-op'd until a collector exists) so adding real observability later is configuration, not code changes.

## 13. Milestones (sequenced build order)

Order follows FEATURE_PLAN.md's own dependency chain: printers and filament can be tracked before profiles exist; profiles gate jobs; jobs are the most complex piece and come last among the "core" features.

| # | Milestone | Key deliverable | Definition of done |
|---|---|---|---|
| M0 | Foundation | Repo scaffold, CI/CD, auth (Identity + JWT), permission-based RBAC (four seeded roles, permission catalog gating pages/actions; tag-scoping schema present but not enforced), encrypted `connection_secrets`, `activity_log` pipeline behavior | A new self-hosted instance boots with a single auto-created org, an Owner can log in, `/me` returns the correct permission set per role, and a dummy mutation writes an activity log entry |
| M1 | Fleet management | Printer CRUD, status enum incl. `awaiting_clear`, feed configuration (single/AMS/auto-reload/combo), tags | Printer status transitions are enforced by the domain state machine and logged; "Mark ready" moves `awaiting_clear` → `idle` |
| M2 | Filament tracking | Spool CRUD, three-state lifecycle, remaining-weight estimation from job usage | Removing a spool shows live remaining weight; state transitions match FEATURE_PLAN.md's rules exactly |
| M3 | Live Bambu integration | LAN connection per printer, AMS slot visibility, auto status detection, built behind the connector contract described in §6 | A connected Bambu printer's status and loaded AMS slots reflect in the dashboard within seconds via SignalR; connector logic reachable only through the `IPrinterConnector` contract, not called directly from `Application` |
| M4 | Printer profiles | Profile CRUD, starter set, import from slicer data, assignment gate on printers | A printer without a profile cannot be sent a job; error surfaces clearly in the UI |
| M5 | Filament profiles | Profile CRUD incl. color inheritance from base profile, assignment gate on spools, AMS cross-check | A spool without a profile cannot be used in a job; AMS contents mismatched against profile catalog raise a dashboard warning |
| M6 | Part library | Part CRUD with file/thumbnail/material/estimates, revision tracking that flags (not invalidates) sliced files | Revising a part's source model flags dependent sliced files as stale without deleting them |
| M7 | Sliced files | Upload tied to printer profile + filament profile, parsed estimates with manual fallback | A part can have multiple sliced files, one per printer profile; job creation can find the right one |
| M8 | Assembly / BOM | Assembly CRUD, nested assemblies via self-referencing components, recursive expansion | A 3-level nested assembly expands to the correct flat part list and quantities via the recursive CTE |
| M9 | Jobs / print queue | Job CRUD incl. unlimited quantity, BOM decomposition into sub-jobs, sliced-file locking at queue time, per-spool filament usage breakdown, manual status workflow, virtualized queue UI | Queuing a job against an assembly produces correctly-quantified sub-jobs; a queued job missing a sliced file is flagged, not blocked; the queue stays responsive at a few thousand entries |
| M10 | Maintenance tracking | Task templates (interval/hours/count), manual logs, threshold alerts | Crossing a configured threshold raises a dashboard alert |
| M11 | Dashboard | At-a-glance view: statuses, active jobs, low-filament, maintenance due | Dashboard reflects live SignalR updates without a manual refresh |
| M12 | Backup & restore | Full org export/import, credentials excluded by default with optional passphrase-encrypted mode | A fresh install restored from an M1–M11 export reproduces the farm's data exactly, minus credentials unless explicitly included |

Each milestone ends with its own integration + e2e tests before moving on — this list is also the PR/branch breakdown, not just a planning artifact.

## 14. Horizontal scaling readiness (Kubernetes)

Not needed for Phase 1 MVP — the target is self-hosted single-node — but worth designing for now, since retrofitting statelessness later is more expensive than being deliberate about it today. The layered architecture (`Domain`/`Application` isolated from `Infrastructure`, stateless JWT auth, Postgres as the only real shared state, health-check endpoints that already match K8s liveness/readiness probes) is horizontally-scalable by design. That said, three concrete gaps must be closed before running multiple **API** replicas behind a load balancer. None are rewrites — all are configuration or extraction changes already anticipated elsewhere in this plan:

1. **SignalR needs a backplane.** By default, each API instance keeps its SignalR connections in memory — a status update handled by pod A never reaches a dashboard client connected to pod B. Fix: add a Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`), a standard, well-documented addition.
2. **Background services must move out of the API process.** §6 currently runs the Bambu LAN listeners and threshold-check loops as `BackgroundService`s inside `OpenFarm.Api` — fine for one instance, but N API replicas means N duplicate connections to the same printer and N duplicate writes. §6 already anticipated this ("extracting them into a standalone worker host later... is a deployment change, not a rewrite") — that extraction is a **hard prerequisite** for multi-replica API, not an optional nice-to-have. The worker deployment itself stays single-replica (or leader-elected), since per-printer connections aren't embarrassingly parallel work to begin with.
3. **Data Protection keys need shared storage.** ASP.NET Core's Data Protection API persists its key ring to local disk by default. In K8s, each pod has its own ephemeral filesystem — pod A can't decrypt a `connection_secrets` row encrypted by pod B, and keys are lost on every pod restart. Fix: persist keys to Postgres (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`) or a KMS. This is the same fix already flagged under §15's key-management open decision — running multiple instances is what turns it from "nice to have eventually" into a hard requirement.

Two more worth doing at that point, but not correctness-breaking on day one:

- **Rate limiting** — the built-in limiter is in-memory per instance by default, so a client can get N× the intended limit by spreading requests across pods. Move to a Redis-backed limiter once multi-instance.
- **Migrations** — must run as a one-off step (CI/CD deploy step or a K8s `Job`) before pods roll out, never triggered from each pod's own startup, to avoid replicas racing each other against the schema.

The **frontend** needs none of this: the built Vite output is static assets served by nginx (or similar), inherently stateless, and scales horizontally behind a load balancer with zero architecture changes.

## 15. Carried-forward open decisions

These are called out in FEATURE_PLAN.md's "Open decisions" and are relevant to Phase 1 implementation choices; flagged here so they aren't lost:

- Key management for `connection_secrets` starts as an install-generated Data Protection key ring; the hosted multi-tenant version will need to move to a KMS-backed, likely per-tenant key — worth confirming the migration path before M0 locks in the key storage format.
- License model for eventual public release should be decided before the first public commit, independent of this plan.
- Default printer/filament profile data sourcing (bundled vs. import-only) affects M4/M5 scope — confirm before starting those milestones.
- `activity_log` retention/aggregation strategy is fine as raw events through Phase 1; revisit before the table grows large.
- Local connector agent (needed for hosted SaaS printer connectivity, see FEATURE_PLAN.md Phase 3 and §6 above): transport is proposed as SignalR with a machine-credential principal, but the actual credential format, provisioning flow (how a customer installs/pairs an agent with their org), and rotation policy aren't decided — not needed for MVP, but the connector contract in §6 should be implemented in M3 regardless, since it costs nothing now and avoids rework later.
