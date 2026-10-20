# Cost catalog

The Cost catalog holds reusable physical material prices and service rates. It
extends existing non-printed components: existing IDs, prices, and assembly BOM
references remain valid. Existing records start as physical hardware priced per
piece. Legacy component page URLs redirect to the catalog.

## Price and unit

Every item is a price for a quantity of one unit. Enter the price and the
quantity it covers. A blank quantity means 1, so a price per piece needs no
quantity. The cost per unit is the price divided by the quantity (40 for 100 m²
is 0.40/m²); estimates use it. A package label saved by an earlier version is
kept but no longer shown.

The unit sets the item type. Piece, metre, square metre, gram, and millilitre are
physical items. Hour and product are services and have no quantity. Choosing the
Labor or Shipping category selects hour or product while the unit is unchanged.
Square-metre items can use length and width in mm, cm, or m instead of a
manually calculated total area. Both source dimensions are saved.

For example, a roll costing 40 with dimensions 100 m × 100 cm contains 100 m²,
so its cost is 0.40/m². A 50 cm × 20 cm cut consumes 0.10 m² and costs 0.04 per
product. Multiple identical cuts multiply that area by the piece count.
Area estimates assume reusable offcuts; they do not optimize cuts or add waste.

Blank prices are unknown; zero is valid. Quantities and dimensions must be
positive. Piece quantities must be whole. Referenced items cannot change unit.
All prices use the same currency, without a currency symbol.

## Assembly costs

The assembly editor has two sections. **Structure** holds parts and
sub-assemblies. **Cost items** holds everything from the Cost catalog: hardware,
packaging, consumables, labor, and shipping, as physical items or services. An
assembly still needs at least one part or sub-assembly. Cost items are saved as
the assembly's additional costs; a catalog item can appear once per assembly, and
items still listed in Structure from earlier versions show as "In Structure" in
the picker.

Add a cost item from the searchable picker (name, category, and cost per unit).
The new row appears with its usage field focused. Counted items (piece, product)
start at 1; measured items (metre, gram, millilitre, minutes) start blank and
must be entered. Picking a square-metre item opens the details dialog first,
because area versus cut size is a real choice. Edit the usage in the row. The
pencil opens the dialog for **Counts as**, usage method, and cut size.
Quantities are per finished assembly; hourly services use **minutes per
product**, divided by 60 before multiplying the rate; flat services use
applications per product.

Each cost item counts as one of:

- **Production** costs carry through nested assemblies with their quantities.
- **Final packing and delivery** costs apply only when the assembly is the
  finished product being estimated. Nested final packing and shipping are excluded.
  This does not change the assembly's own total, only what a parent assembly inherits.

Packaging and shipping default to final packing and delivery; the row subtitle
always shows the current choice. Components added to Structure by earlier
versions still carry through nesting as Production. Prices are current
estimates, not historical expenditure. Missing data leaves estimates partial or
unavailable.

The Build panel separates category subtotals, product cost, shipping allowance,
and estimated total including shipping. Standalone parts can use a one-part
assembly for this workflow. No inventory, leftover rolls, purchasing, actual
expenditure, batch allocation, shared parcels, electricity, or depreciation is
tracked by these features.

## API compatibility

`/api/v1/cost-items` supports listing (server pagination, name sorting, type and
category filters), creation, detail, editing, and removal. Responses expose the
source `pricing` configuration and computed `effectiveRate`.

Existing `/api/v1/non-printed-components` listings expose direct-priced physical
piece items. Legacy edits of measured, service, or package-priced items are
rejected so older clients cannot erase pricing configuration. Existing component
permissions also authorize corresponding cost-catalog operations.

Assembly requests and responses include `additionalCosts`. Omission in an older
edit request preserves saved costs; an empty array clears them. Saves are atomic.
Explosion responses expose `productionCosts` and `finalProductCosts` separately;
production execution and printed-part readiness do not depend on these costs.

## Catalog and editor workflow

The catalog uses the shared resource table, column visibility controls, compact
type/category filters, server pagination, and trailing row-action menus. Selection
is limited to the current page. Remove selected confirms once, reports referenced
items that could not be removed, and retains those selections for another attempt.

Each item shows an icon in the catalog table and in the assembly Cost items rows.
The default comes from the category (Hardware screw, Packaging package, Consumables
droplet, Labor hard hat, Shipping truck, Other shapes). An optional per-item icon,
chosen beside Name from the library in `costIcons.tsx`, overrides it. The API stores
only the Lucide name (`icon`, lowercase kebab-case, max 40); an unknown name falls
back to the category default.

The add/detail editor uses compact Cost item (name, category) and Pricing cards,
matching Add filament. Pricing is one row: price, then quantity with its unit.
The cost per unit appears below it, with the purchase it comes from. Add opens
the new item's detail page; Save updates an existing item in place. There is one
draft and one Save action, with inline validation and unsaved-change protection.
Legacy `/cost-catalog/{id}/edit` URLs redirect to the in-place detail editor.
