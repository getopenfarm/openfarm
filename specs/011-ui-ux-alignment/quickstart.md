# Quickstart: Validating UI/UX Convention Alignment

## Prerequisites

```
cd web
bun install
bun run dev   # starts the Vite dev server against whatever API the .env points at
```

An operator account with permissions to add/edit printers, spools, parts,
profiles, and connection secrets (see `.env.example` / seeded operator per
`specs/001-foundation`).

## User Story 1 — Styled confirmation for destructive actions

1. Open a Part detail page → click "Delete part".
   - **Expect**: an in-page styled dialog appears (not a native browser
     popup). Cancel/Escape/outside-click closes it with no deletion.
   - Confirm → part is deleted, same as before.
2. Repeat on a Printer detail page → "Delete printer".
3. `grep -rn "window.confirm" web/src` → **expect zero matches**.

## User Story 2 — Add/Edit flows are deep-linkable pages

For each of the six flows below: trigger it from its list page, confirm the
browser URL changes to the dedicated route, fill in a few fields, refresh the
browser, and confirm you land back on the same routed page (not the list).

| Flow | Trigger | Expected URL |
|---|---|---|
| Add printer | Printers list → "Add printer" | `/printers/new` |
| Edit printer | Printer detail → "Edit" | `/printers/:id/edit` |
| Add spool | Spools list → "Add spool" | `/spools/new` |
| Add part | Parts list → "Add part" | `/parts/new` (confirm file processing continues in the background while typing name/tags) |
| Import profiles | Profiles list → "Import profiles" | `/profiles/import` |
| Add connection secret | Settings → Connection Secrets → "Add credential" | `/settings/connection-secrets/new` |

Then, on any one of the pages above:

6. Type into a field, then click a sidebar nav link (in-app navigation).
   - **Expect**: a styled confirmation dialog blocks the navigation until you
     confirm or cancel.
7. Type into a field, then refresh the browser tab (or close it).
   - **Expect**: the browser's native "leave site?" warning appears.
8. Open `AddFeedUnitDialog`, `VariantDialog`, `CreateVendorDialog`,
   `PartPickerDialog`, any Load/Unload/Record-usage dialog, the
   replace-part-file dialog, and the profile raw-data view.
   - **Expect**: all still open as modals, unchanged.

## User Story 3 — Consistent form validation on Filament and Assembly

1. Open `/filament/new`, submit with a required field empty.
   - **Expect**: an inline field-level error appears in the same style as the
     Printer/Spool/Part forms (not a toast).
2. Open `/assemblies/new`, submit with a required field empty.
   - **Expect**: same inline field-level error behavior.
3. Successfully submit the Filament form.
   - **Expect**: persists via the real filament API (visible in Network tab /
     reflected on `/filament` list after invalidation).
4. Successfully submit the Assembly form.
   - **Expect**: persists via the mock assemblies/parts store (no network
     request to a backend Assembly endpoint — none exists yet), still visible
     on `/assemblies` list after invalidation.

## Automated checks

```
cd web
bun run lint          # biome check .
bunx tsc -b            # typecheck
bun run test           # vitest run — includes updated/new *.test.tsx for every
                        # page and hook touched above
```
