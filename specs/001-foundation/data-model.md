# Phase 1 Data Model: Foundation

Entities drawn from `spec.md`'s Key Entities section, made concrete per `research.md`'s
layering decisions. `Organization`, `ConnectionSecret`, and `ActivityLogEntry` are true
`OpenFarm.Domain` entities; the Operator (User) Account is modeled via ASP.NET Core Identity
in `OpenFarm.Infrastructure` (see `research.md` §1) and referenced from `Domain` only by a
scalar `Guid` id, never a navigation property.

## Organization

`OpenFarm.Domain/Organizations/Organization.cs`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Fixed well-known value for the single seeded org (enables idempotent first-boot seeding — see FirstBootSeeder in `plan.md`) |
| `Name` | `string` | Non-empty |
| `CreatedAtUtc` | `DateTimeOffset` | Set once at creation |

- **Relationships**: Every tenant-scoped entity (`ConnectionSecret`, `ActivityLogEntry`, and
  every entity later phases add) carries an `OrganizationId` FK to this row.
- **Lifecycle**: Created exactly once by `FirstBootSeeder` on first boot (FR-003). No
  update/delete operations exist in this phase — org management UI is explicitly out of scope
  (per spec Assumptions).
- **Validation**: Uniqueness enforced by the fixed `Id` + a `DbUpdateException`-as-no-op
  idempotency check in the seeder (not a DB-level uniqueness constraint beyond the PK, since
  exactly one row is the invariant, not "unique names").

## ConnectionSecret

`OpenFarm.Domain/ConnectionSecrets/ConnectionSecret.cs`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | Tenant-scoped (Finbuckle `.IsMultiTenant()`) |
| `PrinterSerialNumber` | `string` | Non-secret identifying field — safe to render in full (FR-007) |
| `EncryptedAccessCode` | `string` | Ciphertext only, via `IDataProtector` (FR-006) — never a plaintext field anywhere on this type |
| `CreatedAtUtc` | `DateTimeOffset` | |

- **Relationships**: Belongs to one `Organization`. Will later be referenced by `Printer`
  (Phase 1 of the feature roadmap) — no FK exists yet since `Printer` doesn't exist.
- **State/Lifecycle**: **Immutable after creation** (FR-013) — enforced structurally: the
  type has a `Create(...)` factory/constructor and *no setters, no `Update` method, anywhere*.
  Replacing a credential means creating a new `ConnectionSecret` row; there is no delete or
  edit path in this phase either (not required by the spec, and adding one would need
  explicit design for what happens to the old row — deferred).
- **Validation**: `PrinterSerialNumber` and the plaintext access code (pre-encryption, at the
  `CreateConnectionSecretCommand` boundary) are non-empty — enforced by
  `CreateConnectionSecretCommandValidator` (TDD, per Constitution Check).
- **Display rule**: Any DTO projecting this entity (`ConnectionSecretResult`) exposes
  `PrinterSerialNumber` in full and a constant masked string (e.g. `"***"`) in place of the
  access code — never the decrypted value. The one path that decrypts (`ISecretProtector.Unprotect`)
  is called only by the future printer-connector integration, not by any read/list endpoint.

## ActivityLogEntry

`OpenFarm.Domain/ActivityLog/ActivityLogEntry.cs`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | Tenant-scoped |
| `EntityType` | `string` | e.g. `"ConnectionSecret"` — string, not FK, because it must reference entity types other phases introduce later |
| `EntityId` | `string` | The affected entity's id, stringified |
| `EventType` | `string` | e.g. `"Created"` |
| `ActorId` | `Guid?` | Nullable — `null` when `ActorType == System` |
| `ActorType` | `ActorType` (enum: `Operator`, `System`) | |
| `TimestampUtc` | `DateTimeOffset` | |

- **Relationships**: Belongs to one `Organization`. No FK to the affected entity (polymorphic
  by design — see `EntityType`/`EntityId` above) and no FK to the actor (Domain has no `User`
  type to reference; `ActorId` is resolved to a display name only at the read/DTO boundary via
  `IActivityLogReader`, implemented in `Infrastructure`, which is allowed to join `AspNetUsers`).
- **Lifecycle**: Append-only. No update or delete operation exists — a log is a record of what
  happened, not a mutable object.
- **Write path**: Only ever written by `Api/Processors/ActivityLogPostProcessor.cs`, globally
  registered against all mutating-verb (POST/PUT/PATCH/DELETE) FastEndpoints requests whose
  DTO implements `IActivityLoggable`. This is what makes "every status change is logged"
  structural (Principle I / Security section) rather than a per-endpoint opt-in.
- **Failure semantics**: If a state-changing action fails validation or is rejected, no entry
  is written (FR-011) — the post-processor only runs after a successful mutation. If writing
  the entry itself fails, that failure must be visible (spec Edge Case) — not silently
  swallowed; the post-processor lets the write exception propagate rather than catching it.
- **Index**: composite `(OrganizationId, TimestampUtc)` for ordered, tenant-scoped retrieval
  (FR-010); secondary `(EntityType, EntityId)` for entity-scoped lookups later phases will need.

## Operator (User) Account

Modeled via ASP.NET Core Identity, not a `Domain` entity (see `research.md` §1).

| Concept | Where |
|---|---|
| `ApplicationUser : IdentityUser<Guid>` | `OpenFarm.Infrastructure/Identity/ApplicationUser.cs` |
| `ApplicationRole : IdentityRole<Guid>` | `OpenFarm.Infrastructure/Identity/ApplicationRole.cs` (added now even though unused, to avoid an Identity-table migration diff when Phase 4's real roles land) |

- **Provisioning**: Exactly one seeded account, created idempotently by `FirstBootSeeder`
  from configuration (`Seed:AdminUsername` / `Seed:AdminEmail` / `Seed:AdminPassword`),
  check-then-create via `UserManager.FindByNameAsync`. Login is by `UserName` (not `Email`)
  so the seeded username can be a bare string like `admin` — `Email` is still populated and
  unique (`RequireUniqueEmail = true`) but only `UserName` is used to authenticate. No
  self-registration endpoint exists (FR-001).
- **Authorization level**: Administrator-equivalent by default this phase — the real
  permission catalog doesn't exist yet (FR-005's coarse "authenticated or not" gate is the
  only check), but the seeded identity is intended to carry full capability once Phase 4
  lands the real catalog.
- **Session**: JWT issued by `POST /api/v1/auth/login`, validated via
  `AddAuthentication().AddJwtBearer(...)`. Claims include `sub` (user id) and `org`
  (organization id) — `org` isn't consumed by the static tenant resolver yet but is present
  for Phase 20's claim-based resolution to read later (Principle IV).

## Authorization Policy (placeholder)

Not a data entity — a code-level policy shim.

- `Api/Security/PermissionAttribute.cs` — decorates endpoints with a permission string
  (e.g. `"connection-secrets:create"`).
- `Api/Security/PermissionAuthorizationHandler.cs` — Phase 0's handler ignores the string and
  checks only `context.User.Identity?.IsAuthenticated == true` (FR-005). Phase 4 replaces the
  handler's internals with a real permission-catalog lookup; no endpoint attribute changes.

## State Diagram Note

No entity in this phase has a true state machine (that's Phase 1 of the feature roadmap's
`PrinterStatus`). The closest thing to a lifecycle is `ConnectionSecret`'s one-way
create-only path, which is captured above as a structural immutability constraint rather
than a transition diagram.
