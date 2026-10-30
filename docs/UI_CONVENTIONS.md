# Web UI conventions

OpenFarm uses shadcn Radix Nova, Tailwind v4, Lucide icons, and semantic CSS
variables. The look follows the akaun app: neutral zinc surfaces (grey
hover and selected tints), blue primary, Geist for body and headings. Change colors only through the tokens in
`web/src/index.css`, and keep the compact operational dashboard when making UI
changes.

## Create and edit flows

Creating a first-class entity uses a routed `/<feature>/new` page; editing
follows the bullets below (in place, an edit page, or a dialog). Use
`PageHeader`, `BackLink`, and the sticky `FormFooter`. Preserve background part processing and unsaved-change
protection when changing these flows.

- **Header**: every page uses `PageHeader`. Sub-pages put `BackLink` in its
  `above` slot. Page actions go in `action`, top right; Edit is outline with
  `PencilIcon`, and Remove/Cancel run comes last as
  `variant="destructive"`. Remove is the only removal verb (Delete only for
  irreversible file/credential destruction).
- **Whole-page edit** uses `FormFooter` (page root, `form`, and `FieldGroup`
  take `flex-1`): outline Cancel, then primary `Save`. No footer on pages with
  nothing to save, and never Remove in it.
- **Library and material records** (parts, assemblies, components, spools,
  profiles) edit in place on their page: one react-hook-form draft, one
  `FormFooter` Save (disabled until dirty), `useUnsavedChangesPrompt`. No
  section saves on its own.
- **Operational actions** stay immediate, outside the draft: reprocess, replace
  file, load/unload spool, record usage, reorder/pin, feed units, mark ready.
- **Records with an operational page** edit on a separate page with the same
  single-draft rules: production runs on `/production-runs/:id/edit`, filament
  on `/filament/:id/edit` (its page holds stock, spools, usage and history).
  Print jobs edit through header-triggered dialogs.
- **Pending state**: `<Spinner data-icon="inline-start" />` plus an ellipsis
  label (`Saving…`, `Removing…`, `Importing…`).
- **Create buttons** on list pages read `Add X` with
  `PlusIcon data-icon="inline-start"`, primary, in the header `action`. Add-page
  titles read `Add X`.
- **Batch entry**: records that arrive in batches (spools from one delivery)
  are added as lines on one page: each line its own item and quantity, the
  fields the batch shares (supplier, order, date) once below them, and one
  primary action that names the total (`Add 200 spools`).
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
- Size a field to its value. A number or date holds a few characters, so keep
  the input narrow, put the unit inside the box ("750 g"), and group short fields
  on one row (two columns on phones) instead of stacking full-width inputs.
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

- Every routed page roots in `Page` (`web/src/components/page/Page.tsx`) with a
  `PageHeader` first. `Page` owns the header-to-content gap and the bottom
  clearance for the table selection bar; pass only layout extras like `flex-1`
  — never hand-roll the root `flex flex-col gap-*`.
- Use semantic tokens from `web/src/index.css`; add theme-specific values there
  rather than raw status colors or per-page dark-mode overrides.
- Badge `kind` variants own categorical foreground/background colors. StatusBadge
  remains the compatibility wrapper for the labeled dot treatment. Filament
  swatches represent physical material colors and must retain their data colors.
- Keep normal-sized text at least 4.5:1 against its rendered background, including
  tinted badges. Verify focus indicators and controls in both themes.
- Use CardHeader, CardTitle, and CardDescription for section headings; reserve
  CardContent for the body and CardFooter for actions when appropriate.
- KPI tiles (`KpiCard`) lead a list page. A detail page whose own content leads
  (a filament's color rows, a run's lines) shows the same figures as one compact
  bordered strip (`KpiStrip`), so the totals do not compete with it.
- Production progress is drawn in plates, never a percentage: `PlateStrip` shows
  one cell per plate (done, written off, printing, failed, waiting), switching to
  proportional segments past 60 plates, with the counts as its accessible name.
  Units go next to it as text ("24/40 units").
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

Use the shared DataTable and DataTableRowActions for resource lists. Two lists are
deliberately not tables: a filament's color rows, whose cells are charts (days
left, daily use), and the shelf's groups of identical full rolls (`1 kg · full
× 166`), which count instead of listing. Group identical items like that whenever
a list would otherwise repeat the same row dozens of times; Show expands a group
to its rows. Row operations
belong in accessible trailing ellipsis menus. Tables with bulk operations use
DataTableSelectionBar, which shows the count, actions, and Clear selection when
one or more rows are selected. Parts and Assemblies place it inline in the framed
toolbar beside Fields in both views. Other lists retain the floating bar. Reserve page
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
default segmented appearance and keyboard navigation. A detail page with several
views (the filament page) keeps one tab list above the panels, never inside one,
and the active tab in `?tab=` so links can open it. Use outlined ToggleGroups
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

Parts, Assemblies and Non-printed Components start newest first; their primary
header cycles default → ascending → descending → default. Runs start by due date,
undated last, so the next order due leads; their Due header toggles direction
(the API also sorts runs by displayed label, captured names and legacy fallbacks
included). Profiles start with name ascending and toggle direction. All server
orders have an ascending entity-ID tie-breaker. Activity Log remains
newest first, and the print queue retains its operational ordering.

Sort changes reset to page one. Profile tabs retain independent sorting, page,
and page-size state. Part selection is page-local and clears on sorting, page,
size, or tag-filter changes. Keep sort controls mounted while refetching and
expose `aria-sort` on the active column header. Previous results may remain
visible during loading or a failed request, but conflicting controls must stay
disabled; failures show Retry for the requested page and order. Recover to the
last valid page only from a successful settled response, never placeholder data.

## Copy conventions

- **Verbs**: Add (create a record), Remove (soft-removal; Delete only for
  irreversible file/credential destruction), Unload (spool from slot).
- **Feed path terms**: Feed unit (AMS, reloader, external spool) contains
  Slots. Say "slot", never "channel" or "Ch." (matches `FeedSlot`).
  Reloader: "Add reloader".
- **Failures**: "Could not X." Never "X failed", "Failed to X", "Couldn't".
- **Success toasts**: "<Entity> <past participle>." naming the entity, such as
  "Part saved." and "Printer added." Pending, button and toast share one verb.
- **Style**: sentence case everywhere, American spelling (color), no
  contractions, no "Please", ellipsis is `…`.
- **Helpers** in `web/src/lib`: `pluralize`, `formatUnits` (grams, mm, °C,
  file size), `formatDateTime`, `formatMinutes`, `formatRelativeTime`. Do not
  format counts, units or dates inline.

## Library folders and thumbnail views

Parts and Assemblies have independent folder trees. Folder location lives in
`?folder=`; the root contains unfiled items. Organization never changes BOM
references. Breadcrumbs and folder names are real links. Only empty folders can
be removed. Creating items in a folder carries that destination through confirmation,
including every part created from a multi-plate upload.

Each library uses one shared browser with a mixed folder/item collection, one
Details/Thumbnails switch and one numbered pagination footer. Folders come first;
newest-first or name sorting applies within each group, before server pagination.
The Name column is shared; item-only columns show an em dash for folders. Both
presentations share the same row model, selection and action controllers. Views
are remembered independently through browser preferences, and switching views
preserves state. Changing location, page, page size or sorting clears page-local
item selection. Folders move individually and have no selection checkbox.

Use dnd-kit for moves into visible folders or ancestor breadcrumbs, including root.
Dragging a selected item moves the selected group; an unselected item moves alone.
Allow dragging directly from item names, thumbnails and row/card surfaces onto
folder rows/cards. Keep a keyboard move control that appears on focus. Use a long
press for touch, preserving ordinary scrolling and link/menu interactions.
Highlight accepted targets during dragging and announce outcomes. Escape
cancels. Keep source entries until server confirmation, block conflicting controls
while saving, and preserve selection after failure. Keep Move to in menus and the
inline selection bar as an accessible alternative and for off-page destinations.
Do not turn folder moves into manual row ordering or external-file upload.

The shared DataTable owns rows, cards, column visibility and pagination; its row
container slot adds dragging without changing native table/list structure. Library
bulk actions use its selection bar inline beside Fields in the framed toolbar in
both table and thumbnail views. The Fields menu controls metadata independently
for Details and Thumbnails, saved in localStorage per library and view; names,
previews and actions remain visible.
Part thumbnails default to material chips on the left and file type aligned to
the right below the preview; Estimate and Nozzle are optional. Tags are
available only in Details. Assembly thumbnails default to the collage only, with
Description and Lines optional. Table defaults remain unchanged. Other consumers
retain their presentation.
Assembly previews are batched collages of up to four distinct descendant parts;
never fetch details once per card.

`GET /api/v1/library/{library}/contents` supplies the combined page, total count and
ancestors. It accepts `parentId`, pagination, name sorting, and optional assembly
previews. Existing list APIs retain all-items behavior when `folder` is omitted;
`folder=root` selects unfiled items and a folder ID selects direct contents.
Catalog pickers continue using those all-items queries. Invalidate combined pages
after mutations and part events, including assembly previews affected by part changes.

Folder and assembly row icons use the same 40px preview frame as part thumbnails.
Folders retain their own icon, including in thumbnail view; collage previews stay
with assemblies in thumbnail view. Do not add a persistent drag indicator or a
dedicated drag column. Dropping on another item does not reorder the collection.

Library creation actions share the page header: outline Add folder followed by
primary Add part/assembly. Browsing pages keep Library > Parts/Assemblies in the
shell header. Inside folders, the framed row above table rows or thumbnails shows
a folder icon for the library root followed by folder ancestors,
replaced by selection actions whenever one or more items are selected. During
dragging the local trail returns so root and ancestors remain drop targets.
The root icon has a 32px target and a hover/focus tooltip and accessible name:
Part library root or Assembly library root. It is a real link back to root.
At root, hide the local trail and leave the toolbar left side empty while
preserving its height, Fields and selection actions. Determine visibility from
the current folder ID, including during loading and errors. Shell breadcrumbs retain text.
Fields stays in the toolbar. Inside folders, empty, loading and error states also
show the local trail. Other operations keep it visible with existing busy navigation guards.
Detail and Add pages retain the full shell breadcrumb:
Library > Parts/Assemblies > folder ancestors > opened item.
Item pages resolve saved folder membership, including direct links. Long trails
scroll horizontally without displacing shell utilities. The framed toolbar groups folder navigation or selection actions on the left and
thumbnail sorting, Fields, and the icon-only Details/Thumbnails Tabs on the right.
There is no separate view-controls row. Keep the tabs outside the active content
panel, with controls wrapping on narrow screens. Loading, empty and error states
retain the toolbar and view controls; Fields is disabled when no table is available.
View icons have accessible names and hover/focus tooltips. Thumbnail cards use a
responsive grid with six columns at desktop widths (1280px and above), stepping
down to four, three, two, and one on smaller screens. Previews are 128px tall,
with titles and actions above the previews, chips below, then optional metadata.
There are no card checkboxes or select-all strip.
Tiles show a neutral overlay on hover and keyboard focus. Folder previews stretch
to fill available card height alongside item metadata. View tabs remain outside
the active content panel and keep selection and field visibility when switching.

Ordinary clicks still open library items in either view. Ctrl/Cmd-click toggles
page-local item selection; Shift-click selects an inclusive range from the last
modifier-selected item, excluding folders. Space toggles a focused entry,
Ctrl/Cmd+A selects page items, Escape clears selection, and Enter opens a focused
entry. Item menus include Select/Deselect for touch and keyboard users. Table
checkboxes remain available. Selection and bulk operations are shared across
views; keyboard shortcuts must not intercept menu, dialog or form controls.
Pointer drag restoration focuses the source entry rather than revealing a text
move handle. The keyboard-only move control is compact and icon-only; drag
previews contain one item name or a selected-item count.
