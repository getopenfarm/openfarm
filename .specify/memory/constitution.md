<!--
Sync Impact Report
- Version change: (unratified template) → 1.0.0
- Rationale: Initial ratification. The constitution had never been filled in — every placeholder token
  was still present. This is a MAJOR bump per template rule ("Initial adoption").
- Modified principles: none (first-time fill, nothing renamed)
- Added principles:
  - I. Clean Architecture, Enforced by the Compiler and CI
  - II. Test Discipline Where It Pays, Not Uniformly
  - III. Consistent User Experience Across the Fleet
  - IV. Build for Expansion, Not Speculation
  - V. Performance and Real-Time Responsiveness by Construction
- Added sections:
  - Security & Multi-Tenancy Isolation
  - Development Workflow & Quality Gates
- Removed sections: none
- Templates requiring updates:
  - .specify/templates/plan-template.md — ✅ no change needed (Constitution Check section is a generic
    pointer, doesn't hardcode principle names)
  - .specify/templates/spec-template.md — ✅ no change needed (no constitution-specific references)
  - .specify/templates/tasks-template.md — ✅ no change needed (no constitution-specific references)
  - .claude/skills/speckit-*/SKILL.md — ✅ no change needed (generic command files)
  - CLAUDE.md — ⚠ pending drive-by fix (stale docs/DEV_PLAN_PHASE_1.md reference), applied separately
    in the same session, not part of this file
- Follow-up TODOs: none deferred; no placeholders left unfilled.
-->

# OpenFarm Constitution

## Core Principles

### I. Clean Architecture, Enforced by the Compiler and CI

`Domain` and `Application` MUST stay framework-agnostic — no `FastEndpoints`, no `EF Core`, no HTTP
concerns anywhere in those two projects. `Infrastructure` is the only swappable edge (printer
connectivity, encryption, background jobs), reached exclusively through contracts like
`IPrinterConnector` so a transport can be replaced without touching business logic. `Api` is a thin
FastEndpoints adapter over `Application` commands/queries — one class per endpoint (REPR pattern),
nothing else. Nullable reference types are enabled and warnings are errors in Release builds; TypeScript
runs under `strict: true`. `dotnet format --verify-no-changes` and Biome (`lint`/`format`) are enforced
in CI as merge gates, not left as local-only convention.

Rationale: this layering is what lets the direct-LAN Bambu connector be replaced later by a relay-agent
transport (Phase 24 of the feature plan) without rewriting `Application` or `Domain` — the boundary only
pays off if nothing on the business-logic side of it ever depends on what's across it.

### II. Test Discipline Where It Pays, Not Uniformly

TDD (red-green-refactor) is NON-NEGOTIABLE for deterministic, well-specified logic: `Domain` state
machines (printer/job status transitions), BOM/assembly recursive decomposition, filament
weight-tracking math, and non-trivial FluentValidation gates. Test-after or fake/fixture-based testing
is REQUIRED INSTEAD — not merely tolerated — for orchestration-only `Application` handlers, EF Core
repository/configuration code, the real Bambu LAN connector (tested via fixture-replay against a
`FakePrinterConnector`; validation against physical hardware is manual, never a CI gate), and React
components/pages. Applying TDD uniformly across all of the above is an explicit anti-pattern in this
codebase, not an oversight to be corrected over time.

Required tooling: xUnit + FluentAssertions for backend unit tests; xUnit + `WebApplicationFactory` +
Testcontainers.PostgreSql for backend integration tests; Vitest + React Testing Library for frontend;
Playwright for E2E once a real user flow exists to test (currently and deliberately disabled in CI —
that disabled state must be visible, not silently bypassed).

### III. Consistent User Experience Across the Fleet

Once a feature has real-time wiring, every view of that data MUST update through the SignalR →
TanStack Query cache path (`setQueryData`/invalidation on hub events) — no view may fall back to manual
refresh or a parallel polling mechanism for the same data. Frontend nav/page visibility and API
authorization policies both MUST gate on the `permissions` catalog, never on role names, so the same
rule produces the same visibility decision on both sides of the stack. Domain state-machine semantics
(e.g. a printer in `AwaitingClear` vs. `Idle`) MUST be presented identically everywhere they surface in
the UI — the same label, the same available actions. Forms use react-hook-form + zod and primitives come
from shadcn/ui in `src/components/ui/` uniformly; no feature invents its own form-handling or component
pattern. All API errors surface as RFC 7807 `ProblemDetails`, giving the frontend exactly one error shape
to render rather than one per endpoint.

### IV. Build for Expansion, Not Speculation

Every feature MUST work unmodified for both a self-hosted single-organization install and a future
hosted multi-tenant SaaS. `organization_id` is present on every tenant-scoped table from day one, but the
resolution mechanism (a static single-tenant resolver today vs. Finbuckle's dynamic claim-based
resolution later) MUST be swappable without a schema migration. External integrations sit behind a
swappable contract (`IPrinterConnector` and equivalents) so a second implementation — a new printer
brand, a relay agent for hosted SaaS — never requires changes to `Application` or `Domain`.
Permission-based RBAC means adding a new role is a seed-data change, not a code change. This principle
cuts both ways: extraction of shared structure (e.g. pulling `printer_profiles` out of the `Printer`
entity) is deferred until a second real instance creates actual duplication — designed extension points
are required, preemptive abstraction is not.

### V. Performance and Real-Time Responsiveness by Construction

Polling is not an acceptable fallback once a feature has real-time (SignalR) wiring available to it.
Lists with unbounded growth — most notably the job queue, which must support unlimited depth — MUST use
TanStack Virtual; an unvirtualized render of an unbounded collection is a defect, not a later
optimization. BOM/assembly decomposition MUST run as a recursive CTE at the database layer, not as
application-side recursion, so correctness and latency don't degrade as nesting gets deeper.
Long-running or connection-holding work (Bambu LAN connections, maintenance-threshold checks,
auto-reload detection) runs as `BackgroundService`s inside the API host, and MUST be architected so it
can be extracted into a standalone worker host before the API needs multiple replicas — without a
rewrite when that extraction happens.

## Security & Multi-Tenancy Isolation

- Tenant isolation is enforced exclusively through Finbuckle's EF Core global query filter. Hand-rolled
  per-query tenant `WHERE` clauses are prohibited — this is precisely the bug class (a subtle
  cross-tenant data leak) that Finbuckle exists to prevent, and bespoke logic reopens it.
- Connection secrets — real printer LAN access codes — are encrypted at rest via ASP.NET Core's
  `IDataProtector` from the first commit that stores them. There is no "demo fixture" exemption; these
  rows always hold a working credential to a physical device.
- Every state-mutating command is captured by the FastEndpoints activity-log post-processor. Logging is
  structural — a cross-cutting concern wired once — not a per-feature convention that can be forgotten
  on a new endpoint.
- Auth is real, JWT-based, from day one. No unauthenticated or no-auth throwaway slice is permitted,
  including for scaffold-stage or internal-only endpoints, because retrofitting auth later means
  unwinding both the auth model and the activity log's actor field.

## Development Workflow & Quality Gates

- Conventional Commits are required for commit messages and PR titles — PR titles become the
  squash-merge commit message.
- CI-enforced gates block merges: `dotnet format --verify-no-changes`, Biome (lint + format),
  vulnerability scanning (`dotnet list package --vulnerable`, `bun audit`), secret scanning (Gitleaks),
  and the Orval contract-drift check once a real OpenAPI surface exists beyond `/api/v1/ping`.
- One EF Core migration per PR when the schema changes, reviewed like code — unrelated schema changes
  are not bundled into a single migration.
- `STUB`/`NOTE` markers in CI configuration or code (e.g. the currently disabled `e2e` job, the skipped
  Orval contract-drift check) MUST be checked before assuming a gate is fully wired. Closing a stub means
  removing the marker and enabling the real check in the same PR — the two states never coexist
  silently past that PR.

## Governance

This constitution supersedes ad hoc practice. `CLAUDE.md`, `docs/FEATURE_PLAN.md`, and
`docs/TECH_STACKS.md` operationalize these principles for day-to-day work and carry the detailed
rationale referenced above.

Amendments are proposed via a PR modifying this file. Each amendment PR MUST include an updated Sync
Impact Report (prepended as an HTML comment, as above) and a version bump following semantic versioning:
MAJOR for backward-incompatible principle removals or redefinitions, MINOR for a new principle or
materially expanded guidance, PATCH for wording/clarification fixes. Amendment PRs require review before
merge, same as any other change.

Every PR touching `Domain`, `Application`, `Infrastructure`, `Api`, or frontend feature code MUST be
checked against these principles during review. Any deliberate deviation MUST be justified in the PR
description, mirroring the `Complexity Tracking` table already present in `plan-template.md` for
feature-plan-level violations.

**Version**: 1.0.0 | **Ratified**: 2026-07-12 | **Last Amended**: 2026-07-12
