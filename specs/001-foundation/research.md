# Phase 0 Research: Foundation

No `NEEDS CLARIFICATION` markers remained in the Technical Context — `docs/TECH_STACKS.md`
and `docs/FEATURE_PLAN.md`'s Phase 0 section already lock in the stack and scoping. This
document instead records the concrete design decisions this plan makes where the source
docs leave implementation shape open, plus the alternatives considered for each.

## 1. Where does ASP.NET Core Identity live?

- **Decision**: `ApplicationUser : IdentityUser<Guid>` / `ApplicationRole : IdentityRole<Guid>`
  live in `OpenFarm.Infrastructure/Identity/`. `OpenFarmDbContext` inherits
  `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`. `Domain` never defines a
  `User` entity; `ActivityLogEntry.ActorId` is a plain `Guid?` scalar, not a navigation
  property. `Application` reaches auth only through interfaces (`IAuthenticationService`,
  `IActivityLogReader` returning DTOs with `ActorDisplayName` pre-joined).
- **Rationale**: Principle I forbids `Domain`/`Application` referencing framework types.
  Identity is unavoidably a framework concern (`UserManager`, `SignInManager`), so it must
  sit in `Infrastructure`, reached only through Application-owned interfaces — same pattern
  already used for `IPrinterConnector`.
- **Alternatives considered**: A hand-rolled `Domain.User` entity with its own password
  hashing — rejected as reinventing what ASP.NET Core Identity already does correctly, and
  `docs/TECH_STACKS.md` explicitly locks in "ASP.NET Core Identity" as the auth/identity choice.

## 2. Command/query dispatch without violating layering

- **Decision**: `Application` defines its own zero-dependency
  `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` marker interfaces.
  `Api` endpoints resolve the concrete handler via DI and call `HandleAsync` directly — no
  bus/mediator indirection.
- **Rationale**: `docs/TECH_STACKS.md` says to use "FastEndpoints' own lightweight command
  bus instead of adding MediatR," but that bus's types live in the `FastEndpoints` NuGet
  package, which Principle I forbids `Application` from referencing. Defining Application's
  own trivial handler interface preserves the REPR pattern and avoids adding MediatR, while
  keeping `Application` framework-agnostic. Same-request, same-process invocation doesn't
  need bus indirection to get FastEndpoints' benefits.
- **Alternatives considered**: Reference `FastEndpoints.Command`/`ICommandHandler` directly
  from `Application` — rejected, direct Principle I violation. Add MediatR — rejected,
  `docs/TECH_STACKS.md` explicitly rejects it.

## 3. Multi-tenancy resolution strategy for Phase 0

> Superseded: OpenFarm is now single-tenant; the multi-tenancy decision below no longer applies.

- **Decision**: Wire real Finbuckle.MultiTenant packages (`Finbuckle.MultiTenant` +
  `.EntityFrameworkCore`), `AddMultiTenant<AppTenantInfo>().WithStaticStrategy(<fixed id>)`.
  EF Core's `.IsMultiTenant()` per-entity global query filter is still Finbuckle-generated.
- **Rationale**: `docs/FEATURE_PLAN.md`'s Phase 0 section explicitly calls for "schema now,
  resolution machinery later" — a static single-tenant resolver until Phase 20 needs real
  org-switching. Using Finbuckle's own static-strategy mode (rather than hand-rolling a
  `WHERE organization_id = ...` filter) keeps the constitution's "tenant isolation enforced
  exclusively through Finbuckle's EF Core global query filter" rule intact even in this
  reduced form — swapping to claim-based resolution later is a strategy-registration change,
  not a schema or query-filter change.
- **Alternatives considered**: Hand-rolled tenant filter now, "upgrade" to Finbuckle in
  Phase 20 — rejected, this is exactly the bespoke-query-filter risk the constitution calls
  out as a cross-tenant leak vector, and it would mean migrating every tenant-scoped query
  later instead of just the strategy registration.

## 4. Orval: unstub now or defer?

- **Decision**: Un-stub `web/orval.config.ts` and enable the frontend CI job's contract-drift
  check in this same PR. Commit a generated `api/openapi.json` (FastEndpoints.Swagger export).
- **Rationale**: The stub comment and CI `NOTE` both name the exact unlock condition —
  "a real endpoint set" / "a real OpenAPI surface beyond `/api/v1/ping`" — and Phase 0 ships
  five real endpoints. The constitution requires closing a STUB "in the same PR" that
  resolves its blocking condition, not leaving it stubbed once the condition is already true.
- **Alternatives considered**: Leave Orval stubbed one more phase, hand-write fetch calls for
  Phase 0's few endpoints — rejected as directly contradicting the constitution's STUB-closing
  rule once the condition it's gated on becomes false.

## 5. Does the `e2e` CI job turn on this phase?

- **Decision**: Leave `if: false` as-is.
- **Rationale**: The job's own existing comment sets the bar at "M1+ ships a real flow
  (create printer, etc.)" — a materially richer flow than login alone. Phase 0 does deliver
  a real login flow (User Story 1), which is a legitimate case for enabling a minimal smoke
  test, but the existing marker's explicit scoping should be honored rather than unilaterally
  reinterpreted mid-plan.
- **Alternatives considered**: Add a login-only Playwright smoke test now — viable as a
  stretch goal, but not required by this plan; flagged as an open discussion point rather
  than silently done or silently skipped.

## 6. shadcn primitive set for Phase 0

- **Decision**: Add `input`, `label`, `form`, `card`, `dialog`, `table`, `sonner`, `sidebar`
  (pulls in `sheet`, `tooltip`, `separator`, `skeleton`), `avatar`, `dropdown-menu` —
  vendored into `src/components/ui/` via `bunx shadcn add`, on top of the existing `button`.
- **Rationale**: Each maps to a concrete Phase 0 need: `input`/`label`/`form` for the login
  and connection-secret forms; `dialog` for the required confirm-before-create step on an
  immutable credential; `table` for paged connection-secret/activity-log listings (no
  TanStack Virtual — that's Principle V's Phase 9 job-queue requirement, not this phase's);
  `sonner` as the single global toast host rendering RFC 7807 errors consistently
  (Principle III: "one error shape to render"); `sidebar`+friends for the nav shell,
  installed now specifically because retrofitting nav structure after screens already exist
  is the exact "future screen built before the base kit exists" trap the spec's own edge
  case names; `avatar`/`dropdown-menu` for the logout affordance the auth flow itself needs.
- **Alternatives considered**: Install the full shadcn catalog up front — rejected as
  speculative (Principle IV: "designed extension points are required, preemptive abstraction
  is not"); install nothing until each is needed feature-by-feature — rejected because User
  Story 5's whole point is a ready kit existing *before* the next feature phase starts.
