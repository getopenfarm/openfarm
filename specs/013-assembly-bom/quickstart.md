# Quickstart & Validation: Assembly (BOM)

Runnable scenarios that prove the feature end-to-end. References `contracts/assembly-bom-api.md` and
`data-model.md` rather than restating them. Commands assume the repo conventions in `CLAUDE.md`.

## Prerequisites

- .NET 10 SDK, Bun, Docker (Testcontainers / local stack); `ConnectionStrings__Default` set.
- **Zero printers, files, profiles, jobs, or runs required anywhere below** — proving that is itself
  Story 5/SC-006, so don't set any of it up.
- A handful of existing catalog `Part`s (Phase 4) to reference — e.g. `Bracket`, `Knob`.

## Story 1 — Define a nested assembly and browse it

1. `POST /api/v1/non-printed-components` `{ "name": "M3x8 screw", "unit": "each", "unitCost": 0.02 }`
   → `201`, note `id`.
2. `POST /api/v1/assemblies` `{ "name": "Handle", "lines": [{ "targetKind": "Part", "targetId": "<Knob>", "quantity": 2 }] }`
   → `201`, note `id` (`handleId`).
3. `POST /api/v1/assemblies` `{ "name": "Enclosure", "lines": [
     { "targetKind": "Part", "targetId": "<Bracket>", "quantity": 4 },
     { "targetKind": "Assembly", "targetId": "<handleId>", "quantity": 2 },
     { "targetKind": "NonPrintedComponent", "targetId": "<screwId>", "quantity": 6 }
   ] }` → `201` (Acceptance Scenario 1/2).
4. `GET /api/v1/assemblies/{enclosureId}` → `lines` shows all three, the `Assembly`-kind line named
   `"Handle"`; `GET /api/v1/assemblies/{handleId}` independently still returns Handle's own two-knob
   structure unchanged (Acceptance Scenario 2 — Handle "remains independently usable").
5. `POST /api/v1/assemblies` an assembly with `"lines": []` → `400`/`422` (FR-002, spec Edge Cases).
6. On an existing assembly with exactly one line, `DELETE .../lines/{lastLineId}` → `409` (FR-002).

## Story 2 — Cycle and depth rejection, save-time only

1. Attempt `POST /api/v1/assemblies/{handleId}/lines` `{ "targetKind": "Assembly", "targetId": "<enclosureId>", "quantity": 1 }`
   (Handle including Enclosure, which already includes Handle) → `409`, `detail` names the containment
   path `Handle → Enclosure → Handle` (Acceptance Scenario 3, FR-007).
2. Attempt `POST /api/v1/assemblies/{handleId}/lines` `{ "targetKind": "Assembly", "targetId": "<handleId>", "quantity": 1 }`
   (direct self-reference) → `409` (spec Edge Cases — "identical treatment").
3. Build a chain of 10 nested assemblies (A1 ⊃ A2 ⊃ … ⊃ A10, each including the next with quantity 1),
   confirm all 10 saves succeed. Attempt to add an 11th level → `409`, `detail` states the 10-level limit
   (Acceptance Scenario 4, FR-006a).
4. With A5 (mid-chain) already at some depth, add a *new*, previously-unrelated assembly B that includes
   A1 (pushing A1's own consumers deeper) such that some existing path through A1→…→A10 would now exceed
   10 — confirm the save is rejected even though the edit was made to B, not to any assembly on the
   A-chain itself (spec Edge Cases — FR-008's upward evaluation).

## Story 3 — Concurrent cross-assembly cycle prevention (FR-007a)

1. Create two independent, unrelated assemblies X and Y (each with at least one Part line, no
   relationship to each other).
2. Fire two requests concurrently (two parallel `WebApplicationFactory` clients, or two terminals hitting
   a running dev server at the same instant): `POST /api/v1/assemblies/{x}/lines` targeting Y, and
   `POST /api/v1/assemblies/{y}/lines` targeting X.
3. **Expected**: exactly one of the two returns `201`; the other returns `409` naming the cycle that
   committing both would have created. `GET /api/v1/assemblies/{x}` and `/{y}` afterward show a single
   consistent, acyclic result — never both edges present (Acceptance Scenario 3 under Edge Cases, SC-003's
   "0 cyclic structures... under no interleaving").

## Story 4 — Explosion is pure, aggregates correctly, and is fast

1. `GET /api/v1/assemblies/{enclosureId}/explosion?quantity=4` → `printedParts` includes Bracket at
   `16` (4 × 4) and Knob at `16` (4 requested × 2 Handles × 2 Knobs/Handle); `nonPrintedComponents`
   includes the screw at `24` (Acceptance Scenario 1/2, FR-009a/b/d).
2. Call the same request 100 times → identical result every time; `GET /api/v1/assemblies` before and
   after shows no row changed (Acceptance Scenario 3, SC-002 — nothing persisted as a side effect).
3. Build the SC-007 reference: one assembly with 500 total BOM lines spread across the full 10 levels →
   explode it → completes in under 1 second, returns one aggregated row per distinct part regardless of
   how many paths contributed (SC-007).
4. `GET .../explosion?quantity=0` and `?quantity=-1` → `400` (FR-009e, spec Edge Cases).

## Story 5 — Buildable units

1. Using Story 1's Enclosure (needs 4 Bracket + 4 Knob per unit, after explosion), `POST
   /api/v1/assemblies/{enclosureId}/buildable-units` `{ "goodQuantities": { "<Bracket>": 10, "<Knob>": 12 } }`
   → `{ "isApplicable": true, "value": 2 }` (`min(floor(10/4)=2, floor(12/4)=3) = 2`).
2. Same assembly, `{ "goodQuantities": { "<Bracket>": 90, "<Knob>": 0 } }` → `value: 0` (Acceptance
   Scenario 2, FR-010c).
3. Same request but omitting the non-printed screw line's target entirely from `goodQuantities` → result
   unchanged from step 1 (Acceptance Scenario 4/SC-005 — non-printed lines never affect the value; remove
   the screw line from the assembly entirely and repeat step 1 to directly confirm SC-005's "identical to
   the value with them removed").
4. Create an assembly with only non-printed component lines (no Part/sub-assembly line at all) — this
   itself must be rejected at creation per FR-002/FR-003 (a line must target *something* real, and at
   least one line is required, but nothing requires that line be a Part) — so instead build one with a
   sub-assembly + non-printed lines only, no direct or transitive Part anywhere → `buildable-units` →
   `{ "isApplicable": false, "value": null, "notApplicableReason": "..." }` (FR-010d — never `0`, never
   unbounded).

## Story 6 — Deletion is blocked, never cascaded (FR-011)

1. `DELETE /api/v1/assemblies/{handleId}` (still referenced by Enclosure) → `409`, `detail` names
   `"Enclosure"`.
2. `DELETE /api/v1/parts/{bracketId}` (referenced by Enclosure's BOM line) → `409`, `detail` names
   `"Enclosure"` (narrows Phase 4's own "delete cascades to the part's own data" rule — spec Edge Cases).
3. `DELETE /api/v1/non-printed-components/{screwId}` (referenced by Enclosure) → `409`.
4. Remove Enclosure's line referencing Handle (`DELETE .../lines/{lineId}`), then retry step 1 →
   succeeds (`204`) — deletion is unblocked once nothing references it, never forced through.

## Story 7 — Zero printer knowledge required (SC-006)

Run Stories 1–6 above against a fresh organization with **0 printers registered, 0 files uploaded, 0
profiles imported, 0 jobs/runs of any kind** — every call above succeeds unmodified. This is the whole of
SC-006; no separate setup or teardown is needed to prove it beyond simply not creating any of that data.

## Activity log check (FR-015)

After Stories 1, 2 (successful saves only), and 6, `GET` the activity log filtered to the created
assemblies/components → entries exist for assembly creation, each line add/remove, and non-printed
component creation, each attributed to the acting operator with a timestamp.
