# OpenFarm — Tech Stack & Technical Decisions

This is the standing reference for the technologies and notable technical decisions locked in for OpenFarm. Unlike `FEATURE_PLAN.md`, it is not a roadmap or build sequence — it documents *what* is used and *why*, independent of milestone order.

## Stack

| Layer | Choice | Version (as of this writing) |
|---|---|---|
| Backend runtime | .NET / ASP.NET Core | .NET 10 (LTS, supported through Nov 2028) |
| Backend language | C# | 13, nullable reference types enabled |
| API endpoints | FastEndpoints | 7.x |
| Auth / identity | ASP.NET Core Identity | ships with .NET 10 |
| Validation | FluentValidation | latest, via FastEndpoints' native integration |
| Database | PostgreSQL | 18.x |
| ORM / migrations | EF Core | 10.x |
| Frontend runtime & package manager | Bun | 1.3.x |
| Frontend framework | React | 19.2.x |
| Frontend build tool | Vite (via Bun) | latest |
| Frontend language | TypeScript | latest stable |
| Real-time | SignalR (server) + `@microsoft/signalr` (client) | matches .NET 10 |
| Containerization | Docker + Docker Compose | for local dev and self-hosted deploys |

Pin exact versions in `global.json` (.NET SDK) and `package.json`/`bun.lock`, and let Dependabot/Renovate keep them current — don't let this table go stale.

## Key technical decisions

- **FastEndpoints as the HTTP layer** — not MediatR, not raw minimal-API route groups. Each endpoint is its own class (Request DTO → Handler → Response DTO, the REPR pattern), FluentValidation wires in natively, and OpenAPI metadata is generated per endpoint automatically. FastEndpoints' own lightweight command bus is used instead of adding MediatR. Validation and activity-logging are implemented as FastEndpoints pre/post-processors rather than MediatR pipeline behaviors — the activity-log post-processor writes to a shared `activity_log` table after any state-mutating command succeeds, making "every status change is logged" structural rather than convention.
- **Single-tenant by design** — one farm per self-hosted install. There is no tenant/organization column, query filter, or per-tenant hub group; farm-wide settings live in a single `FarmSettings` row.
- **Permission-based RBAC, not role-based** — a `permissions` catalog maps to roles via `role_permissions`. Both API policies and frontend nav/page visibility check permissions, not role names, so adding a new role is a seed-data change, not a code change.
- **Status fields as Postgres enums with domain-enforced state machines, not free-text columns** — e.g. `printers.status`, `jobs.status`. Invalid transitions throw a typed domain exception rather than silently persisting bad state.
- **Recursive CTE for BOM/assembly decomposition, not application-side recursion** — keeps nested-assembly expansion correct and fast regardless of nesting depth.
- **Plain SPA, not Next.js/Remix/TanStack Start** — OpenFarm is an auth-gated operational dashboard with no SEO or public-content surface. A full-stack framework's server layer would sit unused or invite duplicating business logic outside the .NET API. A plain SPA also builds to static files, keeping self-hosting simple and avoiding a third Node-SSR tier. Routing is React Router v7 in library/declarative mode (client-side only).
- **Orval-generated typed API client, never hand-edited** — generated from the backend's OpenAPI spec into typed TanStack Query hooks (`useGetPrinters`, `useCreateJob`, etc.). A CI contract-drift check regenerates and diffs the client so backend/frontend types cannot silently drift.
- **SignalR for real-time, wired directly into TanStack Query's cache** — a single hub with one `farm` group, pushing printer/job status changes to clients. Cache updates happen via `setQueryData`/invalidation on hub events rather than a separate real-time state store.
- **Printer connectivity behind an internal contract** — an `IPrinterConnector`-style boundary (domain events like `PrinterStatusChanged`, `AmsSlotChanged`, plus outbound commands) that `Application`/`Domain` consume without knowing the transport. The direct-LAN implementation is the only one that exists today, but because business logic only ever sees the contract, a different transport implementation can be swapped in later without reworking `Application`/`Domain`.

## Testing tools

| Layer | Tooling | What it covers |
|---|---|---|
| Backend unit | xUnit + FluentAssertions | Domain state machines, BOM decomposition, weight-tracking math, validators |
| Backend integration | xUnit + `WebApplicationFactory` + Testcontainers.PostgreSql | Real HTTP → real Postgres, run in CI against an ephemeral container |
| Frontend unit/component | Vitest + React Testing Library | Feature components, forms, hooks |
| End-to-end | Playwright | Critical flows, auth boundaries |
| Contract | CI check | Generated Orval client is regenerated and diffed against committed output — fails the build if frontend types drifted from the API |

## CI/CD & security tooling

- GitHub Actions, one workflow per PR with parallel jobs.
- Lint: `dotnet format --verify-no-changes`, Biome (lint + format) for the frontend.
- Vulnerability scanning: `dotnet list package --vulnerable`, `bun audit`.
- Secret scanning: Gitleaks.
- Dependency updates: Dependabot/Renovate.
- Release: pushing an `X.Y.Z` tag re-runs CI, then builds and pushes the single `deploy/Dockerfile` image (linux/amd64) to `ghcr.io/getopenfarm/openfarm`.

## Observability

- Structured logging via Serilog (JSON), with a request-correlation ID on every log line.
- Health check endpoints (`/health/live`, `/health/ready`) for container orchestration.
- OpenTelemetry instrumentation scaffolded from the start (traces/metrics exporters no-op'd until a collector exists), so adding real observability later is configuration, not code changes.
