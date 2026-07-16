# Phase 2 Quickstart: Profiles (Machine + Filament, Imported from Slicer Bundles)

Validation guide proving User Stories 1–4 end-to-end. See `data-model.md` for entity shapes and
`contracts/profiles-api.md` for exact request/response bodies. No physical hardware or live printer
connection is involved anywhere in this feature (spec.md's Assumptions) — everything here is pure
file-upload + catalog CRUD against a real Postgres instance.

## Prerequisites

```bash
cp .env.example infra/.env
cd infra && docker compose up   # postgres + api (dotnet watch, applies the AddProfiles migration) + web (bun run dev)
curl -f http://localhost:8888/health/ready
```

Log in as the seeded operator (see `specs/001-foundation/quickstart.md` Scenario 1) and keep the JWT
for the calls below. You'll also need at least one real exported bundle — per `research.md` §1's
follow-up task, capture one from a real OrcaSlicer or Bambu Studio install (File → Export → Export
Preset Bundle) containing a handful of machine and filament presets, at least one of which overrides
another (e.g. a color-specific filament preset derived from a generic base).

## Scenario 1 — Preview a bundle without persisting anything (User Story 1, FR-001–003)

```bash
curl -i -X POST http://localhost:8888/api/v1/profiles/imports/preview \
  -H "Authorization: Bearer <token>" \
  -F "file=@MyProfiles.zip"
```

**Expected**: `200`, a JSON body listing every machine and filament preset found in the bundle
(`contracts/profiles-api.md`). Confirm the count matches what you'd see inside the slicer's own preset
list for that export. Confirm nothing new appears yet in the catalog:

```bash
curl -s http://localhost:8888/api/v1/profiles/machine -H "Authorization: Bearer <token>" | jq '.profiles | length'
curl -s http://localhost:8888/api/v1/profiles/filament -H "Authorization: Bearer <token>" | jq '.profiles | length'
```

**Expected**: both counts unchanged from before the preview call — SC-004 (an unconfirmed import leaves
zero new profiles).

In the browser: open **Profiles → Import**, choose the same file, and confirm the review/selection list
renders with a name/type/base-or-override column for every preset (FR-003), and that closing the dialog
without confirming leaves the catalog unchanged (Acceptance Scenario 3).

## Scenario 2 — Select a subset and confirm (User Story 1, FR-004; SC-001, SC-003)

In the browser's review list, uncheck all but one machine preset and a few filament presets, then
confirm. Time the whole flow from initial upload to seeing the result — should be well under 2 minutes
(SC-001) for a bundle under 50 presets.

```bash
curl -s http://localhost:8888/api/v1/profiles/machine -H "Authorization: Bearer <token>" | jq '.profiles | length'
```

**Expected**: exactly the number of machine presets you selected — the unselected ones are absent, not
just hidden (SC-003). Repeat for `/api/v1/profiles/filament`.

## Scenario 3 — Base/override relationship survives import (User Story 2, FR-005; SC-002)

Using a bundle with a base filament preset and a derived one, select **both** in the review list and
confirm.

```bash
curl -s "http://localhost:8888/api/v1/profiles/filament" -H "Authorization: Bearer <token>" \
  | jq '.profiles[] | select(.baseProfileId != null)'
```

**Expected**: the derived profile's `baseProfileId` is set (not null) and points at the base profile's
own `id` from the same response — a real parent/child link, not a flattened independent copy (SC-002).

**Chain-merged settings are complete, not just the link** (`research.md` §10): since real slicer exports
are delta-only, the derived preset's *own* `rawDataJson` typically lacks nozzle temp/bed temp/etc.
entirely — those exist only on the base. Confirm the derived profile's *derived display fields* are
nonetheless complete by pulling values from the resolved base:

```bash
curl -s "http://localhost:8888/api/v1/profiles/filament" -H "Authorization: Bearer <token>" \
  | jq '.profiles[] | select(.baseProfileId != null) | {name, nozzleTempC, bedTempC, hasUnresolvedBaseInChain}'
```

**Expected**: `nozzleTempC`/`bedTempC` are populated (pulled from the base row, not the derived row's
own sparse `rawDataJson`) and `hasUnresolvedBaseInChain` is `false` — the chain resolved completely
because both levels were imported together. Also check the *preview* response from Scenario 1/2 for the
same bundle and confirm it already showed these same complete values before you ever clicked confirm —
the review list must not show incomplete data that only becomes complete after import (`research.md`
§10's preview-side fix).

Now repeat with a **fresh** bundle, this time selecting only the derived preset and leaving its base
unchecked. Before clicking confirm in the browser, confirm the review list surfaces a visible warning
that the base is present in the bundle but not selected (FR-006, computed client-side per
`research.md` §4 — no separate API call backs this). Also confirm the preview item for this preset shows
`"hasUnresolvedBaseInChain": true`, since its declared base isn't going to be imported. Confirm anyway:

```bash
curl -s "http://localhost:8888/api/v1/profiles/filament" -H "Authorization: Bearer <token>" \
  | jq '.profiles[] | select(.name == "<the derived preset name>")'
```

**Expected**: the profile exists, `baseProfileName` is populated with the declared base's name, but
`baseProfileId` is `null` and `hasUnresolvedBaseInChain` is `true` — an unresolved reference is a normal
outcome, not an error (Acceptance Scenario 3, US2), but the response makes the resulting incompleteness
visible rather than silently showing blank settings fields with no explanation. Confirm `nozzleTempC`/
`bedTempC`/etc. on this profile are absent, not zero or a misleading default.

**Ambiguous base match** (`research.md` §3): since FR-009 permits importing a duplicate-named preset as
a separate row, `Name` isn't unique — confirm the resolution algorithm treats an ambiguous match the
same as no match, never a guess. Import a filament preset named e.g. `"Generic PLA"`, then import a
*different* bundle containing another filament preset also named `"Generic PLA"`, choosing
"import as new" at the duplicate-detection prompt so both exist as separate catalog rows:

```bash
curl -s "http://localhost:8888/api/v1/profiles/filament" -H "Authorization: Bearer <token>" \
  | jq '[.profiles[] | select(.name == "Generic PLA")] | length'
```

**Expected**: `2`. Now import a third preset whose declared base name is `"Generic PLA"` (either a
third distinct bundle, or the same bundle if it contains an override preset naming that base):

```bash
curl -s "http://localhost:8888/api/v1/profiles/filament" -H "Authorization: Bearer <token>" \
  | jq '.profiles[] | select(.name == "<the third preset name>")'
```

**Expected**: `baseProfileName` is `"Generic PLA"`, but `baseProfileId` is `null` — the same unresolved
shape as the no-match case above, even though a same-named row *does* exist (two of them, in fact).
Resolving to one of the two arbitrarily would be a wrong, silent guess; the system doesn't do that.

## Scenario 4 — Rejected and empty bundles (FR-007, FR-008; SC-005)

```bash
echo "not a zip" > bad.zip
curl -i -X POST http://localhost:8888/api/v1/profiles/imports/preview \
  -H "Authorization: Bearer <token>" -F "file=@bad.zip"
```

**Expected**: `422` `ProblemDetails` naming the file as unrecognized/corrupted (FR-007). Confirm no
catalog rows were created by either count check from Scenario 1.

```bash
zip empty.zip /dev/null   # or any zip with no machine/filament JSON inside
curl -i -X POST http://localhost:8888/api/v1/profiles/imports/preview \
  -H "Authorization: Bearer <token>" -F "file=@empty.zip"
```

**Expected**: `200` with both `machinePresets`/`filamentPresets` empty — the browser shows "nothing
importable was found" rather than an empty-looking success screen with no explanation (FR-008).

## Scenario 5 — Duplicate detection (edge case)

Re-upload the same bundle from Scenario 2 and preview it again.

```bash
curl -s -X POST http://localhost:8888/api/v1/profiles/imports/preview \
  -H "Authorization: Bearer <token>" -F "file=@MyProfiles.zip" \
  | jq '[.machinePresets[], .filamentPresets[]] | map(select(.isDuplicate == true)) | length'
```

**Expected**: greater than zero for the presets you already imported in Scenario 2. In the browser,
confirm the review list flags these and lets you choose skip vs. import-as-new per item, and that
choosing "import as new" for one and confirming produces a second, separate catalog row with the same
name (not a silent overwrite of the first).

## Scenario 6 — Browse catalogs and view/edit raw data (User Stories 3–4; SC-006)

In the browser, open **Profiles**, switch between the Machine and Filament tabs, and confirm every
imported profile is listed with its key fields (US3 Acceptance Scenarios 1–2). From the catalog list,
open any one profile — should take no more than 3 clicks/interactions total from the catalog list
(SC-006) — and confirm its raw JSON viewer shows the full original preset data.

**Compatible-printer declaration** (FR-016, `research.md` §11): using the bundle from Scenario 2 (a
printer preset plus its compatible filament presets — spec's Acceptance Scenario 6), open one of the
imported filament profiles and confirm its declared compatible printer name is shown, as plain text —
this is a display-only field this feature, not resolved against the machine-profile catalog:

```bash
curl -s "http://localhost:8888/api/v1/profiles/filament" -H "Authorization: Bearer <token>" \
  | jq '.profiles[] | {name, compatiblePrinterNames}'
```

**Expected**: `compatiblePrinterNames` lists the printer preset's name for any filament profile that
declared one. Repeat with a single-preset `.orca_filament` export uploaded on its own (its compatible
printer preset not imported) and confirm the same field is populated identically — whether or not the
named printer exists elsewhere in the catalog makes no difference to this response, since no resolution
is attempted.

Edit one field's value (e.g. bump a temperature) and save:

```bash
curl -s http://localhost:8888/api/v1/profiles/filament/<id> -H "Authorization: Bearer <token>" | jq -c .rawDataJson > before.json
curl -i -X PUT http://localhost:8888/api/v1/profiles/filament/<id>/raw-data \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"rawDataJson": "{ \"bed_temperature\": [\"60\"] }"}'
curl -s http://localhost:8888/api/v1/profiles/filament/<id> -H "Authorization: Bearer <token>" | jq .rawDataJson
```

**Expected**: the second `GET` reflects the new value. Then try an invalid save:

```bash
curl -i -X PUT http://localhost:8888/api/v1/profiles/filament/<id>/raw-data \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"rawDataJson": "{ not valid json"}'
```

**Expected**: `422`, and a follow-up `GET` on the same id shows the *previous* valid value unchanged
(FR-013 — malformed JSON never partially overwrites the stored row).

## Activity log coverage check (constitution Security section, `research.md` §7)

After Scenario 2's confirm call, check the activity log directly (or via the Activity Log page in the
UI) and confirm one `ProfileImported` entry exists **per profile created**, not one entry for the whole
batch — this is what the `IHasEntityIds` post-processor extension exists to guarantee.
