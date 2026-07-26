# Feature Specification: UI/UX Convention Alignment (Page-first migration)

**Feature Branch**: `011-ui-ux-alignment`

**Created**: 2026-07-25

**Status**: Draft

**Input**: User description: "I want optimize/modify the current UI according to @../docs/UI_CONVENTIONS.md to have better UX. Please go through current code base again, since there are some changes since we write the convention."

## Clarifications

### Session 2026-07-25

- Q: FR-010 requires the Assembly form to persist through the real API, but no backend Assembly API exists (no controller, no endpoints, no generated client). What should this feature require for Assemblies? → A: Drop the "real API" clause from FR-010; convert the Assembly form to react-hook-form + zod now (still on the mock store), deferring real-API wiring to future work once a backend Assembly API exists.
- Q: FR-014's unsaved-changes guard lists "back button, nav link, closing tab" together, but tab-close/refresh can only be guarded by the browser's native `beforeunload` warning (distinct mechanism from an in-app router guard). Should this feature cover both? → A: Both — a custom styled confirmation for in-app SPA navigation (back button, sidebar links, Cancel), plus the native browser `beforeunload` warning for tab close/refresh/URL bar navigation.

## Codebase re-scan summary

`docs/UI_CONVENTIONS.md` was written to end the drift between routed-page and
modal-dialog treatments of Add/Edit flows. Re-checking the codebase against
its classification table today shows:

- The migrations the doc already called out are **still outstanding**: Add/Edit
  Printer, Add Spool, Add Part, Import Profiles, and Add Connection Secret are
  all still modals.
- Filament and Assembly form pages are still on raw `useState`, not
  react-hook-form + zod as the doc's companion rule requires; the Assembly
  form is also still backed by mock stores instead of the real API.
- The `alert-dialog` primitive the doc flagged as "not yet added" is **still
  missing** from `web/src/components/ui/`.
- **New drift since the doc was written**: a second `window.confirm(...)` call
  was introduced in `printers/PrinterDetailPage.tsx` (delete printer) — the
  doc only called out the one in `parts/PartDetailPage.tsx`. The anti-pattern
  has spread rather than shrunk.
- Jobs and Maintenance "New" actions remain non-functional stubs (toast "not
  implemented yet", backed by mock stores) — there is no real create flow to
  classify yet, so they are excluded from this feature (see Assumptions).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Consistent, styled confirmation for destructive actions (Priority: P1)

An operator deleting a part or a printer sees the same styled, on-brand
confirmation dialog used elsewhere in the app, instead of the browser's native
`confirm()` popup (which can't be styled, is inconsistent across browsers, and
blocks all other page interaction including reading the item being deleted).

**Why this priority**: Destructive-action safety is the highest-risk
interaction in the app (irreversible data loss), touches two entities today,
and is the smallest, most self-contained piece of work — a single shared
primitive fixes both existing occurrences and prevents a third from
reappearing.

**Independent Test**: Can be fully tested by opening the Part detail page and
the Printer detail page, triggering delete on each, and confirming a styled
in-app dialog (not a native browser popup) appears and correctly
confirms/cancels the deletion.

**Acceptance Scenarios**:

1. **Given** an operator is on a Part detail page, **When** they click
   "Delete part", **Then** a styled confirmation dialog appears in-page (not a
   native browser popup) and confirming deletes the part.
2. **Given** an operator is on a Printer detail page, **When** they click
   "Delete printer", **Then** a styled confirmation dialog appears in-page
   and confirming deletes the printer.
3. **Given** either confirmation dialog is open, **When** the operator
   dismisses it (Cancel, Escape, or clicking outside), **Then** no deletion
   occurs and the dialog closes.

---

### User Story 2 - Add/Edit flows are deep-linkable pages (Priority: P2)

An operator adding or editing a Printer, adding a Spool, adding a Part,
importing Profiles, or adding a Connection Secret gets a full routed page —
bookmarkable, shareable, and resumable after a refresh — matching the
experience Filament and Assemblies already provide, instead of a modal that
loses its state and URL context.

**Why this priority**: This is the core of the documented convention and the
largest source of inconsistency operators currently notice; it depends on
nothing else in this feature and delivers value flow-by-flow.

**Independent Test**: For each of Printers, Spools, Parts, and Profiles, can
be fully tested by navigating to the feature's list page, starting the
Add (or Import) flow, confirming the browser URL changes to a dedicated route,
refreshing the browser mid-flow, and confirming the operator lands back on the
same routed page rather than losing the flow.

**Acceptance Scenarios**:

1. **Given** an operator is on the Printers list, **When** they choose "Add
   printer", **Then** the app navigates to `/printers/new` as a full page (not
   a modal) with the shared page header and sticky Cancel/Save footer.
2. **Given** an operator opens an existing printer's edit action, **When** the
   flow starts, **Then** the app navigates to `/printers/:id/edit` as a full
   page.
3. **Given** an operator is on the Spools list, **When** they choose "Add
   spool", **Then** the app navigates to `/spools/new` as a full page.
4. **Given** an operator is on the Parts list, **When** they choose "Add
   part", **Then** the app navigates to `/parts/new` as a full page, and the
   uploaded file continues processing in the background exactly as it does in
   today's modal while the operator edits name/tags.
5. **Given** an operator is on the Profiles list, **When** they choose "Import
   profiles", **Then** the app navigates to `/profiles/import` as a full page.
6. **Given** an operator is on Settings → Connection Secrets, **When** they
   choose "Add credential", **Then** the app navigates to
   `/settings/connection-secrets/new` as a full page instead of expanding an
   inline form.
7. **Given** an operator has unsaved input on any of the pages above, **When**
   they attempt to navigate away within the app (back button, sidebar/nav
   link, Cancel), **Then** a custom styled confirmation dialog prompts before
   discarding the unsaved changes.
8. **Given** an operator has unsaved input on any of the pages above, **When**
   they close the tab, refresh, or type a new URL in the address bar, **Then**
   the browser's native unsaved-changes warning appears before the page
   unloads.
9. **Given** any of the modals explicitly reserved by the convention (feed
   unit, variant, vendor quick-create, part picker, load/unload/record-usage,
   replace-part-file, profile raw-data view), **When** an operator opens them,
   **Then** they still behave as modals — this feature does not change them.

---

### User Story 3 - Consistent form validation on Filament and Assembly pages (Priority: P3)

An operator filling out the Filament or Assembly add/edit form gets the same
validation timing, error messages, and styling as every other form in the app
(react-hook-form + zod), instead of the ad-hoc `useState` + manual-toast
validation those two pages use today.

**Why this priority**: Lower risk and narrower blast radius than P1/P2 (no
routing change, purely internal to two existing pages), but leaves a visible
inconsistency (different error behavior) if left undone.

**Independent Test**: Can be fully tested by opening `/filament/new` and
`/assemblies/new`, submitting with invalid/missing required fields, and
confirming validation errors appear inline per-field in the same style and
timing as the Printer/Spool/Part forms delivered in User Story 2.

**Acceptance Scenarios**:

1. **Given** an operator on `/filament/new` submits the form with a required
   field empty, **When** validation runs, **Then** an inline field-level error
   appears in the same style used by other RHF+zod forms in the app.
2. **Given** an operator on `/assemblies/new` submits the form with a required
   field empty, **When** validation runs, **Then** an inline field-level error
   appears the same way.
3. **Given** an operator successfully submits either form, **When** the save
   completes, **Then** the filament form persists through the real API, and
   the assembly form persists through the mock assemblies/parts store (no
   backend Assembly API exists yet — real-API wiring is deferred future work).

---

### Edge Cases

- What happens if an operator opens a migrated Add/Edit URL directly (deep
  link or bookmark) without the permission required for that action? The page
  MUST enforce the same permission check the entity's other actions already
  enforce, consistent with existing route protection.
- What happens if an operator is mid-way through the Add Part page (file still
  processing in the background) and navigates away? Per the unsaved-changes
  prompt (User Story 2, Scenario 7), the operator is warned before losing the
  in-progress add; confirming the navigation cancels the pending add, matching
  today's behavior when the equivalent modal is closed mid-processing.
- What happens to the two existing `window.confirm` call sites if an operator
  has JavaScript-level browser dialogs suppressed/automated (e.g. testing
  tools)? The replacement alert-dialog is in-page DOM, not a browser-level
  dialog, so it is unaffected by browser dialog suppression.
- What happens on the Connection Secrets page once "Add credential" becomes a
  page — does the list page's existing masked-secret display change? No; only
  the add action's presentation changes, the list and its masking are
  unaffected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a reusable `alert-dialog` UI primitive for
  confirming destructive actions, replacing native `window.confirm`.
- **FR-002**: The "Delete part" action on the Part detail page MUST use the
  alert-dialog primitive instead of `window.confirm`.
- **FR-003**: The "Delete printer" action on the Printer detail page MUST use
  the alert-dialog primitive instead of `window.confirm`.
- **FR-004**: Add Printer and Edit Printer MUST be presented as routed pages
  (`/printers/new`, `/printers/:id/edit`) instead of modal dialogs.
- **FR-005**: Add Spool MUST be presented as a routed page (`/spools/new`)
  instead of a modal dialog.
- **FR-006**: Add Part MUST be presented as a routed page (`/parts/new`)
  instead of a modal dialog, while preserving the existing behavior of
  continuing file processing in the background while the operator edits
  name/tags.
- **FR-007**: Import Profiles MUST be presented as a routed page
  (`/profiles/import`) instead of a modal dialog.
- **FR-008**: Adding a connection secret/credential MUST be presented as a
  routed page (`/settings/connection-secrets/new`) instead of the current
  inline form on the Connection Secrets list page.
- **FR-009**: The Filament add/edit form MUST use react-hook-form with a zod
  validation schema instead of raw `useState`-managed fields and manual toast
  validation.
- **FR-010**: The Assembly add/edit form MUST use react-hook-form with a zod
  validation schema instead of raw `useState`-managed fields. It MAY continue
  persisting through the existing mock assemblies/parts stores — wiring it to
  a real backend Assembly API is out of scope for this feature since no such
  API exists yet.
- **FR-011**: Every page migrated or touched by this feature MUST use the
  shared scaffolding already established by Filament/Assemblies (`PageHeader`
  + `BackLink` header, sticky `FormFooter` Cancel/Save bar).
- **FR-012**: On successful create/update from any page in scope, the system
  MUST show a success toast, invalidate the relevant cached query data, and
  navigate back to the feature's list page.
- **FR-013**: The modals explicitly reserved by `docs/UI_CONVENTIONS.md`
  (confirmations already using alert-dialog, quick actions on an existing
  entity, and in-form pickers/sub-entity add) MUST remain unchanged as modals.
- **FR-014**: If an operator attempts to navigate away from any Add/Edit page
  in scope while it has unsaved changes, the system MUST prompt for
  confirmation before discarding them: a custom styled confirmation dialog for
  in-app navigation (back button, sidebar/nav links, Cancel), and the
  browser's native `beforeunload` warning for tab close, refresh, or address
  bar navigation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every top-level Add/Edit flow for Printers, Spools, Parts, and
  Profiles-import is reachable via its own bookmarkable URL, matching the
  Filament and Assemblies flows.
- **SC-002**: Zero native `window.confirm()` popups remain anywhere in the
  app; every destructive-action confirmation uses the same in-app dialog.
- **SC-003**: An operator can refresh the browser mid-flow on any migrated
  Add/Edit page and land back on that same page rather than losing the flow,
  for 100% of the flows in scope.
- **SC-004**: The Filament and Assembly forms produce identical inline
  validation-error timing and styling to the other RHF+zod forms in the app,
  verified by a manual QA pass with no visual/interaction discrepancy.
- **SC-005**: An operator attempting to leave any in-scope Add/Edit page with
  unsaved input is prompted before losing changes, 100% of the time — whether
  leaving via in-app navigation or via tab close/refresh/address-bar
  navigation.

## Assumptions

- Jobs' and Maintenance's "New" actions are excluded from this feature: both
  are currently non-functional stubs (toast "not implemented yet") backed by
  mock data stores with no real backend create flow to classify as page or
  modal yet. They will be classified per `docs/UI_CONVENTIONS.md` when their
  real create flow is built in a later phase.
- The in-form pickers, quick-action dialogs, and inspect dialogs already
  classified as correct modals in `docs/UI_CONVENTIONS.md` (feed unit,
  variant, vendor quick-create, part picker, load/unload/record-usage,
  replace-part-file, profile raw-data view) are out of scope — this feature
  only touches the flows the doc lists as needing migration, plus the two
  `window.confirm` call sites found during the re-scan.
- "Add Part" keeps its documented continuous background-processing behavior
  (spec `007-part-library`) once moved to a page; this is a presentation
  change only, not a behavior change.
- The unsaved-changes-on-navigation prompt (FR-014) is a net-new UX
  improvement not explicitly in `docs/UI_CONVENTIONS.md` today, added because
  converting these flows from modals (which already blocked background
  navigation) to full pages introduces a new way to accidentally navigate away
  and lose input.
- This feature is delivered as one coordinated effort rather than the
  incremental "next time touched" migration `docs/UI_CONVENTIONS.md` normally
  allows, per the user's explicit request to address the drift now.
- No backend Assembly API exists (no controller, no endpoints, no generated
  Orval client — unlike Filament/Parts/Printers). Wiring the Assembly form to
  a real API is therefore explicit future work, not part of this feature; the
  mock assemblies/parts store remains the persistence layer for now.
