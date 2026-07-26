---

description: "Task list for UI/UX Convention Alignment (Page-first migration)"
---

# Tasks: UI/UX Convention Alignment (Page-first migration)

**Input**: Design documents from `/specs/011-ui-ux-alignment/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/routes.md, quickstart.md

**Tests**: Colocated Vitest + React Testing Library tests are required per `plan.md` Testing
Context and the project's own convention (every existing page/component has a `*.test.tsx`
sibling) — but this is test-after work (Constitution II: React pages are explicitly not a
TDD-required area), so test tasks are sequenced **after** their implementation task, not before.

**Organization**: Tasks are grouped by user story (US1/US2/US3, matching spec.md priorities).
This is a frontend-only change entirely inside `web/src/`; no `api/` changes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps the task to US1, US2, or US3
- Every task includes exact file path(s)

## Path Conventions

All paths are relative to `web/src/` unless stated otherwise (repo root: `/Users/tanghaoquan/code/openfarm`).

---

## Phase 1: Setup

**Purpose**: Confirm the workspace is ready; no new dependencies or project scaffolding needed.

- [X] T001 Run `cd web && bun install` and confirm the build is currently green (`bunx tsc -b`, `bun run lint`, `bun run test`) before making changes, to establish a clean baseline. No new packages are required — `radix-ui@^1.6.5` already re-exports `AlertDialog` (verified: `node_modules/radix-ui/dist/index.d.ts` exports `reactAlertDialog as AlertDialog`).

**Checkpoint**: Baseline verified green. Proceed to Foundational.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared primitives that US1 needs directly and that US2/US3 need transitively
(`useUnsavedChangesPrompt`, built in Phase 4, renders this same `ConfirmDialog`). No user story
work should start before this phase is done.

- [X] T002 [P] Add `web/src/components/ui/alert-dialog.tsx`: a shadcn/radix-ui primitive following the exact composition pattern already used by `web/src/components/ui/dialog.tsx` (`import { AlertDialog as AlertDialogPrimitive } from 'radix-ui'`), exporting `AlertDialog`, `AlertDialogTrigger`, `AlertDialogPortal`, `AlertDialogOverlay`, `AlertDialogContent`, `AlertDialogHeader`, `AlertDialogFooter`, `AlertDialogTitle`, `AlertDialogDescription`, `AlertDialogAction`, `AlertDialogCancel` — reuse `cn` from `@/lib/utils` and `Button` styles from `@/components/ui/button` for `AlertDialogAction`/`AlertDialogCancel` exactly as `dialog.tsx` does for its close button.
- [X] T003 [US1] Add `web/src/components/confirm-dialog/ConfirmDialog.tsx`: a thin wrapper over `alert-dialog.tsx` (T002) implementing the `ConfirmDialogProps` contract from `specs/011-ui-ux-alignment/contracts/routes.md` (`open`, `onOpenChange`, `title`, `description`, `confirmLabel`, `onConfirm`) — `AlertDialogCancel` cancels without calling `onConfirm`, `AlertDialogAction` calls `onConfirm` then closes.
- [X] T004 [P] Add `web/src/components/confirm-dialog/ConfirmDialog.test.tsx`: render open with sample props, assert title/description text renders, clicking the confirm action calls `onConfirm` once, clicking cancel calls `onOpenChange(false)` without calling `onConfirm`.

**Checkpoint**: `ConfirmDialog` exists and is unit-tested. US1 can now proceed; US2/US3 can proceed once Phase 4's `useUnsavedChangesPrompt` (built at the start of US2) also depends on this same component.

---

## Phase 3: User Story 1 - Consistent, styled confirmation for destructive actions (Priority: P1) 🎯 MVP

**Goal**: Replace both `window.confirm` call sites (Part delete, Printer delete) with the shared
`ConfirmDialog` from Phase 2, so zero native browser confirms remain (SC-002).

**Independent Test**: Open a Part detail page and a Printer detail page, trigger delete on each,
confirm a styled in-page dialog appears (not a native popup), and confirm/cancel behave correctly.

### Implementation for User Story 1

- [X] T005 [US1] In `web/src/features/parts/PartDetailPage.tsx`: replace the `window.confirm(...)` call in `handleDelete` (currently around line 50) with local `confirmOpen` state and a rendered `<ConfirmDialog>` (title "Delete part?", description including the part name, confirmLabel "Delete", `onConfirm` calling `deleteMutation.mutate({ id: part.id })`); remove the now-unused `window.confirm` branch logic entirely.
- [X] T006 [P] [US1] Update `web/src/features/parts/PartDetailPage.test.tsx` (if it exists) or create it: assert clicking "Delete part" opens the dialog (not `window.confirm`), confirming triggers the delete mutation and navigation to `/parts`, and cancelling leaves the part untouched.
- [X] T007 [US1] In `web/src/features/printers/PrinterDetailPage.tsx`: replace the `window.confirm(...)` call in `handleRemove` (currently around line 69) with local `confirmOpen` state and a rendered `<ConfirmDialog>` (title "Remove printer?", description mentioning removal isn't blocked by current print status, confirmLabel "Remove", `onConfirm` calling `removeMutation.mutate({ id: printerId })`); remove the now-unused `window.confirm` branch logic entirely.
- [X] T008 [P] [US1] Update `web/src/features/printers/PrinterDetailPage.test.tsx`: assert clicking "Delete printer"/"Remove" opens the dialog (not `window.confirm`), confirming triggers the remove mutation and navigation to `/printers`, and cancelling leaves the printer untouched.
- [X] T009 [US1] Run `grep -rn "window.confirm" web/src` and confirm zero matches remain (SC-002).

**Checkpoint**: User Story 1 is fully functional and independently testable/shippable — this is the MVP slice.

---

## Phase 4: User Story 2 - Add/Edit flows are deep-linkable pages (Priority: P2)

**Goal**: Migrate the six remaining modal-based Add/Edit flows (Printer add, Printer edit, Spool
add, Part add, Profiles import, Connection-secret add) to routed pages per
`specs/011-ui-ux-alignment/contracts/routes.md`, each reusing its existing dialog's schema and
mutation logic unchanged (research.md), with the shared `PageHeader`/`BackLink`/`FormFooter`
scaffolding and the unsaved-changes guard.

**Independent Test**: For each of Printers, Spools, Parts, and Profiles: navigate to the feature's
list page, start Add (or Import), confirm the URL changes to the dedicated route, refresh mid-flow,
and confirm the operator lands back on the same routed page.

### Shared hook for User Story 2 (blocks every flow below)

- [X] T010 [US2] Add `web/src/hooks/useUnsavedChangesPrompt.ts` implementing the contract in `specs/011-ui-ux-alignment/contracts/routes.md`: takes `isDirty: boolean`, uses React Router v8's `useBlocker` (available because `web/src/app/routes.tsx` uses `createBrowserRouter`, a data router) to intercept in-app navigation and render the shared `ConfirmDialog` (T003) with a "Discard unsaved changes?" prompt (confirm → blocker `proceed()`, cancel → blocker `reset()`), plus a `window.addEventListener('beforeunload', ...)` registered/unregistered as `isDirty` changes for tab-close/refresh/address-bar navigation. Returns `{ ConfirmLeaveDialog }` per the contract.
- [X] T011 [P] [US2] Add `web/src/hooks/useUnsavedChangesPrompt.test.tsx`: assert that with `isDirty=true`, attempting in-app navigation renders the confirm dialog and only proceeds on confirm; with `isDirty=false`, navigation is not blocked; assert the `beforeunload` listener is attached only while `isDirty=true` (spy on `addEventListener`/`removeEventListener`).

### Printer add/edit → `/printers/new`, `/printers/:id/edit`

- [X] T012 [US2] Create `web/src/features/printers/PrinterFormPage.tsx` accepting `mode: 'add' | 'edit'`: for `mode="add"`, lift the form schema, catalog-driven brand/model refine logic, verify-before-add flow, and `useOpenFarmApiEndpointsPrintersRegisterPrinterEndpoint` mutation verbatim from `web/src/features/printers/components/AddPrinterDialog.tsx`; for `mode="edit"`, lift the equivalent logic from `web/src/features/printers/components/EditPrinterDialog.tsx` (reading `id` via `useParams`); wrap both in the `PageHeader` + `BackLink` header and sticky `FormFooter` Cancel/Save bar (`web/src/components/page-header/PageHeader.tsx`, `web/src/components/back-link/BackLink.tsx`, `web/src/components/form-footer/FormFooter.tsx`), call `useUnsavedChangesPrompt(form.formState.isDirty)` (T010) and render its `ConfirmLeaveDialog`; on success: `toast.success(...)`, invalidate `getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey()` (and detail query key for edit), then `navigate('/printers')`.
- [X] T013 [US2] Register routes in `web/src/app/routes.tsx`: add `{ path: 'new', element: <PrinterFormPage mode="add" />, handle: { title: 'Add Printer' } }` and `{ path: ':id/edit', element: <PrinterFormPage mode="edit" />, handle: { title: 'Edit Printer' } }` as children of the existing `/printers` route (alongside the existing `index` and `:id` children); import `PrinterFormPage` at the top of the file.
- [X] T014 [US2] In `web/src/features/printers/PrintersPage.tsx`: replace both `<AddPrinterDialog />` usages (the `action` props, currently around lines 107 and 123) with a `Link to="/printers/new"` styled as the existing "Add printer" button; remove the now-unused `AddPrinterDialog` import.
- [X] T015 [US2] In `web/src/features/printers/components/PrinterDetailActions.tsx` (and any other call site rendering `<EditPrinterDialog />`): replace the dialog trigger with a `Link to={`/printers/${id}/edit`}` styled as the existing "Edit" action.
- [X] T016 [US2] Delete `web/src/features/printers/components/AddPrinterDialog.tsx`, `AddPrinterDialog.test.tsx`, `EditPrinterDialog.tsx`, and `EditPrinterDialog.test.tsx` (logic now lives in `PrinterFormPage.tsx`, T012).
- [X] T017 [P] [US2] Create `web/src/features/printers/PrinterFormPage.test.tsx` covering both modes: renders required fields, catalog-driven brand/model options, verify-before-add gating submit, successful add/edit shows a success toast and navigates to `/printers`, and an unsaved-changes attempt to navigate away triggers the confirm dialog (reuse assertions from the deleted `AddPrinterDialog.test.tsx`/`EditPrinterDialog.test.tsx` where applicable).

### Spool add → `/spools/new`

- [X] T018 [US2] Create `web/src/features/spools/SpoolFormPage.tsx`: lift the form schema and `useForm`/mutation logic verbatim from `web/src/features/spools/components/AddSpoolDialog.tsx`, wrap in `PageHeader` + `BackLink` + sticky `FormFooter`, wire `useUnsavedChangesPrompt` (T010); on success: toast, invalidate the spools list query key, `navigate('/spools')`.
- [X] T019 [US2] Register `{ path: 'new', element: <SpoolFormPage />, handle: { title: 'Add Spool' } }` under a new `/spools` parent route in `web/src/app/routes.tsx` (currently `/spools` is a single leaf route — convert it to a parent with `{ index: true, element: <SpoolsPage /> }` and the new `new` child, matching the `/printers`/`/parts` pattern already in the file).
- [X] T020 [US2] In `web/src/features/spools/SpoolsPage.tsx`: replace both `<AddSpoolDialog />` usages (currently around lines 38 and 47) with a `Link to="/spools/new"` styled as the existing "Add spool" button; remove the now-unused `AddSpoolDialog` import.
- [X] T021 [US2] Delete `web/src/features/spools/components/AddSpoolDialog.tsx` and `AddSpoolDialog.test.tsx` (logic now lives in `SpoolFormPage.tsx`, T018).
- [X] T022 [P] [US2] Create `web/src/features/spools/SpoolFormPage.test.tsx`: required-field validation, successful submit shows toast and navigates to `/spools`, unsaved-changes guard triggers on navigate-away attempt.

### Part add → `/parts/new`

- [X] T023 [US2] Extract `web/src/features/parts/components/usePartFileProcessing.ts` from the `add`-mode logic currently inline in `web/src/features/parts/components/AddOrReplacePartDialog.tsx` — the continuous background file-processing behavior required by spec `007-part-library` (upload, poll/subscribe for processing status, expose progress/result state) — with no behavior change, per `research.md` Decision "Split Part's add/replace modes".
- [X] T024 [US2] Update `web/src/features/parts/components/AddOrReplacePartDialog.tsx` to consume `usePartFileProcessing` (T023) for its `add` mode instead of the inlined logic, keeping `replace` mode's existing behavior untouched; keep the dialog itself as the `replace`-mode UI only going forward (add-mode dialog JSX will be removed once `PartFormPage.tsx` lands in T025).
- [X] T025 [US2] Create `web/src/features/parts/PartFormPage.tsx`: add-only page consuming `usePartFileProcessing` (T023) plus the name/tags form fields from `AddOrReplacePartDialog.tsx`'s `add` mode, wrapped in `PageHeader` + `BackLink` + sticky `FormFooter`, wired with `useUnsavedChangesPrompt` (T010) — per spec Edge Cases, confirming navigate-away while a file is still processing cancels the pending add, matching today's modal-close behavior; on success: toast, invalidate the parts list query key, `navigate('/parts')`.
- [X] T026 [US2] In `web/src/features/parts/PartsPage.tsx`: remove the `add`-mode `<AddOrReplacePartDialog mode="add" ... />` usage and its `addOpen` state (currently around lines 21-22 and 79-80), replacing the "Add part" trigger with a `Link to="/parts/new"`; keep the `replace`-mode dialog usage elsewhere untouched.
- [X] T027 [US2] Register `{ path: 'new', element: <PartFormPage />, handle: { title: 'Add Part' } }` as a new child of the existing `/parts` route in `web/src/app/routes.tsx`, alongside the existing `index` and `:id` children.
- [X] T028 [P] [US2] Create `web/src/features/parts/PartFormPage.test.tsx`: file upload kicks off background processing that continues while name/tags are edited, successful submit shows toast and navigates to `/parts`, unsaved-changes guard triggers mid-processing per the spec's Edge Cases.
- [X] T029 [P] [US2] Update `web/src/features/parts/components/AddOrReplacePartDialog.test.tsx` to reflect `replace`-mode-only coverage (remove/relocate any `add`-mode-specific assertions now covered by T028) and add `web/src/features/parts/components/usePartFileProcessing.test.tsx` covering the extracted hook's processing/progress/result states in isolation.

### Profiles import → `/profiles/import`

- [X] T030 [US2] Create `web/src/features/profiles/ProfilesImportPage.tsx`: lift the form/upload/mutation logic verbatim from `web/src/features/profiles/components/ImportProfilesDialog.tsx`, wrap in `PageHeader` + `BackLink` + sticky `FormFooter`, wire `useUnsavedChangesPrompt` (T010); on success: toast, invalidate the profiles list query key, `navigate('/profiles')`.
- [X] T031 [US2] Register `{ path: 'import', element: <ProfilesImportPage />, handle: { title: 'Import Profiles' } }` under a new `/profiles` parent route in `web/src/app/routes.tsx` (currently `/profiles` is a single leaf route — convert it to a parent with `{ index: true, element: <ProfilesPage /> }` and the new `import` child).
- [X] T032 [US2] In `web/src/features/profiles/ProfilesPage.tsx`: replace all three `<ImportProfilesDialog />` usages (currently around lines 131, 146, 163) with a `Link to="/profiles/import"` styled as the existing "Import profiles" button; remove the now-unused `ImportProfilesDialog` import.
- [X] T033 [US2] Delete `web/src/features/profiles/components/ImportProfilesDialog.tsx` and `ImportProfilesDialog.test.tsx` (logic now lives in `ProfilesImportPage.tsx`, T030).
- [X] T034 [P] [US2] Create `web/src/features/profiles/ProfilesImportPage.test.tsx`: required-field/file validation, successful submit shows toast and navigates to `/profiles`, unsaved-changes guard triggers on navigate-away attempt.

### Connection secret add → `/settings/connection-secrets/new`

- [X] T035 [US2] Create `web/src/features/connection-secrets/ConnectionSecretFormPage.tsx`: lift the inline form schema, `useForm`, and create-mutation logic verbatim from the `Dialog`/`DialogContent` block in `web/src/features/connection-secrets/ConnectionSecretsPage.tsx` (currently lines ~73-160), wrap in `PageHeader` + `BackLink` + sticky `FormFooter`, wire `useUnsavedChangesPrompt` (T010); on success: toast, invalidate the connection-secrets list query key, `navigate('/settings/connection-secrets')`.
- [X] T036 [US2] Register `{ path: 'new', element: <ConnectionSecretFormPage />, handle: { title: 'Add Connection Secret' } }` as a new child under the existing `/settings/connection-secrets` route in `web/src/app/routes.tsx` (convert the current single-element `connection-secrets` leaf into a parent with `{ index: true, element: <ConnectionSecretsPage /> }` and the new `new` child).
- [X] T037 [US2] In `web/src/features/connection-secrets/ConnectionSecretsPage.tsx`: remove the inline `Dialog`/`DialogContent`/`DialogTrigger` block and its `open`/`form` state (lines ~73-160), replacing the "Add credential" trigger with a `Link to="/settings/connection-secrets/new"`; remove now-unused `Dialog*`, `useForm`, `zodResolver` imports if nothing else in the file needs them.
- [X] T038 [P] [US2] Update `web/src/features/connection-secrets/ConnectionSecretsPage.test.tsx`: remove assertions about the inline dialog, add an assertion that "Add credential" is a link to `/settings/connection-secrets/new`; create `web/src/features/connection-secrets/ConnectionSecretFormPage.test.tsx` covering required-field validation, successful submit (toast + invalidate + navigate to the list), and the unsaved-changes guard.

### Reserved-modal regression check for User Story 2

- [X] T039 [US2] Manually verify (per `quickstart.md` step 8) that `AddFeedUnitDialog`, `VariantDialog`, `CreateVendorDialog`, `PartPickerDialog`, `LoadSpoolDialog`/`UnloadSpoolDialog`/`RecordUsageDialog`, the `replace`-mode `AddOrReplacePartDialog`, and `ProfileRawDataDialog` still open as unchanged modals — no file changes expected, this is a confirmation task only.

**Checkpoint**: All six flows are routed pages; `AddPrinterDialog`, `EditPrinterDialog`,
`AddSpoolDialog`, `ImportProfilesDialog`, and the inline connection-secret dialog no longer exist.
User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Consistent form validation on Filament and Assembly pages (Priority: P3)

**Goal**: Convert `FilamentFormPage.tsx` and `AssemblyFormPage.tsx` from raw `useState` +
manual-toast validation to react-hook-form + zod, matching every other form delivered in US2, per
the field shapes documented in `data-model.md`.

**Independent Test**: Open `/filament/new` and `/assemblies/new`, submit with invalid/missing
required fields, confirm inline field-level errors appear in the same style/timing as the
Printer/Spool/Part forms from US2.

### Implementation for User Story 3

- [X] T040 [P] [US3] Rewrite `web/src/features/filament/FilamentFormPage.tsx` from raw `useState` fields to react-hook-form + zod: schema per `data-model.md` (`vendorId`, `material`, `materialType` enum from `MATERIAL_TYPES`, `filamentProfileId: string | null`), using `Form`/`FormField`/`FormItem`/`FormLabel`/`FormControl`/`FormMessage` from `@/components/ui/form` (the same primitives `AddPrinterDialog.tsx` uses) instead of manual toast validation; keep the existing `VariantsCard`/`VariantDialog` sub-entity flow and the existing filament create/update mutation calls unchanged; add `useUnsavedChangesPrompt(form.formState.isDirty)` (T010) and render its `ConfirmLeaveDialog`.
- [X] T041 [P] [US3] Update `web/src/features/filament/FilamentFormPage.test.tsx`: replace assertions tied to the old manual-toast validation with inline field-error assertions matching the style used in `PrinterFormPage.test.tsx`/`SpoolFormPage.test.tsx` (T017/T022); keep existing coverage of successful submit persisting via the real filament API and the unsaved-changes guard.
- [X] T042 [P] [US3] Rewrite `web/src/features/assemblies/AssemblyFormPage.tsx` from raw `useState` fields to react-hook-form + zod: schema per `data-model.md` (`name` required non-empty, `description: string | undefined`, `components: AssemblyComponent[]`); keep the existing `PartPickerDialog` sub-entity flow and the existing `mockAssembliesStore`/`mockPartsStore` persistence calls unchanged (no backend Assembly API exists — FR-010, out of scope); add `useUnsavedChangesPrompt(form.formState.isDirty)` (T010) and render its `ConfirmLeaveDialog`.
- [X] T043 [P] [US3] Update `web/src/features/assemblies/AssemblyFormPage.test.tsx`: replace assertions tied to the old manual-toast validation with inline field-error assertions matching the RHF+zod style; keep existing coverage of successful submit persisting via the mock assemblies/parts store and add coverage for the unsaved-changes guard.

**Checkpoint**: All in-scope forms (Printer, Spool, Part, Profiles, Connection-secret, Filament,
Assembly) share identical validation timing/styling. All three user stories are independently
functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature verification against the spec's Success Criteria.

- [X] T044 Run `cd web && bunx tsc -b && bun run lint` and fix any resulting errors across all files touched in Phases 2-5.
- [X] T045 Run `cd web && bun run test` and confirm all colocated tests pass, including every new/updated `*.test.tsx` from Phases 2-5.
- [X] T046 Walk through every scenario in `specs/011-ui-ux-alignment/quickstart.md` manually against `bun run dev` (US1 styled-confirmation checks, all six US2 deep-link + refresh-mid-flow checks, the unsaved-changes in-app + `beforeunload` checks, the reserved-modals regression check, and the US3 Filament/Assembly inline-validation checks) and confirm every "Expect" line holds.
- [X] T047 Run `grep -rn "window.confirm" web/src` one final time and confirm zero matches (SC-002), and confirm no remaining imports of the six deleted dialog components anywhere in `web/src` (`AddPrinterDialog`, `EditPrinterDialog`, `AddSpoolDialog`, `ImportProfilesDialog`, plus the removed inline connection-secret `Dialog` block).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. Blocks US1 (needs `ConfirmDialog` directly) and blocks US2/US3 transitively (their `useUnsavedChangesPrompt`, built at the start of Phase 4, renders the same `ConfirmDialog`).
- **User Story 1 (Phase 3)**: Depends on Phase 2 only. Fully independent of US2/US3 — ships as the MVP slice.
- **User Story 2 (Phase 4)**: Depends on Phase 2. Its first task (T010, `useUnsavedChangesPrompt`) blocks every flow-specific task in the same phase. The six flows (Printer, Spool, Part, Profiles, Connection-secret) are independent of each other once T010 lands and can proceed in parallel.
- **User Story 3 (Phase 5)**: Depends on Phase 2 (`ConfirmDialog`) and on Phase 4's T010 (`useUnsavedChangesPrompt`) — this is the one cross-story dependency in the feature, matching the P2-before-P3 priority order. Does not depend on any of US2's six flow migrations themselves.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### Parallel Opportunities

- T002 (alert-dialog) has no dependencies; T004 (ConfirmDialog test) can run alongside T003 once T003 lands.
- Within Phase 4, after T010/T011 land, the five flow groups (Printer: T012-T017; Spool: T018-T022; Part: T023-T029; Profiles: T030-T034; Connection-secret: T035-T038) touch entirely different files and can be worked in parallel.
- Within Phase 5, T040/T041 (Filament) and T042/T043 (Assembly) touch entirely different files and can run in parallel.
- Test tasks marked `[P]` alongside their implementation task can run once that implementation task's file is stable, in parallel with other stories' work.

---

## Parallel Example: User Story 2 (after T010/T011)

```bash
# Five independent flow migrations, each touching only its own feature folder:
Task: "Printer add/edit → PrinterFormPage.tsx (T012-T017)"
Task: "Spool add → SpoolFormPage.tsx (T018-T022)"
Task: "Part add → PartFormPage.tsx + usePartFileProcessing (T023-T029)"
Task: "Profiles import → ProfilesImportPage.tsx (T030-T034)"
Task: "Connection secret add → ConnectionSecretFormPage.tsx (T035-T038)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational — `alert-dialog` + `ConfirmDialog`).
2. Complete Phase 3 (US1): both `window.confirm` sites replaced.
3. **STOP and VALIDATE**: `grep -rn "window.confirm" web/src` returns zero matches; manually confirm both dialogs in the browser.
4. This is shippable independently — SC-002 is fully satisfied without touching US2/US3.

### Incremental Delivery

1. Setup + Foundational → Foundation ready.
2. US1 (Phase 3) → validate → ship (destructive-action safety fixed).
3. US2 (Phase 4) → validate each of the six flows against `quickstart.md` → ship (Page-first migration complete, SC-001/SC-003/SC-005 satisfied).
4. US3 (Phase 5) → validate Filament/Assembly inline validation → ship (SC-004 satisfied, feature complete).
5. Phase 6 (Polish) → final whole-feature verification against all five Success Criteria.

### Parallel Team Strategy

With multiple developers:

1. One developer completes Phase 1 + Phase 2 (small, fast, unblocks everyone).
2. Developer A takes US1 (Phase 3) — smallest, ships first.
3. Developer B takes US2's T010/T011, then the team splits the five flow groups (Printer/Spool/Part/Profiles/Connection-secret) across developers in parallel.
4. Developer C takes US3 (Phase 5) once T010 lands, in parallel with US2's flow groups (US3 doesn't depend on any specific US2 flow, only on the shared hook).

---

## Notes

- No `api/` (.NET) changes anywhere in this feature — every task is inside `web/src/`.
- Every migrated flow keeps its existing mutation calls, query keys, and validation rules per `plan.md` Constraints — tasks above say "lift ... verbatim" deliberately; rewriting working validation logic during the move is explicit scope creep to avoid.
- `AddOrReplacePartDialog.tsx`'s `replace` mode, and every modal listed in `docs/UI_CONVENTIONS.md` as reserved (feed unit, variant, vendor quick-create, part picker, load/unload/record-usage, replace-part-file, profile raw-data view), must NOT be touched beyond T024's internal refactor to consume the extracted hook.
- Commit after each task or logical group (per flow in US2, per file in US3).
- Verify tests pass after each task; this feature is test-after, not TDD, per Constitution II.
