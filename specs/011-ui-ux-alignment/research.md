# Phase 0 Research: UI/UX Convention Alignment

No unknowns in the Technical Context required external research — this is a
refactor of existing, already-running code inside a monorepo whose stack and
conventions are fully documented (`docs/TECH_STACKS.md`, `docs/UI_CONVENTIONS.md`,
`.specify/memory/constitution.md`). Each decision below was resolved by
inspecting the current codebase rather than external sources.

## Decision: Reuse existing form logic and mutation hooks verbatim; only lift the shell

**Decision**: For Printer, Spool, Part (add), Profiles-import, and
Connection-secret, the existing dialog components already use react-hook-form
+ zod and the correct TanStack Query mutation/invalidation calls
(`AddPrinterDialog.tsx`, `AddSpoolDialog.tsx`, `AddOrReplacePartDialog.tsx`
`add` mode, `ImportProfilesDialog.tsx`, and the inline form in
`ConnectionSecretsPage.tsx` all already call `useForm` +
`zodResolver`). The migration moves this logic into a new page component and
deletes the `Dialog`/`DialogContent` wrapper — it does not rewrite validation,
schemas, or mutation calls.

**Rationale**: Minimizes regression risk on flows that already work
correctly; the doc's own classification table confirms these five are only
mis-classified on presentation (modal vs. page), not on form-implementation
quality. Rewriting working validation logic would be scope creep unjustified
by the spec.

**Alternatives considered**: Rewriting form state from scratch during the
move — rejected, no requirement calls for it and it multiplies the blast
radius of a change that should be presentation-only.

## Decision: `alert-dialog` primitive follows the existing `radix-ui` unified-package pattern

**Decision**: Add `web/src/components/ui/alert-dialog.tsx` using
`import { AlertDialog as AlertDialogPrimitive } from 'radix-ui'`, the same
pattern already used by `web/src/components/ui/dialog.tsx`
(`import { Dialog as DialogPrimitive } from 'radix-ui'`). `radix-ui@^1.6.5`
(already a dependency) bundles the `AlertDialog` primitive, so **no new
package is needed**.

**Rationale**: Constitution III requires "primitives come from shadcn/ui in
`src/components/ui/` uniformly; no feature invents its own component
pattern" — matching the existing `dialog.tsx` import shape is the only
compliant option.

**Alternatives considered**: A hand-rolled confirm modal built on plain divs —
rejected, violates Constitution III and duplicates what Radix already
provides accessibly (focus trap, ESC-to-close, ARIA roles).

## Decision: One shared `ConfirmDialog` component over both delete call sites

**Decision**: Add `web/src/components/confirm-dialog/ConfirmDialog.tsx` as a
thin, reusable wrapper around `alert-dialog.tsx` (props: `title`,
`description`, `confirmLabel`, `onConfirm`, `open`, `onOpenChange`), used by
both `PartDetailPage.tsx` and `PrinterDetailPage.tsx`.

**Rationale**: The doc's own scan already shows the `window.confirm`
anti-pattern spreading from one call site to two since the doc was written; a
single shared component is the structural fix (matching the same rationale
the doc gives for the activity-log post-processor pattern: make the correct
behavior the path of least resistance, not a convention to remember per
call site).

**Alternatives considered**: Inline `AlertDialog` JSX duplicated in both
detail pages — rejected, reproduces exactly the kind of per-call-site drift
this feature exists to close.

## Decision: In-app navigation guard via React Router's `useBlocker`, paired with `beforeunload`

**Decision**: Add `web/src/hooks/useUnsavedChangesPrompt.ts`, a hook that
takes an `isDirty: boolean` and:
1. Calls React Router v8's `useBlocker` (available because
   `web/src/app/routes.tsx` already uses `createBrowserRouter`, a data
   router — `useBlocker` requires a data router and is unavailable outside
   one) to intercept in-app navigation and drive the shared `ConfirmDialog`.
2. Registers a `window.addEventListener('beforeunload', ...)` for tab
   close/refresh/address-bar navigation, per the clarification session's
   answer that both mechanisms are in scope.

**Rationale**: `useBlocker` is the first-party, supported mechanism for this
exact scenario on a data router; `beforeunload` is the only browser-level
hook that exists for the tab-close case and its confirmation text is
browser-controlled (cannot be styled) — this is called out explicitly in the
spec's clarification answer, not a limitation introduced during planning.

**Alternatives considered**: A custom `history.listen`-based guard — rejected,
React Router's data router owns its own history abstraction internally and
`useBlocker` is the documented, supported integration point rather than
reaching around it.

## Decision: Split Part's add/replace modes; extract shared background-processing logic

**Decision**: `AddOrReplacePartDialog.tsx` keeps its `replace` mode as a modal
(per the doc's own classification — quick action on an existing entity). Its
`add` mode logic — most importantly the continuous background file-processing
behavior spec `007-part-library` requires — is extracted into a shared hook
(`usePartFileProcessing.ts`) consumed by both the retained `replace`-mode
dialog and the new `PartFormPage.tsx`.

**Rationale**: The spec (Assumptions) explicitly requires the continuous
background-processing UX to be preserved exactly, not reimplemented; extracting
the shared hook is the only way to guarantee the `add` page and the `replace`
dialog can't drift apart on that specific, tricky async behavior.

**Alternatives considered**: Duplicate the processing logic into the new page
— rejected, directly risks the drift the spec calls out as unacceptable.

## Decision: Assembly form keeps its mock-store persistence

**Decision**: `AssemblyFormPage.tsx` converts to RHF+zod but its submit
handler continues calling the existing
`web/src/features/assemblies/data/mockAssembliesStore.ts` /
`mockPartsStore.ts` functions — unchanged from today.

**Rationale**: Direct output of the spec's clarification session: no backend
Assembly API exists (verified — no controller/entity in `api/`, no generated
client in `web/src/lib/api/generated`), so "persist through the real API" was
explicitly dropped from FR-010.

**Alternatives considered**: N/A — this was a clarification decision, not an
open design choice.
