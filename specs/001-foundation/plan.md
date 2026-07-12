# Implementation Plan: Foundation — Authenticated Access, Organization Scaffolding, Secured Credentials, Activity History, and a Base UI Kit

**Branch**: `001-foundation` | **Date**: 2026-07-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-foundation/spec.md`

## Summary

Phase 0 turns the bare scaffold (one `/api/v1/ping` endpoint, an empty `DbContext`, a
placeholder `DashboardPage`) into a working foundation: real JWT login for a single seeded
operator, an auto-created single `Organization` that every tenant-scoped record attaches to,
encrypted/masked/immutable printer connection credentials, a generic activity log capturing
every state-mutating action, and — the frontend half of this round's explicit scope — a
ready shadcn-based UI kit (app shell, nav, auth-gated routing, and enough primitives to build
a real screen) so every future feature phase composes existing pieces instead of inventing
new ones. Backend approach: four-project Clean Architecture with ASP.NET Core Identity
confined to `Infrastructure`, Finbuckle.MultiTenant wired with a static single-tenant
strategy (real global query filter, swappable resolution later), `IDataProtector` encryption,
and a globally-registered FastEndpoints activity-log post-processor. Frontend approach:
vendor the missing shadcn primitives, build `AppShell`/`ProtectedRoute`/router structure, and
prove it end-to-end with one real feature screen (connection secrets) built entirely from
those primitives against real Phase 0 endpoints.

## Technical Context

**Language/Version**: Backend C# 13 / .NET 10 (`api/global.json`). Frontend TypeScript
(`strict: true`) / React 19.2 / Vite / Bun 1.3.x.

**Primary Dependencies**: Backend (new this phase) — `Microsoft.AspNetCore.Identity.EntityFrameworkCore`,
`Finbuckle.MultiTenant` + `Finbuckle.MultiTenant.EntityFrameworkCore`,
`Microsoft.AspNetCore.Authentication.JwtBearer`, ASP.NET Core's built-in
`Microsoft.AspNetCore.DataProtection` (no new package). Frontend — no new npm packages;
`radix-ui`, `react-hook-form`, `zod`, `@hookform/resolvers`, `@tanstack/react-query`,
`@tanstack/react-table` are already installed (`web/package.json`); only shadcn primitive
files need generating via `bunx shadcn add`.

**Storage**: PostgreSQL 18 via EF Core 10 (`Npgsql.EntityFrameworkCore.PostgreSQL`), one
`OpenFarmDbContext`.

**Testing**: xUnit + FluentAssertions (backend unit), xUnit + `WebApplicationFactory` +
Testcontainers.PostgreSql (backend integration), Vitest + React Testing Library (frontend).
Playwright/E2E stays disabled this phase (see Complexity/STUB notes below).

**Target Platform**: Self-hosted Docker Compose (Postgres + API + static web build), per `infra/`.

**Project Type**: Web application — `api/` (4-project Clean Architecture solution) + `web/` (SPA).

**Performance Goals**: N/A this phase — no SignalR data yet (Phase 1), no unbounded lists
(activity log / connection secrets are paged, not virtualized).

**Constraints**: Nullable reference types + warnings-as-errors (Release) on the backend;
TypeScript `strict: true` on the frontend; every tenant-scoped table carries
`organization_id` from creation; connection secrets are immutable (create-new-to-replace only).

**Scale/Scope**: Single organization, single seeded operator, 5 backend endpoints
(`login`, `whoami`, `connection-secrets` create+list, `activity-log` list), 4 frontend
routes (`/login`, `/`, `/connection-secrets`, `/activity-log`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture, Enforced by the Compiler and CI** — PASS. ASP.NET Core Identity
  types (`ApplicationUser`, `UserManager`) are confined to `Infrastructure/Identity/`;
  `Domain`/`Application` never reference them. `Application` defines its own trivial
  `ICommandHandler<TCommand, TResult>` marker (not FastEndpoints' bus type) so it stays
  framework-agnostic; `Api` endpoints resolve handlers via DI and call `HandleAsync` directly.
- **II. Test Discipline Where It Pays, Not Uniformly** — PASS. Phase 0 has no Domain state
  machine (that's Phase 1's `PrinterStatus`), so the TDD-required surface this phase is
  `LoginCommandValidator` and `CreateConnectionSecretCommandValidator` (non-trivial
  FluentValidation gates, explicitly TDD-required by the constitution regardless of layer).
  Orchestration handlers, EF configs, the seeder, and React components are correctly
  test-after per the constitution's own carve-out.
- **III. Consistent User Experience Across the Fleet** — PASS. All forms use
  react-hook-form + zod against shadcn/ui primitives in `src/components/ui/`; all API errors
  surface as RFC 7807 `ProblemDetails` via `config.Errors.UseProblemDetails()`, rendered
  through one global `sonner` toast path; `[Permission(...)]` policy attributes are used from
  day one (permission strings, not role names) even though the Phase 0 handler only checks
  "authenticated."
- **IV. Build for Expansion, Not Speculation** — PASS. `organization_id` present on
  `ConnectionSecret`/`ActivityLogEntry` from day one; Finbuckle's static resolution strategy
  is swappable for claim-based later without a schema change (the JWT already carries an
  `org` claim it doesn't yet consume); `[Permission]` attribute takes a string now so Phase 4
  only changes the handler internals, not every endpoint.
- **V. Performance and Real-Time Responsiveness by Construction** — N/A / PASS by
  construction: no SignalR wiring yet, activity-log and connection-secret lists are paged
  (TanStack Virtual correctly deferred to Phase 9's job queue), the first-boot seeder is a
  bounded startup task, not a `BackgroundService` (it isn't long-running/connection-holding work).
- **Security & Multi-Tenancy Isolation** — PASS, explicitly verified: Finbuckle's real EF
  Core global query filter is used via `.IsMultiTenant()`, fed by `WithStaticStrategy(...)`
  instead of claim-based resolution — this is precisely what `docs/FEATURE_PLAN.md`'s Phase 0
  section and Principle IV pre-authorize, not a hand-rolled `WHERE` clause. `IDataProtector`
  encryption is real from the first commit (no fixture exemption). The activity-log
  post-processor is registered **globally** against all mutating verbs, not opt-in per
  endpoint. JWT auth is real from day one, including on `whoami`.

**No Complexity Tracking entries required** — no principle deviations this phase.

## Project Structure

### Documentation (this feature)

```text
specs/001-foundation/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
│   └── openapi.yaml
├── checklists/
│   └── requirements.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
api/
├── src/
│   ├── OpenFarm.Domain/
│   │   ├── Organizations/Organization.cs
│   │   ├── ConnectionSecrets/ConnectionSecret.cs
│   │   └── ActivityLog/{ActivityLogEntry.cs,ActorType.cs}
│   ├── OpenFarm.Application/
│   │   ├── Common/ICommandHandler.cs, IQueryHandler.cs
│   │   ├── Auth/{LoginCommand.cs,LoginCommandValidator.cs,WhoAmIQuery.cs}
│   │   ├── ConnectionSecrets/{CreateConnectionSecretCommand.cs,+Validator,ListConnectionSecretsQuery.cs}
│   │   └── ActivityLog/GetActivityLogQuery.cs
│   ├── OpenFarm.Infrastructure/
│   │   ├── Identity/{ApplicationUser.cs,ApplicationRole.cs}
│   │   ├── Multitenancy/{AppTenantInfo.cs,OrganizationTenantStore.cs}
│   │   ├── Security/DataProtectionSecretProtector.cs
│   │   ├── Auth/{IdentityAuthenticationService.cs,JwtTokenService.cs}
│   │   ├── ActivityLog/{EfActivityLogWriter.cs,EfActivityLogReader.cs}
│   │   ├── Persistence/{OpenFarmDbContext.cs,Configurations/*.cs,Migrations/}
│   │   └── Seed/FirstBootSeeder.cs
│   └── OpenFarm.Api/
│       ├── Endpoints/{Auth/,ConnectionSecrets/,ActivityLog/}
│       ├── Security/{PermissionAttribute.cs,PermissionAuthorizationHandler.cs}
│       ├── Processors/ActivityLogPostProcessor.cs
│       └── Program.cs
└── tests/
    ├── OpenFarm.Domain.Tests/
    ├── OpenFarm.Application.Tests/Auth/, ConnectionSecrets/   # TDD: validators
    └── OpenFarm.Api.IntegrationTests/                          # login, whoami gate, secrets, seeder idempotency

web/
├── src/
│   ├── components/ui/            # + input, label, form, card, dialog, table, sonner, sidebar (+sheet/tooltip/separator/skeleton), avatar, dropdown-menu
│   ├── app/
│   │   ├── layout/{AppShell.tsx,ProtectedRoute.tsx}
│   │   └── routes.tsx
│   ├── lib/
│   │   ├── api/{queryClient.ts,generated/}    # generated/ populated by Orval once un-stubbed
│   │   └── auth/{AuthProvider.tsx,useAuth.ts,tokenStorage.ts}
│   └── features/
│       ├── auth/LoginPage.tsx
│       ├── dashboard/DashboardPage.tsx        # existing, stays as landing page
│       ├── connection-secrets/ConnectionSecretsPage.tsx
│       └── activity-log/ActivityLogPage.tsx
└── orval.config.ts                             # un-stubbed this phase
```

**Structure Decision**: Web application (Option 2 shape), using the monorepo's existing
`api/` (4-project Clean Architecture) + `web/` (feature-folder SPA) split — no new
top-level directories introduced. All new backend code slots into the four existing
projects along their established layer boundaries; all new frontend code slots into the
existing `src/app`, `src/lib`, `src/features`, `src/components/ui` structure.

## Complexity Tracking

> No violations — table intentionally empty. See Constitution Check above for the one
> item that might look like a deviation (Finbuckle static strategy) and why it is not one.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
