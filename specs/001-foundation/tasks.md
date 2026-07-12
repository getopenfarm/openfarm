---

description: "Task list template for feature implementation"
---

# Tasks: Foundation — Authenticated Access, Organization Scaffolding, Secured Credentials, Activity History, and a Base UI Kit

**Input**: Design documents from `/specs/001-foundation/` (plan.md, spec.md, research.md, data-model.md, contracts/openapi.yaml, quickstart.md)

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/openapi.yaml, quickstart.md — all present.

**Tests**: TDD (test-first) is applied only where the constitution mandates it — `LoginCommandValidator`, `CreateConnectionSecretCommandValidator`, and `ConnectionSecret`'s domain invariants (deterministic, non-trivial logic). Everything else (orchestration handlers, EF configs, the seeder, endpoints, React components) is test-after, and those test tasks are included inline where they add real verification value — not as speculative padding.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Web app split, matching plan.md's Project Structure exactly: `api/src/OpenFarm.{Domain,Application,Infrastructure,Api}/`, `api/tests/OpenFarm.{Domain,Application,Api.Integration}Tests/`, `web/src/{app,lib,features,components/ui}/`.

**Two placement judgment calls, stated up front:**

1. **`ActivityLogEntry` entity + its EF configuration live in Phase 2 (Foundational)**, not User Story 4's phase, even though its *read* side (query/reader/endpoint/page) is built in US4. Reason: this repo's constitution requires **one consolidated `InitialFoundation` migration**, generated once all entity configs exist — so the entity must exist before that migration task regardless of which story "owns" it narratively. More importantly, the **write path** (`IActivityLogWriter` port, `EfActivityLogWriter`, `IActivityLoggable` marker, `ActivityLogPostProcessor`, global registration) must all be live *before* User Story 3 ships, because creating a `ConnectionSecret` is the first real loggable mutation in the system (login issues a token but mutates no persisted row, so it is not itself logged) — that dependency runs P2→P1, not the other way around, so the write mechanism cannot wait for US4's phase. US4's phase is therefore scoped to retrieval only (FR-010), which is also exactly what its own Independent Test in spec.md checks.
2. **Orval generation is incremental per story, not deferred entirely to Polish.** `web/orval.config.ts` is un-stubbed the moment the first real endpoint group (`auth`) exists (User Story 1), since research.md's own unlock condition ("a real endpoint set" beyond `/api/v1/ping`) is satisfied there — waiting would otherwise force hand-rolled fetch calls in US1/US3/US4, which research.md explicitly rejected as an alternative. What *does* wait for Polish is the **CI contract-drift gate** and the **final, full-surface regeneration + `api/openapi.json` commit**, since that check must run against the complete 5-endpoint surface, not a partial one.

---

## Phase 1: Setup

**Purpose**: Add the dependencies and configuration this feature needs on top of the existing 4-project/SPA scaffold (no new projects — `api/src/OpenFarm.*` and `web/` already exist).

- [X] T001 [P] Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Finbuckle.MultiTenant`, and `Finbuckle.MultiTenant.EntityFrameworkCore` package references to `api/src/OpenFarm.Infrastructure/OpenFarm.Infrastructure.csproj`
- [X] T002 [P] Add `Microsoft.AspNetCore.Authentication.JwtBearer` package reference to `api/src/OpenFarm.Api/OpenFarm.Api.csproj`
- [X] T003 [P] Add `FluentValidation` package reference to `api/src/OpenFarm.Application/OpenFarm.Application.csproj`
- [X] T004 [P] Add a `dotnet-ef` local tool manifest so migrations can be generated/applied in dev and CI in `api/.config/dotnet-tools.json`
- [X] T005 [P] Add `Jwt` (Issuer/Audience/SigningKey), `Seed` (AdminEmail/AdminPassword), and `DataProtection` config section placeholders to `api/src/OpenFarm.Api/appsettings.json` and `api/src/OpenFarm.Api/appsettings.Development.json`
- [X] T006 [P] Add matching `Jwt__*` / `Seed__*` environment entries to `.env.example` and `infra/.env`
- [X] T007 [P] Vendor the Phase 0 shadcn primitive set (`input`, `label`, `form`, `card`, `dialog`, `table`, `sonner`, `sidebar` — pulls in `sheet`/`tooltip`/`separator`/`skeleton` — `avatar`, `dropdown-menu`) via `bunx shadcn add` into `web/src/components/ui/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure — domain entities needed by the one consolidated migration, Identity, Finbuckle multi-tenancy, the activity-log write mechanism, JWT/FluentValidation/ProblemDetails plumbing, and the base frontend shell — that must exist before any user story is implementable.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T008 [TDD test] Write failing domain tests for `ConnectionSecret` invariants (immutable — no setters/Update path; `Create(...)` rejects empty serial number/ciphertext) in `api/tests/OpenFarm.Domain.Tests/ConnectionSecrets/ConnectionSecretTests.cs`
- [X] T009 Implement `ConnectionSecret` entity with a `Create(...)` factory and no setters/Update method (make T008 pass) in `api/src/OpenFarm.Domain/ConnectionSecrets/ConnectionSecret.cs`
- [X] T010 [P] Create `Organization` entity with a fixed well-known static `Id` in `api/src/OpenFarm.Domain/Organizations/Organization.cs`
- [X] T011 [P] Create `ActorType` enum (`Operator`, `System`) in `api/src/OpenFarm.Domain/ActivityLog/ActorType.cs`
- [X] T012 Create append-only `ActivityLogEntry` entity (no update/delete operations) in `api/src/OpenFarm.Domain/ActivityLog/ActivityLogEntry.cs` (depends on T011)
- [X] T013 [P] Define `ICommandHandler<TCommand, TResult>` marker interface in `api/src/OpenFarm.Application/Common/ICommandHandler.cs`
- [X] T014 [P] Define `IQueryHandler<TQuery, TResult>` marker interface in `api/src/OpenFarm.Application/Common/IQueryHandler.cs`
- [X] T015 [P] Define `ISecretProtector` port (`Protect`/`Unprotect`) in `api/src/OpenFarm.Application/Common/ISecretProtector.cs`
- [X] T016 [P] Define `IActivityLogWriter` port in `api/src/OpenFarm.Application/ActivityLog/IActivityLogWriter.cs`
- [X] T017 [P] Create `ApplicationUser : IdentityUser<Guid>` in `api/src/OpenFarm.Infrastructure/Identity/ApplicationUser.cs`
- [X] T018 [P] Create `ApplicationRole : IdentityRole<Guid>` (unused this phase, added now to avoid an Identity-table migration diff later) in `api/src/OpenFarm.Infrastructure/Identity/ApplicationRole.cs`
- [X] T019 [P] Create `AppTenantInfo : ITenantInfo` in `api/src/OpenFarm.Infrastructure/Multitenancy/AppTenantInfo.cs`
- [X] T020 Create `OrganizationTenantStore : IMultiTenantStore<AppTenantInfo>` backed by the `Organizations` table (not a separate Tenants table) in `api/src/OpenFarm.Infrastructure/Multitenancy/OrganizationTenantStore.cs` (depends on T010, T019)
- [X] T021 Implement `DataProtectionSecretProtector : ISecretProtector` using ASP.NET Core's `IDataProtector` in `api/src/OpenFarm.Infrastructure/Security/DataProtectionSecretProtector.cs` (depends on T015)
- [X] T022 Implement `OpenFarmDbContext` — inherit `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`, add `DbSet<Organization>`/`DbSet<ConnectionSecret>`/`DbSet<ActivityLogEntry>`, wire Finbuckle's multi-tenant `DbContext` base in `api/src/OpenFarm.Infrastructure/Persistence/OpenFarmDbContext.cs` (depends on T009, T010, T012, T017, T018, T019)
- [X] T023 [P] `OrganizationConfiguration : IEntityTypeConfiguration<Organization>` in `api/src/OpenFarm.Infrastructure/Persistence/Configurations/OrganizationConfiguration.cs`
- [X] T024 [P] `ConnectionSecretConfiguration` — `.IsMultiTenant()` keyed on `OrganizationId`, no update-path mapping — in `api/src/OpenFarm.Infrastructure/Persistence/Configurations/ConnectionSecretConfiguration.cs`
- [X] T025 [P] `ActivityLogEntryConfiguration` — `.IsMultiTenant()`, composite indexes `(OrganizationId, TimestampUtc)` and `(EntityType, EntityId)` — in `api/src/OpenFarm.Infrastructure/Persistence/Configurations/ActivityLogEntryConfiguration.cs`
- [X] T026 Implement `EfActivityLogWriter : IActivityLogWriter` (append-only insert, lets write failures propagate rather than swallowing them) in `api/src/OpenFarm.Infrastructure/ActivityLog/EfActivityLogWriter.cs` (depends on T016, T022)
- [X] T027 Implement `FirstBootSeeder` — idempotent (check-then-create via `UserManager.FindByEmailAsync` / org-id lookup) seeding of the single `Organization` row and the single administrator-level `ApplicationUser` from `Seed:AdminEmail`/`Seed:AdminPassword` — in `api/src/OpenFarm.Infrastructure/Seed/FirstBootSeeder.cs` (depends on T010, T017, T022)
- [X] T028 Generate the single consolidated `InitialFoundation` EF Core migration (`dotnet ef migrations add InitialFoundation`) covering Identity tables, `Organizations`, `ConnectionSecrets`, `ActivityLogEntries` in `api/src/OpenFarm.Infrastructure/Persistence/Migrations/` (depends on T022–T027)
- [X] T029 [P] Create `PermissionAttribute` (decorates endpoints with a permission string, unused for authz logic this phase) in `api/src/OpenFarm.Api/Security/PermissionAttribute.cs`
- [X] T030 Create `PermissionAuthorizationHandler` — checks only `User.Identity?.IsAuthenticated` this phase (FR-005) — in `api/src/OpenFarm.Api/Security/PermissionAuthorizationHandler.cs` (depends on T029)
- [X] T031 [P] Create `IActivityLoggable` marker interface for request DTOs in `api/src/OpenFarm.Api/Processors/IActivityLoggable.cs`
- [X] T032 Implement `ActivityLogPostProcessor` — globally-registered FastEndpoints post-processor; runs only after a successful mutating response whose DTO implements `IActivityLoggable`, writes via `IActivityLogWriter`, lets write exceptions propagate — in `api/src/OpenFarm.Api/Processors/ActivityLogPostProcessor.cs` (depends on T016, T026, T031)
- [X] T033 Wire Program.cs: `AddIdentityCore<ApplicationUser>`/roles, `AddMultiTenant<AppTenantInfo>().WithStaticStrategy(...).WithStore<OrganizationTenantStore>()` + `app.UseMultiTenant()`, JWT bearer scheme registration (concrete token validation wired in US1), `AddAuthorization` with the `Permission` policy handler, FluentValidation assembly scan, global `ProblemDetails` error mapping, global `ActivityLogPostProcessor` registration, and startup `Database.MigrateAsync()` + `FirstBootSeeder` invocation in `api/src/OpenFarm.Api/Program.cs` (depends on T020, T021, T027, T028, T030, T032)
- [X] T034 [P] Add `Testcontainers.PostgreSql` package reference to `api/tests/OpenFarm.Api.IntegrationTests/OpenFarm.Api.IntegrationTests.csproj`
- [X] T035 Create a shared `CustomWebApplicationFactory` (applies `InitialFoundation` migration + runs `FirstBootSeeder` against a Testcontainers Postgres instance) in `api/tests/OpenFarm.Api.IntegrationTests/CustomWebApplicationFactory.cs` (depends on T033, T034)
- [X] T036 [P] Build `AppShell` layout (sidebar nav shell) in `web/src/app/layout/AppShell.tsx`
- [X] T037 [P] Build `ProtectedRoute` auth gate component in `web/src/app/layout/ProtectedRoute.tsx`
- [X] T038 [P] Configure the shared TanStack Query client in `web/src/lib/api/queryClient.ts`
- [X] T039 [P] Build JWT `tokenStorage` helper (get/set/clear) in `web/src/lib/auth/tokenStorage.ts`
- [X] T040 Wire `AppShell`/`ProtectedRoute` into the router skeleton (existing `DashboardPage` as the protected index route) in `web/src/app/routes.tsx` (depends on T036, T037)

**Checkpoint**: Schema exists (one migration), Identity + Finbuckle + activity-log write mechanism + ProblemDetails/FluentValidation pipeline are live, base frontend shell exists. No user-facing endpoint or page exists yet.

---

## Phase 3: User Story 1 - Operator logs in and reaches the app (Priority: P1) 🎯 MVP

**Goal**: The single seeded operator can authenticate with real credentials via `POST /auth/login`, retrieve their identity via `GET /auth/whoami`, and reach an authenticated area in the browser; any unauthenticated request to a protected area is rejected.

**Independent Test**: Sign in with the seeded operator's credentials and confirm access to an authenticated screen; separately confirm an unauthenticated request to a protected area is rejected (spec.md US1 Independent Test).

> Note: this story is only demonstrable because Phase 2 already seeded both the `Organization` and the operator account together (`FirstBootSeeder`) — `whoami`'s `organizationId` and the JWT's `org` claim have nothing to report otherwise. See "Implementation Strategy" below for what this means for the MVP boundary.

### Tests for User Story 1

- [X] T041 [TDD test] [US1] Write failing FluentValidation tests for `LoginCommandValidator` (rejects empty username/password) in `api/tests/OpenFarm.Application.Tests/Auth/LoginCommandValidatorTests.cs`

### Implementation for User Story 1

- [X] T042 [US1] Implement `LoginCommand` + `LoginCommandValidator` (make T041 pass) in `api/src/OpenFarm.Application/Auth/LoginCommand.cs`, `api/src/OpenFarm.Application/Auth/LoginCommandValidator.cs`
- [X] T043 [P] [US1] Define `IAuthenticationService` port (`AuthenticateAsync`, `GetUserAsync`) in `api/src/OpenFarm.Application/Auth/IAuthenticationService.cs`
- [X] T044 [US1] Implement `LoginCommandHandler : ICommandHandler<LoginCommand, LoginResult>` in `api/src/OpenFarm.Application/Auth/LoginCommandHandler.cs` (depends on T042, T043)
- [X] T045 [P] [US1] Implement `WhoAmIQuery` + `WhoAmIQueryHandler` in `api/src/OpenFarm.Application/Auth/WhoAmIQuery.cs` (depends on T043)
- [X] T046 [US1] Implement `JwtTokenService` (issues JWT with `sub` + `org` claims) in `api/src/OpenFarm.Infrastructure/Auth/JwtTokenService.cs`
- [X] T047 [US1] Implement `IdentityAuthenticationService : IAuthenticationService` (via `UserManager`/`SignInManager` + `JwtTokenService`) in `api/src/OpenFarm.Infrastructure/Auth/IdentityAuthenticationService.cs` (depends on T043, T046)
- [X] T048 [US1] Register `IAuthenticationService`/`JwtTokenService` DI and finish JWT bearer token-validation options (signing key/issuer/audience) in `api/src/OpenFarm.Api/Program.cs` (depends on T044, T045, T047)
- [X] T049 [P] [US1] `LoginEndpoint` — `POST /api/v1/auth/login`, anonymous, FluentValidation pre-processor — in `api/src/OpenFarm.Api/Endpoints/Auth/LoginEndpoint.cs`
- [X] T050 [P] [US1] `WhoAmIEndpoint` — `GET /api/v1/auth/whoami`, `[Permission]`-gated — in `api/src/OpenFarm.Api/Endpoints/Auth/WhoAmIEndpoint.cs`
- [X] T051 [US1] Integration tests: correct/incorrect login, `whoami` with/without/invalid token, in `api/tests/OpenFarm.Api.IntegrationTests/Auth/LoginEndpointTests.cs`, `api/tests/OpenFarm.Api.IntegrationTests/Auth/WhoAmIEndpointTests.cs` (depends on T035, T049, T050)
- [X] T052 [US1] Un-stub `web/orval.config.ts` (remove the stub comment), add an auth-header-injecting fetch mutator in `web/src/lib/api/httpClient.ts`, run the first `bunx orval` generation pass for the `auth` tag into `web/src/lib/api/generated/`
- [X] T053 [P] [US1] Build `AuthProvider` + `useAuth` hook (token persistence via `tokenStorage`) in `web/src/lib/auth/AuthProvider.tsx`, `web/src/lib/auth/useAuth.ts`
- [X] T054 [US1] Build `LoginPage` (react-hook-form + zod, shadcn `form`/`input`/`label`/`card`, calls the generated login hook) in `web/src/features/auth/LoginPage.tsx` (depends on T052, T053)
- [X] T055 [US1] Wire `/login` route and protect `/` (existing `DashboardPage`) behind `ProtectedRoute` with redirect-to-login in `web/src/app/routes.tsx` (depends on T054)
- [X] T056 [US1] Frontend tests: login render/submit/error states, protected-route redirect behavior, in `web/src/features/auth/LoginPage.test.tsx`, `web/src/app/layout/ProtectedRoute.test.tsx`

**Checkpoint**: Seeded operator can log in via curl or browser, reach the authenticated shell, and `whoami` correctly reports their identity + organization; unauthenticated `whoami` returns 401.

---

## Phase 4: User Story 2 - Every install has exactly one organization automatically (Priority: P1)

**Goal**: Verify and lock in the guarantee that first boot produces exactly one `Organization`, idempotently, with no manual step — the seam every tenant-scoped table (already wired via Finbuckle in Phase 2) depends on.

**Independent Test**: Start a fresh install and confirm exactly one organization exists with no manual action, then confirm any tenant-scoped record created afterward is associated with it automatically (spec.md US2 Independent Test).

> The org-creation *mechanism* itself (`Organization` entity, `FirstBootSeeder`, `OrganizationTenantStore`, `.IsMultiTenant()` EF configs) was necessarily built in Phase 2, not here — see the note under Phase 3. The tasks below verify that mechanism holds under the scenarios spec.md actually calls out (idempotency, restart survival). The "any tenant-scoped record auto-associates" half of this story's Independent Test is concretely proven by User Story 3's `ConnectionSecretsEndpointTests` (T068), the first story to create a tenant-scoped row — flagged here rather than duplicated.

### Tests for User Story 2

- [X] T057 [US2] Integration test: `FirstBootSeeder` creates exactly one `Organization` row against a fresh database, and running it again is a no-op (still exactly one row) in `api/tests/OpenFarm.Api.IntegrationTests/Organizations/FirstBootSeederTests.cs`
- [X] T058 [US2] Integration test: three consecutive simulated app-host restarts (re-running `FirstBootSeeder` against the same database) converge on exactly one `Organization` row (SC-004), same file as T057
- [X] T059 [US2] Integration test: an authenticated `whoami` call's `organizationId` equals `Organization`'s well-known seeded `Id`, extending `api/tests/OpenFarm.Api.IntegrationTests/Auth/WhoAmIEndpointTests.cs`

**Checkpoint**: Organization auto-creation is proven idempotent across repeated boots and correctly threaded through the authenticated session established in User Story 1.

---

## Phase 5: User Story 3 - Printer connection credentials are stored securely, masked wherever shown, and never editable in place (Priority: P2)

**Goal**: An operator can create a `ConnectionSecret` whose access code is encrypted at rest via `IDataProtector`, never returned or logged in plaintext, always rendered masked, and has no edit/delete path — only create-new-to-replace.

**Independent Test**: Store a credential, confirm it cannot be read back in plain form via storage inspection, logs, or any API response (only via the one authorized decrypt path), and confirm no edit operation exists (spec.md US3 Independent Test).

### Tests for User Story 3

- [X] T060 [TDD test] [US3] Write failing FluentValidation tests for `CreateConnectionSecretCommandValidator` (rejects empty `printerSerialNumber`/`plainAccessCode`) in `api/tests/OpenFarm.Application.Tests/ConnectionSecrets/CreateConnectionSecretCommandValidatorTests.cs`

### Implementation for User Story 3

- [X] T061 [US3] Implement `CreateConnectionSecretCommand` + `CreateConnectionSecretCommandValidator` (make T060 pass) in `api/src/OpenFarm.Application/ConnectionSecrets/CreateConnectionSecretCommand.cs`, `api/src/OpenFarm.Application/ConnectionSecrets/CreateConnectionSecretCommandValidator.cs`
- [X] T062 [P] [US3] Define `IConnectionSecretRepository` port (`AddAsync`, `ListAsync`) in `api/src/OpenFarm.Application/ConnectionSecrets/IConnectionSecretRepository.cs`
- [X] T063 [US3] Implement `CreateConnectionSecretCommandHandler` — encrypts via `ISecretProtector.Protect`, builds via `ConnectionSecret.Create`, persists via the repository, never holds the plaintext beyond this call — in `api/src/OpenFarm.Application/ConnectionSecrets/CreateConnectionSecretCommandHandler.cs` (depends on T061, T062)
- [X] T064 [P] [US3] Implement `ListConnectionSecretsQuery` + Handler (always projects the masked shape, never calls `Unprotect`) in `api/src/OpenFarm.Application/ConnectionSecrets/ListConnectionSecretsQuery.cs` (depends on T062)
- [X] T065 [US3] Implement `ConnectionSecretRepository : IConnectionSecretRepository` (EF Core, relies on Finbuckle's global query filter — no hand-rolled `WHERE`) in `api/src/OpenFarm.Infrastructure/Persistence/ConnectionSecretRepository.cs` (depends on T022, T062)
- [X] T066 [P] [US3] `CreateConnectionSecretEndpoint` — `POST /api/v1/connection-secrets`, `[Permission]`-gated, request DTO implements `IActivityLoggable` — in `api/src/OpenFarm.Api/Endpoints/ConnectionSecrets/CreateConnectionSecretEndpoint.cs`
- [X] T067 [P] [US3] `ListConnectionSecretsEndpoint` — `GET /api/v1/connection-secrets` — in `api/src/OpenFarm.Api/Endpoints/ConnectionSecrets/ListConnectionSecretsEndpoint.cs`
- [X] T068 [US3] Integration tests: create returns masked response (never plaintext), stored `EncryptedAccessCode` is ciphertext, list is masked, no `PUT`/`PATCH`/`DELETE` route exists (404/405), created row's `OrganizationId` equals the seeded org (closes US2's forward reference), and a failed-validation create produces no activity-log entry, in `api/tests/OpenFarm.Api.IntegrationTests/ConnectionSecrets/ConnectionSecretsEndpointTests.cs` (depends on T066, T067)
- [X] T069 [US3] Regenerate the Orval client to add the `connection-secrets` tag (`bunx orval`), committing updated files under `web/src/lib/api/generated/`
- [X] T070 [P] [US3] Build `ConnectionSecretsPage` (shadcn `table` + "Add credential" `dialog` with a react-hook-form + zod form) in `web/src/features/connection-secrets/ConnectionSecretsPage.tsx`
- [X] T071 [US3] Wire `/connection-secrets` route and its nav item in `web/src/app/routes.tsx`, `web/src/app/layout/AppShell.tsx` (depends on T070)
- [X] T072 [US3] Frontend test: create flow shows the masked value in the table, empty-access-code submission is blocked inline with no request sent, in `web/src/features/connection-secrets/ConnectionSecretsPage.test.tsx`

**Checkpoint**: Connection secrets are created, encrypted, always masked on read, never editable — end to end through the UI.

---

## Phase 6: User Story 4 - Every meaningful change leaves a traceable record (Priority: P2)

**Goal**: Every state-mutating action already captured by Phase 2's `ActivityLogPostProcessor`/`EfActivityLogWriter` is retrievable, correctly attributed, and correctly ordered via `GET /activity-log`.

**Independent Test**: Perform any state-changing action and confirm a corresponding record (entity, event type, actor, timestamp) exists and is retrievable afterward (spec.md US4 Independent Test).

### Implementation for User Story 4

- [X] T073 [P] [US4] Define `IActivityLogReader` port in `api/src/OpenFarm.Application/ActivityLog/IActivityLogReader.cs`
- [X] T074 [US4] Implement `GetActivityLogQuery` + Handler (paged, newest-first) in `api/src/OpenFarm.Application/ActivityLog/GetActivityLogQuery.cs` (depends on T073)
- [X] T075 [US4] Implement `EfActivityLogReader : IActivityLogReader` (joins `AspNetUsers` to resolve `ActorDisplayName`; `null` for `System` actors) in `api/src/OpenFarm.Infrastructure/ActivityLog/EfActivityLogReader.cs` (depends on T022, T073)
- [X] T076 [US4] `GetActivityLogEndpoint` — `GET /api/v1/activity-log?page&pageSize` — in `api/src/OpenFarm.Api/Endpoints/ActivityLog/GetActivityLogEndpoint.cs` (depends on T074, T075)
- [X] T077 [US4] Integration tests: creating a connection secret produces a retrievable entry with correct `entityType`/`eventType`/`actorType`/`actorDisplayName`/`timestampUtc`, entries are ordered newest-first, pagination works, and a failed-validation `POST /connection-secrets` produces no entry (FR-011), in `api/tests/OpenFarm.Api.IntegrationTests/ActivityLog/ActivityLogEndpointTests.cs` (depends on T076)
- [X] T078 [US4] Regenerate the Orval client to add the `activity-log` tag (`bunx orval`), committing updated files under `web/src/lib/api/generated/`
- [X] T079 [P] [US4] Build `ActivityLogPage` (paged shadcn `table`) in `web/src/features/activity-log/ActivityLogPage.tsx`
- [X] T080 [US4] Wire `/activity-log` route and its nav item in `web/src/app/routes.tsx`, `web/src/app/layout/AppShell.tsx` (depends on T079)
- [X] T081 [US4] Frontend test: `ActivityLogPage` renders entries and pagination controls, in `web/src/features/activity-log/ActivityLogPage.test.tsx`

**Checkpoint**: Every mutating action performed since Phase 2 (all of US3's writes) is now independently retrievable, attributed, and ordered.

---

## Phase 7: User Story 5 - Builders have a consistent, ready-to-use component kit (Priority: P3)

**Goal**: Close out the remaining base-kit affordances (logout, loading states, tooltips) so every primitive vendored in Setup is actually exercised somewhere, proving SC-005's "zero new low-level styling" bar for future screens.

**Independent Test**: Assemble a simple screen (form + submit + confirmation dialog) using only existing base components, with no new low-level styling written (spec.md US5 Independent Test) — already demonstrated by `ConnectionSecretsPage` (US3); this phase adds the remaining shared-shell affordances every future screen also needs.

### Implementation for User Story 5

- [X] T082 [US5] Add a logout affordance to `AppShell`'s nav (`avatar` + `dropdown-menu`, clears `tokenStorage`, redirects to `/login`) in `web/src/app/layout/AppShell.tsx`
- [X] T083 [P] [US5] Add `skeleton` loading states to the `ConnectionSecretsPage` and `ActivityLogPage` tables in `web/src/features/connection-secrets/ConnectionSecretsPage.tsx`, `web/src/features/activity-log/ActivityLogPage.tsx`
- [X] T084 [P] [US5] Add `tooltip` affordances to `AppShell`'s collapsed-nav icons in `web/src/app/layout/AppShell.tsx`
- [X] T085 [US5] Frontend test for `AppShell` nav/logout interaction in `web/src/app/layout/AppShell.test.tsx`

**Checkpoint**: The base UI kit (buttons, inputs, dialogs, tables, nav, loading states) is fully exercised; no future screen should need new low-level CSS.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Close the two remaining STUBs this feature's endpoint surface unlocks, verify formatting/lint gates, and walk every quickstart scenario end-to-end.

- [X] T086 Run a final full-surface `bunx orval` regeneration across all 5 operations and commit the FastEndpoints.Swagger-exported `api/openapi.json` alongside the refreshed `web/src/lib/api/generated/`
- [X] T087 Enable the frontend contract-drift CI check — remove the `NOTE` stub and add a `bunx orval && git diff --exit-code src/lib/api/generated` step — in `.github/workflows/ci.yml`
- [X] T088 Remove the backend "applies migrations" `NOTE` stub now that `InitialFoundation` exists, and confirm the `backend-build-test` job's migration path (via `CustomWebApplicationFactory`/Testcontainers) is real, in `.github/workflows/ci.yml`
- [X] T089 [P] Run `dotnet format api/OpenFarm.slnx --verify-no-changes` and fix any drift introduced by this feature
- [X] T090 [P] Run `bun run lint` / `bun run format` in `web/` and fix any drift introduced by this feature
- [X] T091 [P] Update the Phase 0 status marker in `docs/FEATURE_PLAN.md` (and `CLAUDE.md` if it references phase status) to reflect Foundation as delivered
- [X] T092 Manual quickstart walkthrough — Scenario 1 (seeded login, browser + curl negative checks) per `specs/001-foundation/quickstart.md`
- [X] T093 Manual quickstart walkthrough — Scenario 2 (single org, 3× restart idempotency via `psql`) per `specs/001-foundation/quickstart.md`
- [X] T094 Manual quickstart walkthrough — Scenario 3 (encryption/masking/immutability, including log-grep for plaintext) per `specs/001-foundation/quickstart.md`
- [X] T095 Manual quickstart walkthrough — Scenario 4 (activity-log retrieval + failed-validation no-entry check) per `specs/001-foundation/quickstart.md`
- [X] T096 Manual quickstart walkthrough — Scenario 5 (UI kit screen assembly, confirm zero new low-level styling in the diff) per `specs/001-foundation/quickstart.md`
- [X] T097 Run the full automated verification suite (`dotnet test api/OpenFarm.slnx`, `cd web && bun run test && bunx tsc -b && bun run build`) per `specs/001-foundation/quickstart.md`'s "Automated verification" section

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories. Includes the domain entities for *all three* Domain entity types (because the one consolidated `InitialFoundation` migration requires every entity config to exist first) and the full activity-log write path (because User Story 3, not User Story 4, is the first story to trigger a loggable mutation).
- **User Story 1 (Phase 3)**: Depends on Foundational. This is the practical MVP entry point.
- **User Story 2 (Phase 4)**: Depends on Foundational (the org-creation mechanism itself lives there). Its verification tasks (T057–T059) also lean on User Story 1's `whoami` endpoint (T050) for T059, and its full Independent Test isn't closed until User Story 3's T068 exists. Sequenced right after US1 per priority order, but its "hard proof" completes alongside US3.
- **User Story 3 (Phase 5)**: Depends on Foundational + User Story 1 (needs a bearer token to call its endpoints) + relies on User Story 2's org scaffolding already being live (it always was, from Phase 2).
- **User Story 4 (Phase 6)**: Depends on Foundational (write path) + User Story 1 (auth) — reads log entries User Story 3 already produces, so functionally follows it, though its own code has no compile-time dependency on US3.
- **User Story 5 (Phase 7)**: Depends on Foundational (shell/primitives) + benefits from US1/US3/US4's pages already existing to extend (nav items, logout). Lowest priority (P3) — can also be interleaved earlier by a second developer since its tasks touch shared shell files, not story-specific business logic.
- **Polish (Phase 8)**: Depends on all five user stories being complete (needs the full 5-endpoint surface for T086/T087, and all five quickstart scenarios for T092–T096).

### Within Each User Story

- TDD-mandated validator/invariant tests are written and confirmed failing before their implementation task (T041→T042, T060→T061, T008→T009).
- Application ports before their Infrastructure implementations (e.g., T062 before T065).
- Handlers before endpoints; endpoints before integration tests; integration tests before frontend pages that consume the generated client.
- Frontend routing/nav wiring after the page component it routes to.

### Parallel Opportunities

- All Setup tasks (T001–T007) are `[P]` — different files, no cross-dependencies.
- Within Foundational, the domain-entity group (T010, T011), the Application-port group (T013–T016), the Identity group (T017–T018), and the frontend-shell group (T036–T039) are each internally parallelizable.
- Within each user story, Application-port definitions, independent endpoint files, and independent frontend files marked `[P]` can run together once their shared dependency (e.g., the command/query they wrap) exists.
- Once Foundational is done, User Story 1 must go first (nothing else can be exercised without auth), but User Stories 3, 4, and 5 have no code-level dependency on each other and could be staffed to different developers in parallel once US1 lands — they only *narratively* build on each other's data.

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Domain entities that don't depend on each other:
Task: "Create Organization entity in api/src/OpenFarm.Domain/Organizations/Organization.cs"
Task: "Create ActorType enum in api/src/OpenFarm.Domain/ActivityLog/ActorType.cs"

# Application-layer ports, all independent of each other:
Task: "Define ICommandHandler<TCommand, TResult> marker in api/src/OpenFarm.Application/Common/ICommandHandler.cs"
Task: "Define IQueryHandler<TQuery, TResult> marker in api/src/OpenFarm.Application/Common/IQueryHandler.cs"
Task: "Define ISecretProtector port in api/src/OpenFarm.Application/Common/ISecretProtector.cs"
Task: "Define IActivityLogWriter port in api/src/OpenFarm.Application/ActivityLog/IActivityLogWriter.cs"

# Base frontend shell pieces, all independent files:
Task: "Build AppShell layout in web/src/app/layout/AppShell.tsx"
Task: "Build ProtectedRoute gate in web/src/app/layout/ProtectedRoute.tsx"
Task: "Configure TanStack Query client in web/src/lib/api/queryClient.ts"
Task: "Build tokenStorage helper in web/src/lib/auth/tokenStorage.ts"
```

---

## Implementation Strategy

### MVP framing — a genuine, not cosmetic, US1+US2 coupling

**User Story 1 cannot demonstrate anything meaningful without User Story 2's org scaffolding already in place.** `GET /auth/whoami`'s response contract requires `organizationId` (contracts/openapi.yaml), and the JWT itself carries an `org` claim from the moment of login (data-model.md, plan.md) — both are meaningless without a real seeded `Organization` row to point at. Both are also seeded by the *same* `FirstBootSeeder` component (plan.md's Infrastructure structure lists it once, seeding both the org and the operator idempotently in one startup task), so building "just enough org support for login to work" and "the real org story" as two separate efforts would mean writing the seeder twice.

The practical resolution used above: **the org-creation mechanism is Foundational-phase infrastructure** (T010, T020, T027 — required for the migration anyway), so it is *already done* by the time User Story 1's phase starts. This makes the true MVP boundary **Setup → Foundational → User Story 1 (Phase 3)** — at that checkpoint you have a real, restart-safe single organization *and* a real authenticated session referencing it, satisfying both stories' P1 acceptance scenarios simultaneously. User Story 2's own phase (Phase 4) is deliberately thin: it doesn't add new mechanism, it adds the specific verification tests (idempotency across N restarts, whoami/org-id correctness) that spec.md's SC-004 and Independent Test call for, so that requirement is checked explicitly rather than only implied by Phase 2 existing.

1. Complete Phase 1 (Setup) + Phase 2 (Foundational) — this alone already satisfies User Story 2's structural requirement.
2. Complete Phase 3 (User Story 1) — **STOP and VALIDATE**: seeded login works end-to-end, unauthenticated access is rejected. This is the true MVP.
3. Complete Phase 4 (User Story 2) — adds the idempotency/restart proof tests; no new runtime behavior.
4. Complete Phase 5 (User Story 3) — first real tenant-scoped, encrypted, masked, immutable resource; closes User Story 2's "auto-association" proof as a side effect (T068).
5. Complete Phase 6 (User Story 4) — activity history becomes retrievable for the mutations US3 already produces.
6. Complete Phase 7 (User Story 5) — remaining shared-shell polish; already substantially proven by US1/US3/US4's pages.
7. Complete Phase 8 (Polish) — full contract-drift CI gate, formatting gates, and the complete quickstart.md walkthrough.

### Parallel Team Strategy

With multiple developers, after Setup + Foundational are complete together: Developer A takes User Story 1 (must land first — nothing else is reachable without auth). Once US1 lands, Developers B/C/D can take User Stories 3, 4, and 5 in parallel (no code-level dependency between them), while User Story 2's thin verification phase can be picked up by whoever finishes first.

### Critical Files for Implementation

- `api/src/OpenFarm.Infrastructure/Persistence/OpenFarmDbContext.cs` — the single DbContext all Identity/Finbuckle/entity wiring converges on; gates the one consolidated migration.
- `api/src/OpenFarm.Infrastructure/Seed/FirstBootSeeder.cs` — the shared mechanism both US1 (operator) and US2 (organization) actually depend on; get its idempotency right once.
- `api/src/OpenFarm.Api/Processors/ActivityLogPostProcessor.cs` — must be globally registered and functioning before User Story 3's first mutating endpoint ships, not added reactively in User Story 4.
- `api/src/OpenFarm.Api/Program.cs` — the single place Identity, Finbuckle, JWT bearer, FluentValidation, ProblemDetails, and the post-processor are all actually wired together; touched incrementally across Phases 2 and 3.
- `web/orval.config.ts` — the un-stub point; once flipped in User Story 1, every subsequent story's frontend work depends on regenerating against it rather than hand-rolling fetch calls.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing (T008, T041, T060)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence
