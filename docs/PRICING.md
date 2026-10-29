# Pricing

An assembly can carry a selling price. The assembly editor's **Pricing** section
shows, for every fee profile, the price, the profile's fees, the profit left
after cost and fees, the margin, the markup, and the largest discount before a
loss. Standalone parts use a one-part assembly, as for costing.

## Fees

Settings → Fees lists fee profiles: one per place products sell, such as Shopee
or Direct. Each profile has a name (unique, case-insensitive) and any number of
fees. Each fee has a free-text name and one of two types:

- **% of sale price** (0 to below 100), such as commission, payment or ad fees.
- **Fixed per order** (zero or more), such as a listing fee.

The Fees overview shows each profile's fee count and combined fees. Add a profile
or open its name to edit it on a dedicated page. Each editor saves independently,
warns before discarding unsaved changes, and groups the name, fee rows, and total
in one compact card. **Try a sale amount** expands a live example (default 100 in
the farm currency); **Include a monthly fee** expands allocation guidance. The example's remaining amount excludes production
costs. Removing a profile requires confirmation; removing an individual fee line
is part of the unsaved draft.

A profile's percent fees must total below 100%. A monthly fee is entered as a
fixed fee per order: the monthly amount divided by orders per month (48 ÷ 400 =
0.12). Every assembly shows every fee profile, so there is no profile choice on
the assembly. With no profiles, Pricing shows one row without fees. Hovering a
row's fees shows each fee's amount at that price.

## Price, margin, or markup

Each assembly stores one pricing rule, and the choice decides what stays fixed
when costs change:

- **Price**: a fixed amount, the same on every fee profile. Margin moves with cost.
- **Margin**: profit after fees as a percent of the price (0 to below 100). The
  price follows cost and differs per fee profile, because fees differ.
- **Markup**: a percent on cost, before fees. The price follows cost and is the
  same on every fee profile.

A blank value saves no pricing; the table then shows each profile's break-even
price. Switching the rule keeps the first profile's current price and converts
the value. Computed prices are rounded to cents.

With cost `C` (the Build panel's estimated total including shipping), fee rate
`p` (the profile's percent fees ÷ 100) and fixed fee `f` (its fixed fees):

- Price from markup `m` = `C × (1 + m)`; from margin `m` = `(C + f) ÷ (1 − p − m)`.
  When `p + m ≥ 1` no price reaches the margin; the profile shows "—" and a note.
- Fees = `price × p + f`. Profit = `price − C − fees`. Margin = `profit ÷ price`.
  Markup = `(price − C) ÷ C`.
- Break-even = `(C + f) ÷ (1 − p)`. Max discount = `1 − break-even ÷ price`.

Fixed fees count once per unit, as if every order holds one unit. Each row has a
bar splitting the price into cost, fees and profit; on a loss, profit is red and
the bar holds only cost and fees.

A partial cost marks the cost "(partial)", so profit may be lower than shown.
An unavailable cost leaves only a fixed price; margin and markup prices cannot be
computed.

## Failure allowance

Settings → General → Failure allowance (percent, 0 to below 100, default 0) adds
a Build line of that percent of filament cost, printer depreciation and
electricity: the costs a failed print wastes. Packaging, labor, other cost items
and shipping are not increased. The allowance is part of product cost and the
total that Pricing uses.

Prices, fees and the allowance are current estimates. This feature does not
record actual revenue, per-profile price overrides, fee caps, quantity quotes or
price rounding to price points.

## API

- `GET /api/v1/settings/fees` and `PUT /api/v1/settings/fees`: the PUT replaces
  every profile for compatibility (`profiles: [{ name, fees: [{ name, type, amount }] }]`, `type`
  is `Percent` or `Fixed`). Nothing references a profile, so removed profiles
  simply disappear. Supplied existing IDs are retained; entries without an existing
  ID receive a new ID. Totals are not stored or returned; clients sum the fees.
- `POST /api/v1/settings/fees/profiles` creates a profile; 
  `PUT /api/v1/settings/fees/profiles/{id}` updates just that profile. Both accept
  `{ name, fees: [{ name, type, amount }] }` and return the saved profile with its
  stable ID. `DELETE /api/v1/settings/fees/profiles/{id}` removes only that profile
  and returns 204. Unknown update/remove IDs return 404; duplicate names return
  400. These operations use the existing `settings:fees:update` permission.
- `GET` and `PUT /api/v1/settings/general` carry `failureAllowancePercent`;
  omitting it on PUT keeps the saved value.
- Assembly create, edit and detail carry `pricing: { mode, value } | null`, with
  `mode` one of `Price`, `Margin`, `Markup`. On edit, omitting `pricing` keeps
  it and `null` clears it. The server stores the rule; prices, fees and profit
  are calculated in the web client.
