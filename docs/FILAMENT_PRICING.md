# Filament pricing

Set an optional **Price per kg** when adding or editing a filament. Each color
variant can override this price. Clear an override to use the filament price;
clear the filament price to leave it unknown. Zero is a valid price. Enter all
filament and hardware prices in the same currency; amounts have no currency symbol.

Assembly Build panels use the catalog variant selected on each part's production
filament requirement. Detected material/color alone does not select a price.
The panel's estimate is a forecast: it uses current saved catalog prices, rather than
spool purchase prices or historical snapshots. What a finished print really cost is
recorded separately (see [Realized cost of a print](#realized-cost-of-a-print)):

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

## Spool purchase info

A spool can also record what it cost, all optional:

| Field | Meaning |
|---|---|
| `price` | What was paid for the whole spool. Blank is unknown; zero is a real price |
| `pricePerKg` | Derived, read-only: `price / (spoolWeightGrams / 1000)`. A spool bought as 750 g for 18.50 is 24.67 per kg |
| `supplier`, `orderNumber` | Free text, up to 100 characters each |
| `receivedOn` | The day it arrived, a calendar date with no time or zone |

- `POST /api/v1/spools` takes `quantity` (1 to 50, default 1) and adds that many identical spools in one save, all or none.
  The label, weights, and purchase fields apply to each. The response is `{ spools: [...] }`, and the activity log gets one
  `SpoolRegistered` entry per spool.
- `PUT /api/v1/spools/{id}` replaces the purchase fields together with the label and remaining weight; omitting one clears it.
- The per-kg figure divides by the spool's full weight, not what is left, since the price paid covers the whole spool.
- These fields do not change the Build panel: it keeps using the catalog price above. A spool's price is what a print that
  used the spool is costed at (next section).

## Realized cost of a print

When a print's filament is deducted, each line is also costed, once, and the cost is kept with the print.

- **Price per kg**, most specific first: the price paid for the spool that fed the line (over the spool's full weight), else the
  color's price, else the filament's. Zero is a price at every level; only a blank falls through to the next. No price at any
  level leaves the line unpriced.
- **Cost** = grams deducted × price per kg ÷ 1000, kept unrounded and shown to two decimals. A failed or cancelled print costs
  only the grams deducted. A line with nothing deducted has no cost.
- **The price is written once**, when the line is first deducted. Changing a spool, color, or filament price later does not change
  what a print cost, and a repeated deduction adds its grams at the same price. A line left unpriced gains a price when the
  operator assigns a priced spool to it (**Assign spool**).
- **Total**: the job page adds the lines that have a cost and shows *Partial* when any line has none (no price, or not deducted).
  A line without a price shows "No price", never zero. Prints that ended before this was recorded have no price or cost, and
  are not back-filled.
- `GET /api/v1/print-jobs/{id}` returns `pricePerKg`, `priceSource` (`Spool`, `Variant` for the color, or `Filament`) and `cost`
  on each filament line. Printer-started prints are costed the same way.

Physical material package rates, measured usage, labour, and shipping allowances
are described in [Cost catalog](COST_CATALOG.md).

## Stock and low-stock alerts

`GET /api/v1/filaments` returns a `stock` array next to `filaments`, one entry per color. It is a separate array so a filament
save, which returns only the filament, cannot reset it in the client cache.

- **On hand**: grams left on the color's unfinished spools (in storage or loaded), and how many there are.
- **Value**: each of those spools at its own price per kg, resolved as for a print (spool, else color, else filament). A spool
  with grams left and no price at any level is counted in `unpricedSpoolCount` and left out of the value, so the value is
  *Partial*, never a zero.
- **Used 30 d**: grams prints and manual usage took from the color's spools in the last 30 days. Corrections, write-offs and
  restores are not usage.
- **Low stock**: `LowStockGrams` sits on the filament (the default) and on each color (an override), set and cleared exactly
  like the price per kg. A color is low when its grams on hand drop under the threshold that applies: its own, else the
  filament's default, which covers only colors stocked at least once (any spool, finished or not), so a color the farm never
  kept does not alert. A threshold of 0 never fires; set it on a color to silence the default.
- Low colors appear in the *Action required* bell (`lowStock` on `GET /api/v1/notifications`), with no Dismiss: each clears
  once restocked. Every spool or filament change publishes `NotificationsChanged`, so open views refresh.
- The spools page shows the totals as tiles (On hand, Value, Active spools, Used 30 d); the filament catalog shows them per
  filament, and the filament page per color.
