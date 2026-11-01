# Shared print estimates

`PrintEstimates` in Application/Parts is the source of print-time and filament planning estimates. Part detail and list responses, live assembly drafts, production previews, and remaining run workload use it. These are planning estimates, not deductions or measured consumption.

## Rules

- A part's stored figures describe one plate. Scale by requested units divided by units per plate, without rounding the estimate to a full plate. A partial final plate marks the result approximate: startup, purge and travel do not necessarily scale linearly. Actual plate counts and object selections remain the production planner's responsibility.
- Time and filament have separate nullable known subtotals and missing-contribution counts. Null means no known estimate; zero is a valid known estimate. A known subtotal with missing contributions is Partial. Empty work is known zero.
- Prefer a complete set of per-color gram estimates, including purge. Otherwise use the part's stored scalar weight. A scalar can be attributed to a color/variant only when there is one requirement; otherwise it appears as unattributed weight. Without a scalar fallback, preserve known color weights as a partial subtotal.
- Color grouping retains missing-contribution counts and distinguishes selected variants. Pricing consumes these already-scaled rows and never re-scales grams.
- Remaining run workload includes queued, printing and paused job targets, using each job's immutable production snapshot. Exclude completed, failed, cancelled, replaced and written-off jobs. These totals are not a live countdown or a prediction of completion time.

## Interfaces

- `PrintEstimate`: `time` and `filament` each contain nullable `value` and `missing`; `approximate` marks proportional partial plates; `colors` contains already-scaled grams, material, color, selected variant and missing count.
- Part detail exposes `printEstimate` for a full plate and `unitEstimate` for one unit. Part summaries expose the full-plate estimate.
- `POST /api/v1/parts/print-estimate` accepts `{ parts: [{ partId, quantity }] }` and returns a `PrintEstimate`. It is read-only, requires `parts:list`, aggregates repeated IDs, and loads estimate inputs in a single query without thumbnails. Missing source parts remain unknown; no file parsing, slicing, demand creation, or draft saving occurs.
- Production plans and plan components include `estimate`. Legacy `minutes`/`grams` totals remain available when complete. Run requirement responses include `remainingEstimate`.
- Frontend `printEstimates.ts` only sums server results and formats complete/partial/unavailable values. The assembly editor sends its current rolled-up draft quantities; it does not duplicate estimation arithmetic. Pricing and hardware calculations remain separate from print estimation.

## Verification

Regression coverage checks fractional quantities, full plates, multiple plates, per-color precedence, scalar fallback, missing metrics, valid zero, partial color groups, cross-endpoint agreement, snapshot preservation, and exclusion of completed/failed work. UI tests cover availability formatting, draft refresh/retry, and independence from pricing availability. No data migration or reprocessing is required for the estimation policy change. Existing snapshots are read as captured; actual inventory accounting is unchanged.
