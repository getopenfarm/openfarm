# Phase 1 Data Model: UI/UX Convention Alignment

This feature introduces no backend entities and no new persisted data shapes
— it is a presentational/structural refactor. The "data model" here documents
the client-side form-value shapes and shared UI-state contracts that the new
pages and hooks operate on. Where a form already has a validated shape today
(Printer, Spool, Part-add, Profiles-import, Connection-secret), that shape is
carried over unchanged from its existing dialog implementation; it is listed
here only for traceability, not redefined.

## Carried-over form schemas (unchanged validation, new shell only)

| Page | Existing schema source (unchanged) | Persistence |
|---|---|---|
| `/printers/new`, `/printers/:id/edit` | `AddPrinterDialog.tsx` / `EditPrinterDialog.tsx` zod schemas | Existing printers API (unchanged) |
| `/spools/new` | `AddSpoolDialog.tsx` zod schema | Existing spools API (unchanged) |
| `/parts/new` | `AddOrReplacePartDialog.tsx` (`add` mode) zod schema, via extracted `usePartFileProcessing` hook | Existing parts API (unchanged) |
| `/profiles/import` | `ImportProfilesDialog.tsx` zod schema | Existing profiles API (unchanged) |
| `/settings/connection-secrets/new` | Inline form schema in `ConnectionSecretsPage.tsx` | Existing connection-secrets API (unchanged) |

## New/changed form schemas

### Filament form (`/filament/new`, `/filament/:id/edit`)

Converts from raw `useState` fields to a zod schema modeling the same fields
already collected today:

- `vendorId: string` (required, non-empty)
- `material: string` (required, non-empty)
- `materialType: enum` (one of the existing `MATERIAL_TYPES`)
- `filamentProfileId: string | null`
- `variants: NewFilamentVariant[]` (managed via the existing `VariantDialog`
  sub-entity modal — unchanged, still a modal per `docs/UI_CONVENTIONS.md`)

Persistence: existing filament API (unchanged).

### Assembly form (`/assemblies/new`, `/assemblies/:id/edit`)

Converts from raw `useState` fields to a zod schema modeling the same fields
already collected today:

- `name: string` (required, non-empty)
- `description: string | undefined`
- `components: AssemblyComponent[]` (managed via the existing `PartPickerDialog`
  sub-entity modal — unchanged, still a modal)

Persistence: **unchanged** — continues through
`web/src/features/assemblies/data/mockAssembliesStore.ts` /
`mockPartsStore.ts` (per clarification: no backend Assembly API exists, real
wiring is future work).

## Shared UI-state contracts (new)

### `ConfirmDialog` state

- `open: boolean`
- `title: string`
- `description: string`
- `confirmLabel: string`
- `onConfirm: () => void`
- `onOpenChange: (open: boolean) => void`

No persistence — purely transient UI state, one instance per detail page
(Part, Printer) at first; reusable by any future destructive action.

### `useUnsavedChangesPrompt` state

- Input: `isDirty: boolean` (sourced from each form's `formState.isDirty`)
- Internal: React Router `useBlocker` blocker state (`'blocked' | 'unblocked' | 'proceeding'`)
- Side effect: a `beforeunload` listener registered/unregistered as `isDirty`
  changes
- Output: renders the shared `ConfirmDialog` when the blocker is in the
  `'blocked'` state; no persisted state, resets on unmount
