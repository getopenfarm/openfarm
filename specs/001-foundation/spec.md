# Feature Specification: Foundation — Authenticated Access, Organization Scaffolding, Secured Credentials, Activity History, and a Base UI Kit

**Feature Branch**: `001-foundation`

**Created**: 2026-07-12

**Status**: Draft

**Input**: User description: "Create specifications based on docs/FEATURE_PLAN.md" — scoped to Phase 0 (Foundation) per user clarification, including a base UI kit setup.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator logs in and reaches the app (Priority: P1)

A single, pre-provisioned operator account can sign in with real credentials and land in an authenticated area of the application. That seeded account is provisioned at administrator level — the highest privilege tier the system will eventually support — even though the fine-grained permission catalog itself isn't built until a later phase. There is no self-registration and no role-management UI yet — this install has exactly one operator until later phases add more.

**Why this priority**: Nothing else in the system is reachable or attributable to anyone without this. It is the first thing that must work, and it's also what makes the activity history (User Story 4) meaningful — every logged action needs a real actor behind it.

**Independent Test**: Can be fully tested by attempting to sign in with the seeded operator's credentials and confirming access to an authenticated screen, and separately confirming an unauthenticated request to a protected area is rejected.

**Acceptance Scenarios**:

1. **Given** a freshly provisioned install with its seeded operator account, **When** the operator signs in with the correct credentials, **Then** they reach an authenticated area of the application.
2. **Given** the seeded operator account, **When** someone attempts to sign in with incorrect credentials, **Then** access is denied and no session is created.
3. **Given** no active session, **When** a request is made to a protected area, **Then** the request is rejected rather than silently allowed.

---

### User Story 2 - Every install has exactly one organization automatically (Priority: P1)

On first boot, a self-hosted install already has one organization in place — no setup wizard, no manual "create your organization" step. Every record created afterward (printers, jobs, spools, everything else the roadmap will add) is automatically associated with that organization.

**Why this priority**: This is the structural seam the entire multi-tenancy story depends on. Every future tenant-scoped table needs an organization to attach to from the moment it's created — retrofitting this after data exists means a migration touching every row and a real risk of missing one. It has to exist before or alongside login (P1), not after.

**Independent Test**: Can be fully tested by starting a fresh install and confirming exactly one organization exists without any manual action, then creating any tenant-scoped record and confirming it is associated with that organization automatically.

**Acceptance Scenarios**:

1. **Given** a brand-new install with no prior data, **When** the system starts for the first time, **Then** exactly one organization already exists with no manual setup step.
2. **Given** an install that has already completed first boot, **When** the system restarts, **Then** no additional organization is created — still exactly one.
3. **Given** the single organization exists, **When** any tenant-scoped record is created, **Then** it is associated with that organization without the operator having to specify it.

---

### User Story 3 - Printer connection credentials are stored securely, masked wherever shown, and never editable in place (Priority: P2)

An operator can store a printer's real network access credential (needed to connect to physical hardware later in the roadmap) and trust that its secret value is never recoverable in plain text — not from storage, not from logs, not from an API response, and not from any future UI that displays it. Non-secret identifying fields tied to that credential (e.g. a printer's serial number) may be shown in full, but the access code itself always renders masked (e.g. `***`). Once a credential is stored, it cannot be edited in place — replacing it means creating a new one, not mutating the old value.

**Why this priority**: These rows will hold a working credential to real physical devices, not placeholder data, from the moment printer connectivity is built (the next phase). Storing them unencrypted even briefly creates a real exposure window; retrofitting encryption after credentials already exist in plain form is a much harder migration than building it in from the start. The masking and no-edit rules exist for the same reason — a credential that can be viewed in full or silently mutated in place widens that exposure window every time a UI is built against it.

**Independent Test**: Can be fully tested by storing a credential value, then confirming it cannot be read back in plain form through direct storage inspection, logs, or any API response — only through the one authorized decrypt path a printer connection actually needs — and confirming no operation exists to edit the stored value in place.

**Acceptance Scenarios**:

1. **Given** an operator stores a printer connection credential, **When** the underlying storage is inspected directly, **Then** the credential value is not readable in plain text.
2. **Given** a stored credential, **When** any system log is generated during normal operation, **Then** the credential value never appears in that log in plain text.
3. **Given** a stored credential, **When** it is retrieved through the one authorized path meant to use it, **Then** it is successfully usable — encryption does not silently corrupt or lose the value.
4. **Given** a stored credential with non-secret identifying fields (e.g. a serial number) alongside a secret access code, **When** those fields are shown anywhere, **Then** the identifying fields render in full and the access code always renders masked.
5. **Given** a stored credential, **When** an attempt is made to edit the access code or its identifying fields in place, **Then** no edit operation is available — the only supported path is creating a new credential to replace it.

---

### User Story 4 - Every meaningful change leaves a traceable record (Priority: P2)

Any state-changing action taken through the system — whether initiated by the operator or by a background process — produces a record of what happened, who or what did it, and when. That record can be retrieved later.

**Why this priority**: Later phases (printer status changes, job lifecycle, maintenance logs, analytics) all read from this same history. It needs to already be accumulating data from the first state-mutating action in the system, not bolted on after the fact once operators start asking "what happened here?" and the answer isn't recorded.

**Independent Test**: Can be fully tested by performing any state-changing action and confirming a corresponding record — entity affected, event type, actor, and timestamp — exists and is retrievable afterward.

**Acceptance Scenarios**:

1. **Given** an authenticated operator performs a state-changing action, **When** that action succeeds, **Then** a record capturing the entity, event type, actor, and timestamp is created.
2. **Given** a state-changing action fails validation or is rejected, **When** that happens, **Then** no misleading success record is created.
3. **Given** several actions have been recorded over time, **When** the history is retrieved, **Then** the records are attributable to the correct actor and correctly ordered by time.

---

### User Story 5 - Builders have a consistent, ready-to-use component kit (Priority: P3)

Anyone building a new screen has a base set of styled UI building blocks — buttons, form fields, dialogs, tables, and similar primitives — already available, so every future feature doesn't reinvent its own visual style or interaction pattern.

**Why this priority**: This doesn't block backend work (P1/P2 above), but every user-facing phase from here forward needs it, and building it once up front is far cheaper than each feature inventing its own components and reconciling the inconsistency later.

**Independent Test**: Can be fully tested by assembling a simple screen (e.g. a form with a submit action and a confirmation dialog) using only the existing base components, with no new low-level styling written.

**Acceptance Scenarios**:

1. **Given** the base UI kit is in place, **When** a new screen needs a form, button, or dialog, **Then** it can be built entirely from existing base components.
2. **Given** two different screens both use the same kind of component (e.g. a button), **When** they are compared, **Then** they look and behave consistently.

---

### Edge Cases

- What happens when someone attempts to sign in with an expired or otherwise invalid session token? The request must be rejected the same way as having no session at all.
- What happens if the system is restarted mid-first-boot, before the single organization finishes being created? The next startup must still converge on exactly one organization, not zero or two.
- What happens if a stored credential fails to decrypt (e.g. corrupted data)? The failure must surface clearly to whatever process needed the credential rather than silently proceeding with a bad or empty value.
- What happens if someone attempts to edit a stored connection credential's access code or identifying fields directly? No edit path should exist — this must be surfaced as an unsupported operation, not silently accepted or partially applied.
- What happens if writing an activity history record itself fails during an otherwise-successful state change? This must not be silently swallowed — the failure needs to be visible rather than leaving a gap in the history with no trace it happened.
- What happens if a future screen is built before the base UI kit exists? Out of scope for this phase to prevent, but the ordering dependency should be visible to anyone planning later work.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a single pre-provisioned operator identity, provisioned at administrator level, that can authenticate with real credentials; self-registration and role-management UI are explicitly out of scope for this phase.
- **FR-002**: System MUST reject access to any protected area when no valid, active session is present.
- **FR-003**: System MUST ensure exactly one organization exists for a self-hosted install after first boot, created automatically with no manual setup step, and MUST NOT create a duplicate on subsequent restarts.
- **FR-004**: System MUST associate every tenant-scoped record with an organization at the moment it is created, without requiring the operator to specify one.
- **FR-005**: System MUST apply a coarse authorization check ("is this request authenticated") to state-changing operations; a full permission catalog is explicitly out of scope for this phase and deferred to a later phase.
- **FR-006**: System MUST encrypt printer connection credentials at rest such that the plain-text value is not recoverable from direct storage inspection.
- **FR-007**: System MUST NOT emit a stored credential's plain-text access-code value in logs, API responses, or any UI — at most a masked representation (e.g. `***`) may indicate a credential exists, while non-secret identifying fields associated with it (e.g. a printer's serial number) may be shown in full.
- **FR-008**: System MUST make a stored credential's plain-text value available only through the one authorized path intended to use it.
- **FR-009**: System MUST record every state-changing action with the affected entity, the event type, the responsible actor, and a timestamp, whether the action was initiated by an operator or a background process.
- **FR-010**: System MUST make recorded activity-history entries retrievable afterward, correctly attributed and ordered by time.
- **FR-011**: System MUST NOT record a misleading success entry for an action that failed validation or was rejected.
- **FR-012**: System MUST provide a base library of reusable, consistently styled UI components (at minimum: buttons, form inputs, dialogs, tables) available for any future screen to build from.
- **FR-013**: System MUST NOT provide any way to edit a stored connection credential's fields in place; replacing a credential MUST be a create-new operation, not a mutation of the existing record.

### Key Entities *(include if feature involves data)*

- **Operator (User) Account**: The single seeded identity able to authenticate into the system in this phase, provisioned at administrator level by default. Has credentials and an identity usable as the "actor" on activity-history records.
- **Organization**: The single tenant-scoping unit auto-created on first boot. Every tenant-scoped record created afterward belongs to it.
- **Connection Secret**: A stored, encrypted printer network access credential. Holds a real working credential, not a placeholder, from the moment it's created; readable in plain form only through its one authorized use path. Its secret value always renders masked wherever shown; non-secret identifying fields (e.g. a printer's serial number) may render in full. Immutable once created — no in-place edit exists, only create-new-to-replace.
- **Activity Log Entry**: A record of a single state-changing action — what entity it affected, what kind of event occurred, who or what performed it, and when. Forms the basis of later maintenance/job/analytics history.
- **Authorization Policy (placeholder)**: A coarse, "authenticated or not" gate attached to state-changing operations in this phase, standing in for the real permission catalog that a later phase will introduce.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A newly provisioned operator can sign in and reach an authenticated screen in under 1 minute, on the first attempt, using only their seeded credentials.
- **SC-002**: 100% of state-changing actions performed through the system produce a retrievable activity-history record with a correct actor and timestamp.
- **SC-003**: 0 instances of a stored connection credential's secret access-code value being observable in plain text in storage, logs, or any UI/API response — masked display and non-secret identifying fields excluded — across a full audit of the running system.
- **SC-004**: A fresh install converges on exactly one organization after first boot, verified across at least 3 consecutive restarts with no duplication or loss.
- **SC-005**: A developer can assemble a representative new screen (form + action + confirmation) using only existing base UI components, with zero new low-level styling code required.
- **SC-006**: 100% of attempts to edit a stored connection credential's fields in place are rejected or unavailable, with replacement only possible by creating a new credential.

## Assumptions

- This phase targets a single-organization, self-hosted deployment only; real multi-organization switching and any organization-management UI are out of scope until a much later phase.
- The real permission catalog (multiple roles, scoped permissions) is out of scope for this phase — only a coarse authenticated/not-authenticated gate is required here, matching the roadmap's explicit sequencing.
- No self-registration flow or password-reset flow is required in this phase — the single operator identity is pre-provisioned as part of setup.
- Beyond authenticated login and the base UI kit, this phase is not otherwise user-facing — it exists to make every later, user-facing phase possible without requiring rework.
- The base UI kit is scoped to foundational, reusable primitives (buttons, inputs, dialogs, tables, layout basics); feature-specific components (e.g. a printer status card) belong to the phases that introduce those features.
- The seeded operator account is provisioned at administrator level by default; the full permission catalog (Owner/Admin/Operator/Viewer) still lands in a later phase, but the single seeded identity already carries full administrative capability under this phase's coarse authenticated-only check.
- Connection credentials are immutable once created — rotating one is a replace operation (create-new), never an in-place edit. The Printer entity and its UI (where a serial number and masked access code would actually be rendered together) are built in a later phase, but this display/edit rule is captured now since it governs the Connection Secret data model this phase establishes.
