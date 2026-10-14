# Web UI conventions

OpenFarm uses shadcn Radix Nova, Tailwind v4, Lucide icons, and semantic CSS
variables. Keep the existing branding and compact operational dashboard when
making UI changes.

## Create and edit flows

Creating a first-class entity uses a routed `/<feature>/new` page; editing
follows the bullets below (in place, an edit page, or a dialog). Use
`PageHeader`, `BackLink`, and the sticky `FormFooter`. Preserve background part processing and unsaved-change
protection when changing these flows.

- **Header**: every page uses `PageHeader`. Sub-pages put `BackLink` in its
  `above` slot. Page actions go in `action`, top right; Edit is outline with
  `PencilIcon`, and Delete/Remove/Cancel run comes last as
  `variant="destructive"`, keeping the domain verb (Remove for
  printers/parts/jobs, Delete for assemblies and components).
- **Whole-page edit** uses `FormFooter` (page root, `form`, and `FieldGroup`
  take `flex-1`): outline Cancel, then primary `Save`. No footer on pages with
  nothing to save, and never Delete/Remove in it.
- **Library and material records** (parts, filament, assemblies, components,
  spools, profiles) edit in place on their page: one react-hook-form draft, one
  `FormFooter` Save (disabled until dirty), `useUnsavedChangesPrompt`. No
  section saves on its own.
- **Operational actions** stay immediate, outside the draft: reprocess, replace
  file, load/unload spool, record usage, reorder/pin, feed units, mark ready.
- **Operate records**: production runs edit on `/production-runs/:id/edit`;
  print jobs edit through header-triggered dialogs.
- **Pending state**: `<Spinner data-icon="inline-start" />` plus an ellipsis
  label (`Saving…`, `Removing…`, `Importing…`).
- **Create buttons** on list pages read `Add X` with
  `PlusIcon data-icon="inline-start"`, primary, in the header `action`. Add-page
  titles read `Add X`.
- **Dialog footers**: `DialogFooter` with outline Cancel first, then the
  primary: `Save` when it persists form data, otherwise a specific verb
  (Remove, Load spool, Record usage, Send to queue, Mark ready).
- **Destructive confirms**: every `ConfirmDialog` that deletes, removes, cancels
  or discards passes `destructive` and `isPending`.
- **States**: loading is `Skeleton` under the page header; not-found is
  `EmptyState` with a back action; request failure is `ErrorState` with Retry.
  No raw `<p>` states.
- **Wording**: "credential", not "secret", in user-visible text (route URLs
  unchanged).

Dialogs are appropriate for confirmations, quick actions on existing entities,
and in-form pickers or sub-entity creation that must retain the parent form.
Use the shared `ConfirmDialog` (AlertDialog) for destructive confirmations.
Every dialog and sheet needs an accessible title. Long dialogs must fit the
viewport and allow scrolling; use `ScrollableDialogContent` when the header and
footer must remain visible.

## Forms and selection

- Use React Hook Form and Zod for entity forms. Preserve domain-specific checks,
  including verification before printer registration.
- Use `FieldGroup` and `Field`; use `FieldSet` and `FieldLegend` for related
  controls. Labels must target the actual control. Descriptions and errors must
  be associated with it through `aria-describedby`.
- Put `data-invalid` on the Field and `aria-invalid` on its control. Pair
  `data-disabled` on the Field with `disabled` on the control.
- Existing `FormField`, `FormItem`, `FormLabel`, and `FormMessage` exports remain
  supported: the shared React Hook Form adapter renders Field primitives,
  associates labels/errors, and marks invalid fields. New forms may compose
  Controller and Field directly.
- Use shadcn Select for predefined options and wrap SelectItem in SelectGroup.
  Keep DropdownMenuItem and CommandItem inside their corresponding groups.
- Decorated search controls use InputGroupInput and InputGroupAddon. Every search
  input needs an accessible name; placeholders must describe the actual search.
- Profile search uses Radix Popover and Command. Server search remains
  authoritative (`shouldFilter={false}`), with debounce, loading, retry, and empty
  states. Support arrow keys, Enter, Escape, clearing, and trigger focus return.
- Use ToggleGroup for selectable filters and expose selection state. Disable
  repeated submissions while pending; use Spinner with a descriptive action label.

## Shared presentation

- Use semantic tokens from `web/src/index.css`; add theme-specific values there
  rather than raw status colors or per-page dark-mode overrides.
- Badge `kind` variants own categorical foreground/background colors. StatusBadge
  remains the compatibility wrapper for the labeled dot treatment. Filament
  swatches represent physical material colors and must retain their data colors.
- Keep normal-sized text at least 4.5:1 against its rendered background, including
  tinted badges. Verify focus indicators and controls in both themes.
- Use CardHeader, CardTitle, and CardDescription for section headings; reserve
  CardContent for the body and CardFooter for actions when appropriate.
- Use shared EmptyState/ErrorState, which compose shadcn Empty. Distinguish initial
  loading, request failure with Retry, no resources with an Add action, and no
  filter matches. Use Skeleton for loading, Alert for callouts, and Sonner for toasts.
- Prefer component variants over caller color/typography overrides. Use `cn()`
  for conditional classes, `gap-*` for layout, `size-*` for square dimensions,
  and `truncate` for single-line truncation. Icons inside controls inherit sizing;
  button icons use `data-icon="inline-start"` or `"inline-end"`.

## Tables and bulk actions

Ordinary list tables size to their rows and scroll with the page. Do not force
full-height frames or internal vertical scrolling. Use a shared numbered footer
inside the frame: rows-per-page selector (10/25/50/100, default 25), range count,
outlined chevrons and active page, plain inactive numbers, and ellipses for long
page lists. Reset to page one on size changes. Use server pagination for paged
endpoints and client pagination for complete collections. The queue retains its
specialized independent scroll regions and default 50 rows per section.
Embedded form/review tables remain content-sized.

Use the shared DataTable and DataTableRowActions for resource lists. Row operations
belong in accessible trailing ellipsis menus. Tables with bulk operations use
DataTableSelectionBar, which floats at the bottom of the viewport when one or more
rows are selected and shows the count, actions, and Clear selection. Reserve page
space after pagination, respect safe areas, and keep dialogs above the bar.
Do not duplicate bulk controls above the table or add selection without a useful
workflow. See [agent conventions](../AGENTS.md) for selection and safety rules.

## Table tabs, filters, and search

Use `DataTableToolbar` above list content: view tabs or row filters on the left,
with optional `DataTableSearch` and secondary actions on the right. Keep 12px
between toolbar controls and 16px between the toolbar and content. Below 640px,
search takes the full width below the controls; at larger widths it is 256px
wide. Let controls wrap when space runs out without clipping labels or focus
rings. Keep DOM order consistent with the visible order.

Use `DataTableTabsList` inside Tabs for switching content panels, preserving the
default segmented appearance and keyboard navigation. Use outlined ToggleGroups
for row filters: single selection for printer status and multiple selection for
part tags. Preserve each feature's filtering, counts, pagination, selection, and
pending behavior. Do not add search to pages without it simply for consistency.
`DataTableSearch` requires an accessible label and a descriptive placeholder and
uses the shared InputGroup primitives. Profile picker search retains its
specialized Popover/Command behavior.

## Navigation and responsive behavior

Every navigable table row must contain a real link to its detail/edit page.
Row clicking is an additional pointer convenience. Nested links, buttons, and
form controls must not activate the row handler. Preserve native table semantics
and keep wide tables horizontally scrollable within their containers.

Breadcrumbs must shrink without pushing utility actions outside the viewport.
Form-footer margins must match shell padding. Preserve the dashboard's fixed
responsive camera grid, neutral surfaces, in-card warnings, explicit finished
status action, and filament-source distinctions.

## Verification

Run `bun run test`, `bun run lint`, and `bun run build` from `web/`. Add focused
regression tests when changing navigation, keyboard interaction, validation, or
request states. Inspect light and dark modes at 375px, 768px, and 1440px, plus
200% zoom. Check long labels, keyboard focus, dialog scrolling, and page overflow.
Keep browser-validation limitations explicit; a passing build is not visual QA.

When adding primitives, use the project package runner and the official registry.
Inspect generated files for import aliases, composition, and lint compatibility.
Do not overwrite customized primitives without explicit approval.

## Sorting paged lists

Sorting and pagination must operate on the same collection. Server-paged lists
use controlled `serverSorting` with `manualSorting`; the API filters, counts,
orders, and then paginates. Never apply local sorting to a server page. A
server-paged table without a sorting handler renders non-sortable headers.
Complete-collection tables retain client sorting and pagination.

Parts, Assemblies, Non-printed Components, and Production Runs start newest
first. Their primary header cycles default → ascending → descending → default;
Profiles start with name ascending and toggle direction. Run ordering uses the
actual displayed label, including captured names and legacy fallbacks. All
server orders have an ascending entity-ID tie-breaker. Activity Log remains
newest first, and the print queue retains its operational ordering.

Sort changes reset to page one. Profile tabs retain independent sorting, page,
and page-size state. Part selection is page-local and clears on sorting, page,
size, or tag-filter changes. Keep sort controls mounted while refetching and
expose `aria-sort` on the active column header. Previous results may remain
visible during loading or a failed request, but conflicting controls must stay
disabled; failures show Retry for the requested page and order. Recover to the
last valid page only from a successful settled response, never placeholder data.
