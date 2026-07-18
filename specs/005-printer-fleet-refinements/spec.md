# Feature Specification: Printer Fleet Management Refinements

**Feature Branch**: `005-printer-fleet-refinements`

**Created**: 2026-07-17

**Status**: Draft

**Input**: User description: "Follow-up refinements identified after 002-printer-integration and 004-virtual-printer: (1) allow editing all printer info except brand/modelCode/serialNumber, replacing the connection-only edit endpoint; (2) move edit/remove actions out of the fleet table/grid and onto the printer detail page only; (3) populate the Add-printer dialog's Brand/Model catalog from the backend instead of a hardcoded frontend list; (4) revisit the real-time (SignalR) printer event contract to confirm it stays brand-neutral as more printer brands/models are added."

## Clarifications

### Session 2026-07-17

- Q: Should editing (name/connection) or removing a printer be restricted based on its current status (e.g., blocked while actively Printing), or always allowed regardless of status? → A: Always allowed regardless of status — matches today's removal behavior (FR-006) exactly; the same policy now applies to editing too, so there is one consistent rule across both actions and every printer state.
- Q: Should a rejected zero-trust edit attempt (FR-010 — client sends a mismatched brand/model/serial) write an activity-log/audit entry, or just return a validation error with no special logging? → A: Validation error only, no special logging — treated like any other rejected/invalid request; no new logging path is introduced for this feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Edit a printer's name alongside its connection details (Priority: P1)

An operator notices a printer was registered with an unclear name, or its LAN address changed after a
router reset. Today they can only fix the IP address/access code (via a connection-only edit); they
cannot rename the printer at all. They need one edit action that lets them update the printer's name and
connection details together, while its brand, model, and serial number — which identify the physical
device — stay fixed for the printer's lifetime.

**Why this priority**: This is the most concrete, immediately valuable gap — renaming a printer is
currently impossible anywhere in the product, a basic fleet-management capability every other entity in
the system already has.

**Independent Test**: Open a registered printer's edit action, change its name (and optionally its IP
address/access code), save, and confirm the fleet list and detail view both reflect the new name — while
its brand, model, and serial number are unchanged and not editable anywhere in the form.

**Acceptance Scenarios**:

1. **Given** a registered printer, **When** an operator opens its edit action and changes only the name,
   **Then** the change saves immediately without requiring a fresh connection verification.
2. **Given** a registered printer, **When** an operator changes its IP address or access code (with or
   without also changing the name), **Then** the same verify-before-save gate used at registration applies
   before the save can succeed.
3. **Given** the edit form, **When** an operator looks for a way to change the printer's brand, model, or
   serial number, **Then** no such control exists — those fields are always displayed as read-only or
   omitted entirely.
4. **Given** an edit that changes the IP address or access code, **When** the save succeeds, **Then** the
   printer's live connection reconnects using the new details, exactly as today's connection edit already
   does.
5. **Given** a printer that is currently Printing, **When** an operator edits its name or connection
   details, **Then** the edit proceeds exactly as it would for an Idle or AwaitingClear printer — status
   is never a blocker for editing, matching how removal already behaves today.
6. **Given** an edit request submitted directly (bypassing the product UI) with a brand, model, or serial
   number value different from the printer's current one, **When** the server processes it, **Then** it
   rejects the request outright — it does not silently ignore the mismatched fields and save the rest of
   the edit, and it does not trust that the UI alone prevents the change.

---

### User Story 2 - Manage a printer only from its detail page (Priority: P2)

An operator scanning the fleet list wants to see status at a glance without accidentally triggering a
destructive action from a dense table row. Edit and Remove currently live in both the fleet table/grid row
menu and (edit only) the detail page. Consolidating both actions onto the detail page removes the
accidental-click risk and gives removal a proper, deliberate confirmation step in context.

**Why this priority**: A safety and clarity improvement rather than new capability — it depends on User
Story 1 only in that the detail page's edit action should already support the broadened field set, not
the reverse.

**Independent Test**: Open the fleet list (table or grid view) and confirm no row exposes an edit or
remove control; open a printer's detail page and confirm both actions are available there, each fully
functional.

**Acceptance Scenarios**:

1. **Given** the fleet list in table view, **When** an operator inspects a row, **Then** it offers no edit
   or remove control — only navigation to the detail page (and the existing "Mark ready" action where
   applicable, which is not a destructive or editing action).
2. **Given** the fleet list in grid view, **When** an operator inspects a card, **Then** the same holds —
   no edit or remove control on the card itself.
3. **Given** a printer's detail page, **When** an operator uses its Remove action, **Then** they see the
   same confirmation and consequences as today's fleet-list removal (not blocked by print status, writes
   an activity log entry) and are returned to the fleet list afterward.
4. **Given** a printer's detail page, **When** an operator uses its edit action, **Then** it opens the
   same broadened edit flow from User Story 1.

---

### User Story 3 - Populate the Add-printer catalog from the backend (Priority: P3)

A developer adds a new printer brand or model to the backend catalog and expects it to become selectable
in the Add-printer dialog without touching frontend code. Today the dialog's Brand and Model lists are a
hardcoded frontend constant that must be manually kept in sync with the backend's own catalog.

**Why this priority**: A maintainability/consistency fix with no end-user-visible behavior change (the
dialog looks and behaves the same) — lower priority than the two user-facing UX changes above.

**Independent Test**: Add a new model code to the backend catalog (no frontend change), reload the
Add-printer dialog, and confirm the new model appears as a selectable option under its brand.

**Acceptance Scenarios**:

1. **Given** the Add-printer dialog is opened, **When** it loads, **Then** its Brand options and each
   brand's Model options match exactly what the backend's printer catalog currently defines.
2. **Given** a brand/model pair is added to (or removed from) the backend catalog, **When** the
   Add-printer dialog is next opened, **Then** the change is reflected with no frontend code change.
3. **Given** the catalog is temporarily unavailable, **When** the dialog opens, **Then** it shows a clear
   loading/error state rather than an empty or broken Brand/Model selector.

---

### User Story 4 - Confirm the real-time event contract stays brand-neutral (Priority: P4)

As more printer brands and models are added to the catalog, the team wants confidence that the real-time
status/telemetry/camera broadcast contract (and the frontend code that consumes it) has no hidden
assumption baked in from being built against a single brand — without restructuring anything that isn't
actually broken.

**Why this priority**: This is a verification/audit activity, not new user-facing capability; it only
produces code changes if the audit actually finds a brand-specific assumption, which is why it is lowest
priority and explicitly bounded in scope (see Assumptions).

**Independent Test**: Review every field and event name in the real-time printer event contract and its
frontend consumer; confirm each is expressed in terms of the canonical printer status/telemetry model,
with nothing keyed on a specific brand or model.

**Acceptance Scenarios**:

1. **Given** the real-time event contract (status, telemetry, camera-frame events) and its frontend
   consumer, **When** reviewed, **Then** no field, event name, or branch is found that assumes a specific
   printer brand or model.
2. **Given** the review finds no brand-specific assumption, **When** the feature ships, **Then** no change
   to the event contract, hub, or frontend subscription code is made (confirmation alone satisfies this
   story).
3. **Given** the review does find a brand-specific assumption, **When** it is fixed, **Then** the fix
   generalizes the contract to the canonical model without introducing any brand-specific branch to
   replace it.

---

### Edge Cases

- What happens if an operator submits the edit form with the name unchanged and only the IP
  address/access code changed? The same verify-before-save gate applies, exactly as today's
  connection-only edit already behaves.
- What happens if an operator navigates directly to a printer's detail page via a bookmarked/shared URL
  and immediately tries to remove it? The Remove action works identically regardless of navigation path,
  since it is now the only place this action exists.
- What happens to a printer already registered under a brand/model that is later removed from the backend
  catalog? The existing printer is unaffected — the catalog change only affects what is selectable for
  new registrations going forward.
- What happens if the Add-printer dialog's catalog fetch fails after a Brand is already selected mid-form?
  The dialog surfaces the error and disables submission rather than silently falling back to a stale or
  empty list.
- What happens if an edit request is crafted outside the product UI (e.g., a direct API call) with a
  brand, model, or serial number that doesn't match the printer's existing value? The server rejects the
  whole request — it never partially applies an edit whose brand/model/serial fields don't match, and it
  never assumes only the UI-generated request shape can reach this operation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a single edit operation for a registered printer covering its
  display name and its connection details (IP address, access code), replacing today's connection-only
  edit flow. Editing MUST be available regardless of the printer's current status (Idle, Printing, or
  AwaitingClear) — status is never a blocker, matching FR-006's removal policy.
- **FR-002**: A printer's brand, model, and serial number MUST remain immutable after registration — no
  product UI control or API operation may change them.
- **FR-003**: Changing only a printer's name MUST NOT require a fresh connection verification; changing
  its IP address or access code MUST still require the same verify-before-save gate used at registration.
- **FR-004**: A successful edit that changes IP address or access code MUST cause the printer's live
  connection to reconnect with the new details, matching today's connection-edit behavior.
- **FR-005**: Edit and Remove actions for a printer MUST be available only from that printer's detail
  page; the fleet list (table and grid views) MUST NOT expose either action on its rows/cards.
- **FR-006**: The printer detail page MUST provide a Remove action equivalent to today's fleet-list
  removal — same confirmation step, not blocked by the printer's current status, and the same activity-log
  entry on success.
- **FR-007**: The Add-printer dialog's Brand and Model options MUST be populated from a backend-exposed
  printer catalog rather than a hardcoded frontend list, so adding a brand/model to the backend catalog
  requires no frontend code change to become selectable.
- **FR-008**: The backend-exposed printer catalog MUST reflect the same (brand, model) pairs used to
  validate printer registration, so the dialog can never offer a combination the backend would reject.
- **FR-009**: The real-time printer event contract (status, telemetry, camera-frame broadcasts) and its
  frontend consumer MUST be reviewed to confirm no field, event name, or branch assumes a specific printer
  brand or model; any such assumption found MUST be generalized to the canonical model as part of this
  feature.
- **FR-010**: The server MUST NOT trust the product UI alone to enforce FR-002's immutability constraint.
  If an edit request supplies a brand, model, or serial number value that differs from the printer's
  current one — regardless of what sent it — the server MUST reject the entire request rather than
  silently ignoring the mismatched fields and applying the rest of the edit. This rejection MUST surface
  as a standard validation error like any other invalid request — it does not require a dedicated
  activity-log or security-audit entry beyond that.

### Key Entities *(include if feature involves data)*

- **Printer**: Existing managed printer entity. Its name becomes editable after registration; its brand,
  model, and serial number remain fixed for its lifetime, unchanged from today.
- **Printer Catalog**: The set of (brand, model) combinations the system accepts for registration —
  already exists as backend validation data; this feature adds a way for the frontend to read it rather
  than duplicate it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can rename a printer and/or update its connection details through one edit
  action, without switching between separate name-editing and connection-editing flows.
- **SC-002**: Zero paths exist anywhere in the product (UI or API) to change a printer's brand, model, or
  serial number after registration.
- **SC-003**: The fleet list (both table and grid views) contains zero edit or remove controls; every edit
  or remove action is performed from the printer detail page.
- **SC-004**: A printer can be removed entirely from its detail page, with no need to return to the fleet
  list first.
- **SC-005**: Adding a new brand/model to the backend catalog makes it selectable in the Add-printer
  dialog with zero frontend code changes.
- **SC-006**: A review of the real-time event contract and its frontend consumer finds zero brand- or
  model-specific fields, event names, or branches outside the connector layer.
- **SC-007**: Sending an edit request directly (outside the product UI) with a brand, model, or serial
  number that doesn't match the printer's current value is rejected by the server 100% of the time — never
  partially applied, never silently ignored.

## Assumptions

- The unified edit operation replaces today's connection-only edit flow; its exact API shape is a
  planning-level decision, not specified here.
- Removing a printer from its detail page returns the operator to the fleet list afterward, matching the
  outcome of today's fleet-list removal.
- The existing `printers:edit-connection` and `printers:remove` permission gates continue to apply to the
  broadened edit and the relocated remove action; no new permission is introduced by this feature.
- The backend-exposed printer catalog requires no new permission beyond what an authenticated user
  already needs to reach the Add-printer dialog (i.e., `printers:register`-level access).
- User Story 4 is a verification activity bounded by the constitution's "extraction happens at the second
  real need, not speculatively" principle — it does not preemptively add abstractions for hypothetical
  future brands; it only fixes a concretely-found brand-specific assumption, if one exists.
- Existing fleet-list "Mark ready" and row-click-to-open-detail behaviors are unaffected — only Edit and
  Remove move to the detail page.
