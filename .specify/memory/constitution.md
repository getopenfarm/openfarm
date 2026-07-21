<!--
Sync Impact Report
- Version change: 1.0.0 → 1.1.0
- Rationale: MINOR bump. Principle V gains a new, materially-expanded guardrail block ("Durable
  Recovery of In-Flight Background Work") distilled from a real production bug: a part upload stuck
  showing "Estimating…" forever after a mid-processing host restart, because the background service's
  in-memory work queue is never durable across restarts and nothing reconciled the orphaned row. No
  existing principle was removed or redefined, so this is not a MAJOR bump.
- Modified principles:
  - V. Performance and Real-Time Responsiveness by Construction — appended the background-work
    durability/reconciliation guardrail (net-new guidance, existing text unchanged)
- Added principles: none
- Added sections: none (the guardrail lives inside Principle V)
- Removed sections: none
- Templates requiring updates:
  - .specify/templates/plan-template.md — ✅ no change needed (generic Constitution Check pointer)
  - .specify/templates/spec-template.md — ✅ no change needed
  - .specify/templates/tasks-template.md — ✅ no change needed
- Follow-up TODOs: none deferred; no placeholders left unfilled.

Prior report (1.0.0, retained for history):
- Version change: (unratified template) → 1.0.0 — Initial ratification (every placeholder was still
  present; MAJOR bump per "Initial adoption"). Added principles I–V, the Security & Multi-Tenancy
  Isolation and Development Workflow & Quality Gates sections; no principles renamed or removed.
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

### Durable Recovery of In-Flight Background Work

This guardrail was ratified after a real defect: a part upload sat showing "Estimating…" forever
because the host restarted (crash, `dotnet watch` reload, deploy) while its job was mid-processing. The
work queue is an in-memory `System.Threading.Channels.Channel`, which is never durable across restarts —
so the job's row stayed at a non-terminal status, the queue was empty on the next boot, and nothing
ever picked it back up. The three rules below are what make that failure mode structurally
impossible rather than a bug to be re-fixed per feature.

- **Every persisted entity a `BackgroundService` moves through non-terminal states MUST have a
  startup reconciliation pass.** Because the queue is in-memory, any row still in an in-flight status
  at process start was orphaned by a previous lifetime — not genuinely in progress — so it MUST be
  driven to a terminal state (Failed/Unavailable, mirroring the feature's normal failure path) once,
  before workers begin consuming. The recovery model is the feature's existing one (no silent
  auto-retry unless the feature already specifies retry); a user re-submitting is an acceptable
  terminal outcome, a permanently-spinning UI is not.
- **An operation that can leave an entity non-terminal MUST route EVERY failure to a terminal
  transition, not only the anticipated ones.** Narrow `catch (SpecificException)` blocks around such
  work are a trap: any unmodeled exception (missing CLI binary, unconfigured path, I/O error, parse
  failure) escapes to a generic log-and-continue and strands the row. Catch every non-cancellation
  exception around the whole operation and route it to the terminal transition — either mark the row
  failed directly with the exception's message, or convert it to the feature's domain failure type
  where a lower layer owns that mapping (e.g. `SliceAsync` wrapping arbitrary failures as
  `SlicingFailedException`) — so the single terminal-transition path always runs.
- **Genuine host-shutdown cancellation MUST be distinguished from a real per-item failure.** Swallow
  `OperationCanceledException` only when the stopping token is actually cancelled (shutdown) — never
  mislabel ordinary shutdown as an error, and never let it crash the host via the default
  `BackgroundServiceExceptionBehavior.StopHost`. Cleanup of whatever shutdown left behind is owned by
  the next startup's reconciliation pass, not the consume loop.

Corollary — **enqueue MUST NOT silently drop work.** A bounded channel's `TryWrite` returns `false`
when full regardless of `FullMode`; ignoring that return value strands a row exactly like a restart
does. Enqueue paths MUST either surface the drop (so the caller fails the command / marks the row
terminal) or block until capacity is available — never discard the item and leave the persisted row
behind. Reconciliation is the safety net of last resort, not a licence to lose work between restarts.

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

**Version**: 1.1.0 | **Ratified**: 2026-07-12 | **Last Amended**: 2026-07-20
