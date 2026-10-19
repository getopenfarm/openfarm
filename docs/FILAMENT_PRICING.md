# Filament pricing

Set an optional **Price per kg** when adding or editing a filament. Each color
variant can override this price. Clear an override to use the filament price;
clear the filament price to leave it unknown. Zero is a valid price. Enter all
filament and hardware prices in the same currency; amounts have no currency symbol.

Assembly Build panels use the catalog variant selected on each part's production
filament requirement. Detected material/color alone does not select a price.
Costs use current saved prices, rather than inventory purchase prices or historical
snapshots:

`cost = estimated grams × required units / units per print / 1000 × price per kg`

Nested assemblies contribute their exploded quantities. Per-color grams include
purge where available. A single requirement can fall back to the part's total
weight, while unsplit multi-color estimates remain unavailable. Older parts may
need reprocessing to populate per-color weight estimates.

The panel shows filament costs by color, a filament subtotal, hardware cost, and
a combined estimated total. Known amounts are labeled partial when any required
cost is missing. When no required filament can be priced, its cost is unavailable.
Loading and request errors do not appear as complete totals; use **Retry totals**
after a failed request.

The filament API exposes nullable `pricePerKg` on filament and variant records.
Create requests accept these prices. Edit requests update existing variant prices
through `variantPrices: [{ variantId, pricePerKg }]`; omitted variants retain their
prices. Omitting the filament `pricePerKg` preserves its price; explicit null
clears it. Price changes, new variants, details, and profile assignments share one
atomic save. Existing variant identity and colors remain immutable.

Physical material package rates, measured usage, labour, and shipping allowances
are described in [Cost catalog](COST_CATALOG.md).
