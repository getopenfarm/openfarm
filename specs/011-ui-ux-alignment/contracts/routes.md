# Contract: Routes added/changed in `web/src/app/routes.tsx`

No backend/HTTP contracts change in this feature (see plan.md Constraints).
The "interfaces this project exposes" for a frontend-only feature are its
routes and shared component props — documented here instead of an OpenAPI/CLI
contract.

| Route | Component | Mode/props | Permission gate | Replaces |
|---|---|---|---|---|
| `/printers/new` | `PrinterFormPage` | `mode="add"` | Same permission `AddPrinterDialog` already required | `AddPrinterDialog` |
| `/printers/:id/edit` | `PrinterFormPage` | `mode="edit"`, `id` from route param | Same permission `EditPrinterDialog` already required | `EditPrinterDialog` |
| `/spools/new` | `SpoolFormPage` | `mode="add"` | Same permission `AddSpoolDialog` already required | `AddSpoolDialog` |
| `/parts/new` | `PartFormPage` | (add-only; no `mode` prop needed since this page never handles `replace`) | Same permission `AddOrReplacePartDialog` (`add` mode) already required | `AddOrReplacePartDialog` (`add` mode only — `replace` mode is untouched) |
| `/profiles/import` | `ProfilesImportPage` | n/a | Same permission `ImportProfilesDialog` already required | `ImportProfilesDialog` |
| `/settings/connection-secrets/new` | `ConnectionSecretFormPage` | n/a | Same permission the inline add form already required | Inline `Dialog` in `ConnectionSecretsPage.tsx` |

All six follow the existing recipe in `docs/UI_CONVENTIONS.md` § Recipe:
`PageHeader` + `BackLink` header, sticky `FormFooter` Cancel/Save bar, and on
success: `toast.success(...)`, TanStack Query cache invalidation, then
`navigate('/<feature>')`.

## Contract: `ConfirmDialog`

`web/src/components/confirm-dialog/ConfirmDialog.tsx`

```ts
interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel: string;
  onConfirm: () => void;
}
```

Consumers: `PartDetailPage.tsx` ("Delete part"), `PrinterDetailPage.tsx`
("Delete printer"). Renders on top of `web/src/components/ui/alert-dialog.tsx`.
Cancel/Escape/outside-click closes without calling `onConfirm`.

## Contract: `useUnsavedChangesPrompt`

`web/src/hooks/useUnsavedChangesPrompt.ts`

```ts
function useUnsavedChangesPrompt(isDirty: boolean): {
  /** Render this alongside the form when true; wires to the blocker's proceed/reset. */
  ConfirmLeaveDialog: () => JSX.Element | null;
  /** Call synchronously right before an intentional post-save navigate() so that
   * navigation isn't blocked by the form's now-stale (pre-mutation) dirty state. */
  markClean: () => void;
};
```

Behavior:
- While `isDirty` is `true`, in-app navigation (back button, `<Link>` clicks,
  programmatic `navigate()`) is intercepted via `useBlocker`, rendering
  `ConfirmDialog` with a "Discard unsaved changes?" prompt; confirming calls
  the blocker's `proceed()`, cancelling calls `reset()`.
- While `isDirty` is `true`, a `beforeunload` listener is attached so tab
  close/refresh/address-bar navigation shows the browser's native warning;
  removed when `isDirty` becomes `false` or the component unmounts.
- `useBlocker`'s shouldBlock check is a stable, ref-backed function rather
  than the raw `isDirty` boolean — a mutation's `onSuccess` calling
  `markClean()` immediately before its own `navigate()` therefore takes
  effect on that very navigation, with no render round-trip in between (a
  plain per-render boolean would still read the pre-save `true` at that
  point, since React hasn't re-rendered yet when `onSuccess` runs).

Consumers: every page listed in the routes table above, plus
`FilamentFormPage.tsx` and `AssemblyFormPage.tsx`.

## Contract: `usePartFileProcessing`

`web/src/features/parts/components/usePartFileProcessing.ts`

Extracted, unchanged-behavior hook encapsulating the continuous
background-processing logic currently inline in `AddOrReplacePartDialog.tsx`
(`add` mode). Both `AddOrReplacePartDialog.tsx` (`replace` mode's shared
upload-progress display, if applicable) and the new `PartFormPage.tsx` import
it — no duplication of the async processing logic. Exact input/output shape
is carried over from the current dialog implementation (see research.md); no
behavior change.
