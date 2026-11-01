# Printer costs

Settings → Printer costs stores current costing assumptions per supported printer
model. Existing fleet models are added during migration, and registering printers
adds their models automatically. Models can also be added manually before a
printer is purchased. Removing printers retains these records and their values.
There is one shared set of assumptions per brand/model. Use the row menu's Remove
operation to explicitly remove a model's cost settings after all printers of that
model have been removed from the fleet. Remove is hidden for fleet models, and the
API rejects their removal with HTTP 409, including requests from stale pages.
Removing an unused model makes its costs unknown in estimates. Models can be added
again manually; registering another
printer of that model also recreates an unconfigured record. Ordinary printer
updates do not recreate removed cost settings.

Each record contains printer purchase price, printer upfront cost (setup,
accessories, and other one-time expenses), lifetime service cost, lifetime printing
hours, and average printing power in watts. Electricity price per kWh is farm-wide.
Use measured average printing power rather than the printer's peak power rating.
All amounts use the same currency convention as the existing cost catalog.

The Assembly Build panel adds exactly two lines:

- Printer depreciation = allocated printing hours × (purchase price + printer
  upfront cost + lifetime service cost) ÷ lifetime printing hours.
- Electricity = allocated printing hours × average printing watts ÷ 1,000 ×
  electricity price per kWh.

Allocated printing hours = required units ÷ units per print × estimated print
minutes ÷ 60. Like existing per-product estimates, this allows fractional plates;
it does not estimate the integer plates needed for a production batch. Nested and
repeated part quantities are aggregated before costing. Each part uses its saved
estimation model; no fleet-average or arbitrary default model is selected.

Blank inputs mean unknown, while zero costs and zero power are valid. Lifetime
hours must be positive when supplied. Missing model identity, time, or cost inputs
produce unavailable or partial estimates independently for each of the two lines.
Known amounts still contribute to partial product and shipping-inclusive totals.
Configure model assumptions and the electricity price in Settings; reprocess parts
when they lack estimation metadata or print times.

Estimates use current settings. Retaining a model preserves its configuration,
not a historical price snapshot. This feature does not track actual energy,
accounting depreciation, salvage value, or idle power. The failure allowance and
selling prices are in `docs/PRICING.md`.

## API

- `GET /api/v1/settings/general` and `PUT /api/v1/settings/general`: shared currency and electricity tariff. Currency defaults to USD; changing it relabels amounts without conversion.
- `GET /api/v1/settings/printer-costs`: all retained models and electricity price.
- `PUT /api/v1/settings/printer-costs/electricity`: set or clear the kWh price.
- `DELETE /api/v1/settings/printer-costs/models/{brand}/{modelCode}`: remove model
  cost settings when no fleet printer uses the model; otherwise returns 409.
  Repeated removal of an unused, absent model is a no-op returning 204.
- `PUT /api/v1/settings/printer-costs/models`: add/update the supported brand/model
  with nullable cost assumptions; omitted numeric fields are cleared.

Part summaries expose canonical estimation printer brand/model identity. Assembly
explosions expose per-part machine-cost contributions calculated with backend
decimal arithmetic. Draft Build previews use backend-calculated hourly rates.

Electricity price is edited in Settings → General. The legacy electricity endpoint remains compatible. Monetary values use the farm currency code, including high-precision unit rates.
