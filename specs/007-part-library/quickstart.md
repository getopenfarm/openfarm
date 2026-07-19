# Phase 4 Quickstart: Part Library (with Sliced Files & In-App Slicing)

Supersedes the previous version — every add/replace, regardless of file type, now goes through the
identical Processing → Ready for Review → Confirm/Discard pipeline (`part-processing-jobs`, renamed from
`part-slicing-jobs`), and `name`/`tags` are supplied at Confirm, not at the initial upload. See
`data-model.md` for entity shapes and `contracts/part-library-api.md` for exact request/response bodies.
Prerequisites assume Phase 1–3 are already running.

## Prerequisites

```bash
cp .env.example infra/.env
cd infra && docker compose up   # postgres + api (applies this phase's migrations) + web
curl -f http://localhost:8888/health/ready
```

Log in as the seeded operator and keep the JWT. Confirm at least one printer and one imported Machine
Profile already exist (Phase 1/2) — note their ids — and at least one Filament catalog entry (Phase 3).

## Scenario 1 — Link a printer to a Machine Profile (FR-005a/b)

```bash
curl -s -X PUT http://localhost:8888/api/v1/printers/<printerId>/machine-profile \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"machineProfileId": "<machineProfileId>"}'
```

**Expected**: `200`, `{ "printerId": "...", "machineProfileId": "..." }`. Without at least one printer
linked this way, every requires-slicing add/replace below would instead return `422`.

## Scenario 2 — Add a part from a file requiring slicing: attach → process → review → save (User Stories 1 & 2)

```bash
curl -s -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" \
  -F "file=@bracket.stl" \
  -F "filamentId=<filamentId>" | tee part_job.json
JOB_ID=$(jq -r '.jobId // empty' part_job.json)
```

**Expected**: `202`, `{ "jobId": "...", "status": "Processing" }` — note `name` was never sent; in the real
UI, the operator would already be typing the part's name in the still-open dialog at this point. Poll or
subscribe:

```bash
curl -s http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID -H "Authorization: Bearer <token>" | tee job.json
```

**Expected**: eventually `{ "status": "ReadyForReview", "preview": { "nozzleDiameterMm": ..., "estimatedPrintTimeMinutes": ..., "estimatedFilamentWeightGrams": ..., "thumbnailAvailable": true, "filamentRequirements": [{ "slotIndex": null, "materialType": "PETG" }] } }`
— nothing under `/parts` shows this part yet. Confirm it (this is the "Save" click — `name` is required
here, since this is where it's finally submitted):

```bash
curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Widget Bracket", "tags": ["Brackets"]}' | tee part.json
PART_ID=$(jq -r .id part.json)
```

**Expected**: `201` — the part now appears under `GET /api/v1/parts` with the exact data reviewed a moment
ago, one `filamentRequirements[0]` entry, and a real thumbnail — never a placeholder or partial value.

## Scenario 2b — Discard instead of confirming (works both while Processing and once ReadyForReview)

Repeat the submit step above to get a second `JOB_ID`, then immediately (while still `Processing`, simulating
an operator hitting Cancel right away):

```bash
curl -i -X DELETE http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID/confirm -H "Authorization: Bearer <token>"
curl -s http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID -H "Authorization: Bearer <token>" | jq '.status'
```

**Expected**: `204` on discard; the job's `status` is now `"Discarded"`; no new part exists under
`GET /api/v1/parts` from this attempt.

## Scenario 2c — Attempting to save before the job is ready, or with no name, is rejected

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://localhost:8888/api/v1/part-processing-jobs/<a-still-Processing-jobId>/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"name": "Too Early"}'
# Expected: 409 — mirrors the UI's disabled Save button
```

## Scenario 3 — Add a part from an already-sliced file: the identical flow, just faster (User Story 3)

```bash
curl -s -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" \
  -F "file=@bracket.gcode" | tee part_job.json
JOB_ID=$(jq -r .jobId part_job.json)

curl -s http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID -H "Authorization: Bearer <token>" | jq '.status'
```

**Expected**: `202` on submit (no `filamentId` needed — an already-sliced file's material is parsed, not
targeted), and `status` typically already `"ReadyForReview"` by the time you check — parsing embedded
metadata needs no external process, so this resolves near-instantly, but goes through the same
`Processing` → `ReadyForReview` states an STL does. Confirm it the same way as Scenario 2:

```bash
curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Widget Bracket (pre-sliced)"}' | tee part.json
PART_ID=$(jq -r .id part.json)
```

**Expected**: `201`, with exactly one `filamentRequirements[0]` entry (`slotIndex: null`) parsed straight
from the G-code's own metadata. Confirm the thumbnail is available:

```bash
curl -s -o thumb.png -w '%{http_code}\n' http://localhost:8888/api/v1/parts/$PART_ID/thumbnail \
  -H "Authorization: Bearer <token>"
```

**Expected**: `200`, `Content-Type: image/png`.

## Scenario 3b — Add a multi-color already-sliced file, narrowing one slot at confirm time

```bash
curl -s -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" -F "file=@multicolor.gcode.3mf" | tee job.json
JOB_ID=$(jq -r .jobId job.json)
# wait for ReadyForReview, then:
curl -s http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID -H "Authorization: Bearer <token>" | jq '.preview.filamentRequirements'
# Expected: two or more entries, one per detected AMS slot

curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Two-Tone Vase", "filamentRequirementOverrides": [{"slotIndex": 0, "narrowFilamentVariantId": "<variantId>"}]}' \
  | jq '.filamentRequirements'
```

**Expected**: the `slotIndex: 0` entry shows `narrowFilamentVariantId` set to the supplied variant, others
remain unnarrowed.

## Scenario 4 — Rejections never even reach the processing pipeline (FR-006)

```bash
# No Filament specified for a file requiring slicing
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" -F "file=@bracket.stl"
# Expected: 400

# No printer with a linked Machine Profile at all (unlink the one from Scenario 1 first)
curl -s -X PUT http://localhost:8888/api/v1/printers/<printerId>/machine-profile \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"machineProfileId": null}'
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" -F "file=@bracket.stl" -F "filamentId=<filamentId>"
# Expected: 422
```

Re-link the printer (Scenario 1) before continuing.

## Scenario 5 — Replace a part's file, keeping name/tags, either fully succeeds or changes nothing (User Story 4)

```bash
curl -s -X PUT http://localhost:8888/api/v1/parts/$PART_ID/file \
  -H "Authorization: Bearer <token>" -F "file=@bracket_v2.gcode" | tee replace_job.json
REPLACE_JOB_ID=$(jq -r .jobId replace_job.json)

curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$REPLACE_JOB_ID/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{}' | tee replaced.json
```

**Expected**: `202` then `200` on confirm (no `name`/`tags` in the confirm body for a replace — `400` if
supplied), same `id`, updated `fileFormat`/derived data/thumbnail, `name`/`tags` unchanged. Repeat with a
deliberately corrupt file and confirm the initial `POST` itself returns `400`/`422` (or the job reaches
`Failed`) with the part's prior file and data completely unchanged.

## Scenario 6 — Tag and filter the catalog (User Story 5)

```bash
curl -s -X POST http://localhost:8888/api/v1/parts/$PART_ID/tags \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"name": "Enclosure"}'

curl -s "http://localhost:8888/api/v1/parts?tag=Enclosure" -H "Authorization: Bearer <token>" \
  | jq '.parts[] | {id, name, tags}'
```

**Expected**: only parts carrying the `Enclosure` tag are returned.

## Scenario 7 — Real-time job/catalog updates, no polling; Save gated on readiness (FR-009/FR-009b, Constitution Principle V)

With the web app open to the Parts catalog page, click "Add Part" and attach `bracket.stl` with a Filament
selected. Observe: the dialog stays open, a "Processing" indicator appears immediately
(`PartProcessingJobStatusChanged`, `status: "Processing"`) and the Save button is disabled; type a name and
add a tag while it's still processing — confirm the fields accept input but Save stays disabled. The moment
`status: "ReadyForReview"` arrives, the derived thumbnail/estimates appear inline and Save becomes enabled
(assuming a name has been entered). Clicking Save fires Confirm and the part appears in the catalog
(`PartUpserted`) — with no manual refresh at any point in the sequence.
