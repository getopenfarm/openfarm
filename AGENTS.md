# OpenFarm agent conventions

## Web tables

Read and follow the [shadcn skill](.agents/skills/shadcn/SKILL.md) when working on
web tables. Follow [Web UI conventions](docs/UI_CONVENTIONS.md) for surrounding
navigation, forms, themes, and verification.

- Reuse `web/src/components/data-table/DataTable.tsx` and existing shadcn Table
  primitives. Keep consistent borders, spacing, semantic colors, hover states,
  selected-row styling, and horizontal scrolling on narrow screens.
- Ordinary list tables are content-sized: a few rows produce a short table, and
  larger pages scroll with the document. Do not force viewport height, stretch
  rows, or add an internal vertical scroll region. Keep horizontal scrolling.
  Use the shared numbered pagination footer inside the table frame, with a range
  count, previous/next chevrons, active page outline, ellipses, and a rows-per-page
  selector (10/25/50/100; default 25). Changing size resets to page one.
  Use server pagination for paged APIs and client pagination only for complete
  collections. Preserve independent tab pagination and clear page-local selection
  when page size changes. The queue retains its specialized viewport layout,
  virtualization, independent scrolling, and default 50 rows per section.
  Embedded form/review tables remain content-sized without invented pagination.
- Put row operations in the trailing ellipsis menu using `DataTableRowActions`.
  Use an accessible “Actions for {name}” label. Keep navigation as real links;
  selection controls and menu actions must not activate row navigation.
- Add row selection only when it supports a meaningful existing workflow. Use
  stable entity IDs, an indeterminate select-all checkbox, and an explicit
  selection scope. Preserve selection through sorting and prune removed IDs.
- For table bulk actions, use the shared `DataTableSelectionBar`: a floating bar
  centered at the bottom of the viewport, showing the selected count, relevant
  actions, and Clear selection. Show it for one or more selected rows; do not
  duplicate bulk actions in a top-right toolbar.
- Respect device safe areas and narrow screens. Reserve enough page space after
  pagination for the bar (including wrapped controls) so final rows and pagination
  can scroll clear of it. Keep it below menus and confirmation dialogs.
- Announce selection counts politely, preserve keyboard access, and restore focus
  when selection is cleared or a dialog closes. Disable repeated submissions and
  conflicting controls while a bulk request is pending.
- Preserve feature-specific action eligibility, pagination, dependency checks,
  confirmation, failure feedback, and retry behavior. Use the shared ConfirmDialog
  for destructive operations; mount dialogs outside dropdown content.
- Specialized tables such as the virtualized print queue must retain their
  rendering and operational behavior while matching the same visual conventions.
  Read-only tables and form-selection lists do not need invented bulk operations.

Run `bun run test`, `bun run lint`, and `bun run build` from `web/` after table
interaction changes. Cover keyboard interaction with focused automated tests.
The user performs visual checks, including responsive layout, both themes,
200% zoom, focus appearance, and overlay stacking. Do not start browser-based
visual verification unless the user explicitly requests it. Report automated
results and identify visual checks as pending user verification.
