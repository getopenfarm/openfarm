# UI Conventions — Add/Edit flows (Page-first)

This document is the authoritative rule for **how a create or edit flow is
presented in the web app**: a routed full page vs. a modal dialog. It exists
because the app drifted into both patterns for the same kind of task (Filament
and Assemblies use full pages; Printers, Spools, Parts, Profiles, Connection
Secrets use modals), which is confusing to operators and to contributors.

**The rule is Page-first.** Read this before building any new "Add", "New", or
"Edit" flow.

## The rule

Creating or editing a first-class entity is a **routed full page**:

```
/<feature>/new          → add
/<feature>/:id/edit     → edit
```

Modals are the **exception**, reserved for exactly the three cases below.

### When to use a full page (the default)

Use a page whenever the flow is a top-level "Add \<Entity\>" or "Edit
\<Entity\>" reached from a list/index or detail page. Pages are:

- **Deep-linkable and resumable** — the URL is the state.
- **Roomy** — multi-section forms, nested child collections, and summary
  sidebars fit without cramming.
- **Consistent** — built from the shared scaffolding (see recipe below).

Examples that are correctly pages today: Add/Edit **Filament**
(`web/src/features/filament/FilamentFormPage.tsx`), New/Edit **Assembly**
(`web/src/features/assemblies/AssemblyFormPage.tsx`).

### When a modal is allowed (the only exceptions)

1. **Confirmations / destructive actions.** e.g. delete confirmations. Use an
   `alert-dialog` primitive rather than the native `window.confirm`
   (`PartDetailPage.tsx` currently uses `window.confirm` — replace when
   touched).
2. **Quick actions on an existing entity** that are *not* a full edit of it.
   e.g. Load / Unload / Record-usage on a spool
   (`spools/components/*Dialog.tsx`), Replace file on a part
   (`AddOrReplacePartDialog` in `replace` mode), verify a connection.
3. **In-form pickers / sub-entity add** that must preserve the parent form's
   unsaved state — navigating away would discard it. e.g. the assembly
   `PartPickerDialog`, filament `VariantDialog`, printer `AddFeedUnitDialog`,
   inline `CreateVendorDialog`. These operate *inside* a page that is itself
   the entity being created, so they stay modals.

Quick test: **if the flow is a top-level "Add \<Entity\>" from a list, it is a
page. If it acts on or within an already-open entity, it may be a modal per the
cases above.**

## Companion rule: form implementation

All entity forms — page or modal — use **react-hook-form + zod**, matching the
documented stack and `AddPrinterDialog` today. Do not hand-roll `useState` +
manual `toast(...)` validation.

Current outliers to convert when next touched: `FilamentFormPage.tsx` and
`AssemblyFormPage.tsx` (both on raw `useState`; the latter also still uses mock
stores).

## Recipe: adding a new create/edit flow

1. **Routes** — in `web/src/app/routes.tsx`, add children under the feature:

   ```tsx
   {
     path: '/<feature>',
     handle: { title: '<Feature>' },
     children: [
       { index: true, element: <FeaturePage /> },
       { path: 'new',      element: <FeatureFormPage mode="add" />,  handle: { title: 'New <Entity>' } },
       { path: ':id/edit', element: <FeatureFormPage mode="edit" />, handle: { title: 'Edit <Entity>' } },
     ],
   }
   ```

2. **Scaffolding** — build the page from the shared components:
   - `PageHeader` + `BackLink` (`web/src/components/page-header`,
     `web/src/components/back-link`) for the header row.
   - `FormFooter` (`web/src/components/form-footer/FormFooter.tsx`) for the
     sticky Cancel / Save bar.
3. **State** — react-hook-form + zod resolver.
4. **On success** — `toast.success(...)`, invalidate the relevant TanStack
   Query key, and `navigate('/<feature>')`.

## Classification of existing flows

Migration is **incremental** — bring a misaligned flow into conformance the
next time it is meaningfully touched, not in a big-bang refactor.

| Flow | File | Status |
|---|---|---|
| Add/Edit filament | `filament/FilamentFormPage.tsx` | Page ✔ (convert form to RHF+zod) |
| New/Edit assembly | `assemblies/AssemblyFormPage.tsx` | Page ✔ (convert form to RHF+zod; wire real API) |
| Add feed unit | `printers/components/AddFeedUnitDialog.tsx` | Modal ✔ (in-form sub-entity) |
| Add variant | `filament/components/VariantDialog.tsx` | Modal ✔ (in-form sub-entity) |
| Create vendor | `filament/components/CreateVendorDialog.tsx` | Modal ✔ (inline quick-create) |
| Part picker | `assemblies/components/PartPickerDialog.tsx` | Modal ✔ (in-form picker) |
| Load/Unload/Record usage | `spools/components/{Load,Unload,RecordUsage}*Dialog.tsx` | Modal ✔ (quick actions) |
| Replace part file | `parts/components/AddOrReplacePartDialog.tsx` (`replace`) | Modal ✔ (quick action) |
| View raw profile data | `profiles/components/ProfileRawDataDialog.tsx` | Modal ✔ (inspect) |
| Delete part | `parts/PartDetailPage.tsx` (`window.confirm`) | → `alert-dialog` |
| Add printer | `printers/components/AddPrinterDialog.tsx` | Migrate → `/printers/new` |
| Edit printer | `printers/components/EditPrinterDialog.tsx` | Migrate → `/printers/:id/edit` |
| Add spool | `spools/components/AddSpoolDialog.tsx` | Migrate → `/spools/new` |
| Add part | `parts/components/AddOrReplacePartDialog.tsx` (`add`) | Migrate → `/parts/new` — see note |
| Import profiles | `profiles/components/ImportProfilesDialog.tsx` | Migrate → `/profiles/import` |
| Add credential | inline in `connection-secrets/ConnectionSecretsPage.tsx` | Migrate → `/settings/connection-secrets/new` |
| New job | `jobs/JobsPage.tsx` (stub) | Build as `/jobs/new` |
| New maintenance task | `maintenance/MaintenancePage.tsx` (stub) | Build as `/maintenance/new` |

**Note on Add Part.** Spec `007-part-library` deliberately specced Add-Part as a
single continuous dialog that keeps processing the uploaded file in the
background while the operator types name/tags. That behaviour works identically
on a page — the migration is a conscious call, not an oversight. Preserve the
continuous background-processing UX when moving it to `/parts/new`.

## Missing primitives this standard implies

- `alert-dialog` in `web/src/components/ui/` — for confirmations, replacing
  `window.confirm`. Not yet added.
