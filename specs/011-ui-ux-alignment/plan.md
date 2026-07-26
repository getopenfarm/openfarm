# Implementation Plan: UI/UX Convention Alignment (Page-first migration)

**Branch**: `011-ui-ux-alignment` | **Date**: 2026-07-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/011-ui-ux-alignment/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Bring the six remaining Add/Edit flows still built as modals (Printer add/edit,
Spool add, Part add, Profiles import, Connection-secret add) into conformance
with the Page-first rule in `docs/UI_CONVENTIONS.md`, add the still-missing
`alert-dialog` primitive to replace both `window.confirm` call sites (Part and
Printer delete), and convert the two remaining forms not on react-hook-form +
zod (Filament, Assembly) to the shared pattern. This is a frontend-only,
presentational refactor: every migrated flow keeps its existing mutation
calls, query keys, and validation rules — only the shell (modal vs. routed
page) and, for Filament/Assembly, the form-state mechanism change. No backend
(`api/`) changes are required; the Assembly form's persistence explicitly
stays on the mock store per the `011-ui-ux-alignment` clarification session
(no backend Assembly API exists).

## Technical Context

**Language/Version**: TypeScript ~6.0.3 (`strict: true`), React 19.2.8

**Primary Dependencies**: React Router 8.3 (`createBrowserRouter` data router,
already wired in `web/src/app/routes.tsx`), TanStack Query 5.101, TanStack
Table/Virtual (unaffected by this feature), react-hook-form 7.82 +
`@hookform/resolvers/zod` + zod 4.4, `radix-ui` 1.6.5 (unified package backing
every `web/src/components/ui/*` primitive, including `dialog.tsx` today),
Tailwind CSS, `lucide-react`, `sonner` (toasts), the Orval-generated API client
in `web/src/lib/api/generated`

**Storage**: N/A for this feature — Printer/Spool/Part/Profiles/Connection-secret
pages persist through their existing backend endpoints unchanged; the Assembly
form continues to persist through the existing in-memory mock
assemblies/parts store (`web/src/features/assemblies/data/mock*.ts`) per the
clarification decision, since no backend Assembly API exists yet

**Testing**: Vitest + React Testing Library, test-after (per Constitution II
— React pages/components are explicitly not a TDD-required area), colocated
`*.test.tsx` next to each component/page, matching the existing convention

**Target Platform**: Web browser SPA (Vite dev server locally, static build in
production) — no platform-specific work

**Project Type**: Frontend-only change inside the existing `web/` package of
the OpenFarm monorepo; no `api/` changes

**Performance Goals**: N/A — no new unbounded lists, no new real-time/SignalR
paths; existing SPA responsiveness expectations carry over unchanged

**Constraints**:
- Must not change any HTTP contract, mutation, or TanStack Query key already
  used by a migrated flow — only the presentational shell moves (modal →
  page); the Filament/Assembly conversions change only validation mechanism,
  not the shape of data submitted.
- Must preserve Part-add's continuous background-processing behavior (spec
  `007-part-library`) exactly when lifted from `AddOrReplacePartDialog`
  (`add` mode) into `/parts/new`.
- Must NOT touch the modals `docs/UI_CONVENTIONS.md` explicitly reserves:
  feed unit, variant, vendor quick-create, part picker, load/unload/record-
  usage, replace-part-file (the `replace` mode of `AddOrReplacePartDialog`),
  profile raw-data view.
- New/edited pages must reuse the same permission checks each entity's other
  actions already enforce (no new RBAC rules).

**Scale/Scope**: 6 modal→page migrations (Printer add, Printer edit, Spool
add, Part add, Profiles import, Connection-secret add), 1 new shared UI
primitive (`alert-dialog`), 2 `window.confirm` replacements, 1 new shared
unsaved-changes-guard hook, 2 forms (Filament, Assembly) converted to RHF+zod

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applicability | Assessment |
|---|---|---|
| I. Clean Architecture, Enforced by the Compiler and CI | N/A | No `Domain`/`Application`/`Infrastructure`/`Api` changes — frontend-only feature. |
| II. Test Discipline Where It Pays, Not Uniformly | Applies | React pages/components are explicitly test-after territory, not TDD-required; this plan follows the existing colocated `*.test.tsx` convention rather than inventing a new testing approach. PASS. |
| III. Consistent User Experience Across the Fleet | Applies — this feature directly advances it | Converts the remaining outlier forms to RHF+zod, sources every new page from the same shared `PageHeader`/`BackLink`/`FormFooter` scaffolding and `src/components/ui/` primitives, and adds no feature-invented pattern. PASS. |
| IV. Build for Expansion, Not Speculation | Applies, narrowly | New pages reuse existing permission gates verbatim — no new RBAC rule, no schema/tenancy change. PASS. |
| V. Performance and Real-Time Responsiveness by Construction | N/A | No new unbounded lists, no new real-time wiring. |
| Security & Multi-Tenancy Isolation | Applies, narrowly | Connection-secret encryption/masking/immutability behavior is untouched — only the "add" action's presentation moves from an inline list-page form to a routed page; the same mutation is invoked. PASS. |
| Development Workflow & Quality Gates | Applies | Biome lint/format gate applies (frontend-only PR); no EF Core migration since there is no schema change. PASS. |

No violations identified. Complexity Tracking table is not needed.

**Post-Phase-1 re-check**: The design artifacts (`data-model.md`,
`contracts/routes.md`) introduce three new shared pieces —
`ConfirmDialog`, `useUnsavedChangesPrompt`, `usePartFileProcessing` — all
built from existing primitives (`radix-ui`, React Router's `useBlocker`) and
existing logic lifted unchanged, none of which touch `Domain`/`Application`/
`Infrastructure`/`Api` or introduce a new pattern outside `src/components/ui/`.
Gate re-confirmed: PASS, no new violations.

## Project Structure

### Documentation (this feature)

```text
specs/011-ui-ux-alignment/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
web/src/
├── app/
│   └── routes.tsx                                  # add /printers/new,/printers/:id/edit,
│                                                    # /spools/new, /parts/new, /profiles/import,
│                                                    # /settings/connection-secrets/new
├── components/
│   ├── ui/
│   │   └── alert-dialog.tsx                        # NEW — shadcn/radix-ui primitive
│   ├── confirm-dialog/
│   │   └── ConfirmDialog.tsx                        # NEW — shared wrapper over alert-dialog
│   ├── page-header/PageHeader.tsx                   # reused, unchanged
│   ├── back-link/BackLink.tsx                       # reused, unchanged
│   └── form-footer/FormFooter.tsx                   # reused, unchanged
├── hooks/
│   └── useUnsavedChangesPrompt.ts                   # NEW — useBlocker + beforeunload
├── features/
│   ├── printers/
│   │   ├── PrinterFormPage.tsx                      # NEW — replaces Add/EditPrinterDialog
│   │   ├── PrinterDetailPage.tsx                    # replace window.confirm w/ ConfirmDialog
│   │   └── components/
│   │       ├── AddPrinterDialog.tsx                 # DELETED (logic moved to PrinterFormPage)
│   │       └── EditPrinterDialog.tsx                # DELETED (logic moved to PrinterFormPage)
│   ├── spools/
│   │   ├── SpoolFormPage.tsx                        # NEW — replaces AddSpoolDialog
│   │   └── components/AddSpoolDialog.tsx            # DELETED
│   ├── parts/
│   │   ├── PartFormPage.tsx                          # NEW — add-mode page
│   │   ├── PartDetailPage.tsx                        # replace window.confirm w/ ConfirmDialog
│   │   └── components/
│   │       ├── AddOrReplacePartDialog.tsx            # KEPT, `replace` mode only
│   │       └── usePartFileProcessing.ts              # NEW — shared hook extracted from
│   │                                                  # AddOrReplacePartDialog so page + dialog
│   │                                                  # share identical background-processing logic
│   ├── profiles/
│   │   ├── ProfilesImportPage.tsx                    # NEW — replaces ImportProfilesDialog
│   │   └── components/ImportProfilesDialog.tsx       # DELETED
│   ├── connection-secrets/
│   │   ├── ConnectionSecretFormPage.tsx               # NEW — extracted from inline Dialog
│   │   └── ConnectionSecretsPage.tsx                  # remove inline add Dialog, add nav
│   ├── filament/
│   │   └── FilamentFormPage.tsx                       # REWRITE — raw useState → RHF+zod
│   └── assemblies/
│       └── AssemblyFormPage.tsx                       # REWRITE — raw useState → RHF+zod
│                                                        # (persistence stays on mock store)
└── (test files colocated 1:1 with each file above, per existing convention)
```

**Structure Decision**: Single-project frontend change entirely within the
existing `web/` package (Bun + React 19 + Vite SPA). No new top-level
directories; every new file lands inside the feature folder or shared
`components`/`hooks` directories the codebase already uses. No `api/` (.NET)
changes.

## Complexity Tracking

*No violations — table intentionally omitted.*
