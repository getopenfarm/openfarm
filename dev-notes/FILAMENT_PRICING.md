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

- `POST /api/v1/spools` takes one delivery: `lines` (1 to 20), each a color with its spool weight, `quantity` (1 to 500),
  optional grams left, price and label, plus the supplier, order number and received date the lines share. A delivery adds
  at most 1,000 spools, in one save, all or none: an unknown color on any line adds nothing. The response is
  `{ spools: [...] }`, and the activity log gets one `SpoolRegistered` entry per spool, written in one save. The Add spools
  page builds the lines; a spool's label is set on its own page.
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
- **Days left**: grams on hand over the last 30 days' daily use (`daysLeft`); null when the color was not used, since there is
  no pace to measure.
- **Low stock**: two thresholds, each on the filament (the default) and on each color (an override), set and cleared exactly
  like the price per kg: `LowStockGrams` (grams on hand) and `LowStockDays` (days left, 0 to 365). A color is low when it is
  under either threshold that applies: its own, else the filament's default, which covers only colors stocked at least once
  (any spool, finished or not), so a color the farm never kept does not alert. A threshold of 0 never fires; set it on a
  color to silence the default. A color with no use in 30 days has no days left, so only the grams threshold can fire.
- Low colors appear in the *Action required* bell (`lowStock` on `GET /api/v1/notifications`), with no Dismiss: each clears
  once restocked. Every spool or filament change publishes `NotificationsChanged`, so open views refresh.
- The filament list shows the totals as tiles (On hand, Value, Active spools, Used 30 d) and per filament; each filament's
  page shows them as a compact strip. Its *Colors* tab lists the colors most used first, each with its days left as a bar
  toward a 60-day horizon (a tick marks the days threshold), what it holds, and its daily use.
- **Short**: `GET /api/v1/filaments/shortages` lists colors that finite jobs still to print (queued, printing or paused;
  nothing is deducted until a print ends) need more of than is on hand. A job line counts its sliced grams, else the part's
  estimate for that color. A line narrowed to a color counts against it; otherwise against every color of the same
  material and hex, so a shortage may be over-reported but is never missed. Unlimited work is not counted. The list
  shows an amber *Short* badge per filament, and the filament's *Colors* tab per color.

## Daily usage

`GET /api/v1/reports/daily-usage?filamentId&timeZone` returns one filament's grams drawn per color per day over the last 30
days, counted in the client's IANA time zone (an unknown zone is a 400), oldest day first. Drawn is what prints and manual
usage took from the colors' spools (ledger `Job` and `Manual`); waste is the part prints that failed or were cancelled took.
The *Colors* tab draws it as one bar per day, waste hatched, and shows waste as a share of what was drawn. That differs from
the materials report's waste share, which compares failed and cancelled prints with completed ones by when the prints ended.

## Materials report

`GET /api/v1/reports/materials?from&to&filamentId` (and `.csv`) sums filament per color over a range. The Stats page's
*Materials* card shows its totals and the colors used most, with the CSV export; a filament's *Usage* tab passes `filamentId`,
which keeps that filament's colors only, so the *No spool* row drops out. `from` is inclusive and `to` exclusive; the pages send local calendar days as UTC instants.

- **Rows** are the color of the spool a print line was deducted from, plus a *No spool* row for lines with none. A print counts
  when it ended in the range. A line counts once deduction ran on it: grams recorded, or skipped with a reason. Lines of a
  print cancelled before it started have neither, so they are left out.
- **Used**: grams completed prints took. **Waste**: grams failed and cancelled prints took. **Waste %** is waste over both.
- **Written off**: what finishing spools wrote off in the range, net of restores in it (ledger `WriteOff` and `Restore`).
- **Cost** adds only lines with a recorded price (see Realized cost). Grams with no price are listed as unpriced, and make the
  cost *Partial*.
- **Prints** counts each print once per row; the total counts a print that used several colors once. **Not deducted** counts
  lines skipped without grams; fix them from the bell.
- Manual usage and weight corrections are not in the report; they show on the filament's *History* tab.
- Rows come most grams first. The lines are grouped in memory, which is fine for a farm's range of prints.
