# Phase 4 Quickstart: Part Library (with Sliced Files & In-App Slicing)

Supersedes the previous version — every add/replace, regardless of file type, now goes through the
identical Processing → Ready for Review → Confirm/Discard pipeline (`part-processing-jobs`, renamed from
`part-slicing-jobs`), `name`/`tags` are supplied at Confirm, not at the initial upload, and filament-
requirement detection groups by distinct material+color pair across every plate/slot.

**A sixth follow-up round changes three more things reflected below**: (1) `filamentId` is removed from the
request entirely — Scenario 2 no longer sends one, Scenario 4b now demonstrates an STL's automatic "any
material" outcome directly rather than an omitted field, and Scenario 4c is new, covering an unsliced 3MF
whose own project material can't be parsed; (2) thumbnails are per-plate now (Scenario 3b); (3) `Confirm` no
longer waits on the estimate — Scenario 2 shows confirming as soon as nozzle diameter/material resolve, and
Scenario 2d (new) shows the trailing estimate arriving afterward on the already-saved part. See
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

An unsliced 3MF's own project material is used automatically — no `filamentId` field exists on this
request at all:

```bash
curl -s -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" \
  -F "file=@bracket_unsliced.3mf" | tee part_job.json
JOB_ID=$(jq -r '.jobId // empty' part_job.json)
```

**Expected**: `202`, `{ "jobId": "...", "status": "Processing" }` — note `name` was never sent; in the real
UI, the operator would already be typing the part's name in the still-open dialog at this point. Poll or
subscribe:

```bash
curl -s http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID -H "Authorization: Bearer <token>" | tee job.json
```

**Expected**: `status` reaches `"ReadyForReview"` **quickly** — nozzle diameter and material no longer wait
on the slice itself:
```jsonc
{ "status": "ReadyForReview", "preview": {
    "nozzleDiameterMm": 0.4,
    "filamentRequirements": [{ "requirementIndex": 0, "materialType": "PETG", "detectedColorHex": null }],
    "estimateStatus": "Resolving", "estimatedPrintTimeMinutes": null, "estimatedFilamentWeightGrams": null,
    "plateCount": 1, "thumbnails": []
} }
```
`materialType: "PETG"` came from the 3MF's own project settings — nothing was supplied on the request.
`estimateStatus: "Resolving"` means the slicer CLI is still running in the background; Confirm is already
available at this point (see below) — you don't have to wait for it. To instead watch it resolve first:

```bash
watch -n1 "curl -s http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID -H 'Authorization: Bearer <token>' | jq '.preview.estimateStatus'"
# Expected: transitions Resolving -> Available within a few seconds, then Ctrl-C
curl -s http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID -H "Authorization: Bearer <token>" | tee job.json | jq '.preview'
```

**Expected**: `estimateStatus: "Available"`, real `estimatedPrintTimeMinutes`/`estimatedFilamentWeightGrams`,
and `thumbnails: [{ "plateIndex": 0, "thumbnailBase64": "iVBORw0KGgo..." }]`. Decode it locally to confirm
it's a real image:

```bash
jq -r '.preview.thumbnails[0].thumbnailBase64' job.json | base64 -d > preview_thumb.png
file preview_thumb.png   # Expected: PNG image data
```

Confirm the job (this is the "Save" click — `name` is required here, since this is where it's finally
submitted). This works identically whether `estimateStatus` is still `"Resolving"` or already `"Available"`
— see Scenario 2d for the early-confirm case:

```bash
curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Widget Bracket", "tags": ["Brackets"]}' | tee part.json
PART_ID=$(jq -r .id part.json)
```

**Expected**: `201` — the part now appears under `GET /api/v1/parts` with the exact data reviewed a moment
ago, one `filamentRequirements[0]` entry, and a real thumbnail — never a placeholder or partial value (since
`estimateStatus` was already `"Available"` by the time this ran).

## Scenario 2d — Confirm before the estimate finishes; the trailing update arrives on the saved part

Repeat the submit step above with a large/slow-to-slice file, but confirm **immediately** after
`ReadyForReview`, without waiting for `estimateStatus: "Available"`:

```bash
curl -s -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" -F "file=@large_unsliced.3mf" | tee job2.json
JOB_ID2=$(jq -r .jobId job2.json)
# wait only for ReadyForReview (nozzle diameter/material), not for estimateStatus:
curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID2/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Large Panel"}' | tee early_part.json
EARLY_PART_ID=$(jq -r .id early_part.json)
jq '{estimateStatus, estimatedPrintTimeMinutes}' early_part.json
```

**Expected**: `201`, `estimateStatus: "Resolving"`, `estimatedPrintTimeMinutes: null` — the part already
exists in the catalog with its nozzle diameter/material set. Wait for the slice to finish in the background,
then re-fetch:

```bash
sleep 5
curl -s http://localhost:8888/api/v1/parts/$EARLY_PART_ID -H "Authorization: Bearer <token>" \
  | jq '{estimateStatus, estimatedPrintTimeMinutes, estimatedFilamentWeightGrams}'
```

**Expected**: `estimateStatus: "Available"` with real numbers now populated — in the real UI this update
arrives via the `PartEstimateResolved` SignalR event (`contracts/part-library-api.md`), not a manual
refresh.

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

**Expected**: `202` on submit (there is no `filamentId` field at all — an already-sliced file's material is
always parsed, never targeted), and `status` typically already `"ReadyForReview"` **with
`preview.estimateStatus: "Available"` already** by the time you check — parsing embedded metadata needs no
external process, so nozzle diameter, material, estimate, and thumbnails all resolve together, near-
instantly. This is the one file type where the two-milestone split from Scenario 2 collapses back into a
single moment. It still goes through the same `Processing` → `ReadyForReview` states an STL does. Confirm it
the same way as Scenario 2:

```bash
curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Widget Bracket (pre-sliced)"}' | tee part.json
PART_ID=$(jq -r .id part.json)
```

**Expected**: `201`, with exactly one `filamentRequirements[0]` entry (`requirementIndex: 0`) parsed straight
from the G-code's own metadata, and `estimateStatus: "Available"` immediately (no `"Resolving"` gap for this
file type). Confirm the thumbnail travelled back inline on this same response (there is no separate
thumbnail endpoint — `research.md` §8, since a bare authenticated byte-stream URL isn't renderable via
`<img src>` under this app's Bearer-token auth):

```bash
jq -r '.thumbnails[0].thumbnailBase64' part.json | base64 -d | file -
```

**Expected**: `... PNG image data ...`. The same `thumbnails[]` array also appears on
`GET /api/v1/parts/$PART_ID` afterward; the catalog list endpoint (`GET /api/v1/parts`) instead exposes just
`plateIndex: 0`'s image as a single `thumbnailBase64` field per row, bounded by `pageSize`.

## Scenario 3b — Add a multi-plate/multi-color already-sliced file: grouped, not per-plate, requirements

Using a 4-plate `.gcode.3mf` where plates 1 and 3 are both black PETG, plate 2 is white PLA, and plate 4 is
red PETG:

```bash
curl -s -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" -F "file=@four_plate_multicolor.gcode.3mf" | tee job.json
JOB_ID=$(jq -r .jobId job.json)
# wait for ReadyForReview, then:
curl -s http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID -H "Authorization: Bearer <token>" \
  | jq '.preview.filamentRequirements'
```

**Expected**: exactly **3** entries (not 4, not 1) — plates 1 and 3's shared black-PETG pair collapses into
one entry:
```jsonc
[
  { "requirementIndex": 0, "materialType": "PETG", "detectedColorHex": "#000000" },
  { "requirementIndex": 1, "materialType": "PLA",  "detectedColorHex": "#FFFFFF" },
  { "requirementIndex": 2, "materialType": "PETG", "detectedColorHex": "#FF0000" }
]
```

Narrow the black-PETG requirement (`requirementIndex: 0`) at confirm time:

```bash
curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Four-Plate Batch", "filamentRequirementOverrides": [{"requirementIndex": 0, "narrowFilamentVariantId": "<variantId>"}]}' \
  | tee part.json | jq '.filamentRequirements'
```

**Expected**: the `requirementIndex: 0` entry (which represents **both** plate 1 and plate 3) shows
`narrowFilamentVariantId` set to the supplied variant; the PLA and red-PETG requirements remain unnarrowed.
Confirm the aggregate estimates reflect all four plates, and that each plate got its own thumbnail:

```bash
jq '{estimatedPrintTimeMinutes, estimatedFilamentWeightGrams, plateCount, thumbnailCount: (.thumbnails | length)}' part.json
```

**Expected**: `estimatedPrintTimeMinutes`/`estimatedFilamentWeightGrams` are the sum across all four plates'
own individual estimates, not just plate 1's; `plateCount: 4`; `thumbnailCount: 4` (one entry in
`thumbnails[]` per plate, `plateIndex` 0 through 3) — this is new behavior as of the sixth follow-up round;
previously only `plateIndex: 0` would have had one.

## Scenario 4 — Rejections never even reach the processing pipeline (FR-006)

```bash
# No printer with a linked Machine Profile at all (unlink the one from Scenario 1 first) —
# this is the one thing that still rejects an STL/unsliced-3MF outright
curl -s -X PUT http://localhost:8888/api/v1/printers/<printerId>/machine-profile \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"machineProfileId": null}'
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" -F "file=@bracket.stl"
# Expected: 422 — "any material" still needs a nozzle-diameter target to slice against
```

Re-link the printer (Scenario 1) before continuing.

## Scenario 4b — An STL always produces "any material" automatically (FR-002a)

```bash
curl -s -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" -F "file=@bracket.stl" | tee job.json
JOB_ID=$(jq -r .jobId job.json)
```

**Expected**: `202` — an STL never has a material to supply or parse, so this is always the normal case, not
a rejection; there is no field to omit anymore (`filamentId` doesn't exist on the request at all). Once
`ReadyForReview`:

```bash
curl -s http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID -H "Authorization: Bearer <token>" \
  | jq '.preview.filamentRequirements'
```

**Expected**: `[{ "requirementIndex": 0, "materialType": null, "detectedColorHex": null, "detectedSubBrand": null }]`
— nozzle diameter/material are present as soon as `ReadyForReview` is reached; the estimate/thumbnail follow
shortly after (`estimateStatus`, Scenario 2), computed against a fixed internal fallback material, not a
real operator-chosen one — a known, accepted approximation (SC-003). Confirm it, then optionally narrow it
to a real filament after the fact, exactly like any other requirement:

```bash
curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$JOB_ID/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Generic Mounting Bracket"}' | tee any_material_part.json
ANY_MATERIAL_PART_ID=$(jq -r .id any_material_part.json)
jq '.filamentRequirements' any_material_part.json

curl -s "http://localhost:8888/api/v1/parts?page=1&pageSize=50" -H "Authorization: Bearer <token>" \
  | jq --arg id "$ANY_MATERIAL_PART_ID" '.parts[] | select(.id == $id) | .materialTypes'
```

**Expected**: `filamentRequirements[0].materialType` is `null` on the detail response; the catalog-list
response's `materialTypes` field for this same part is `[]` (empty, not `[null]`).

## Scenario 4c — An unsliced 3MF with no parseable project material is a genuine rejection, not "any material"

Using a hand-crafted or corrupted 3MF project with no filament preset assignment in its
`Metadata/project_settings.config`:

```bash
curl -s -X POST http://localhost:8888/api/v1/parts \
  -H "Authorization: Bearer <token>" -F "file=@no_material.3mf" | tee bad_job.json
BAD_JOB_ID=$(jq -r .jobId bad_job.json)
sleep 1
curl -s http://localhost:8888/api/v1/part-processing-jobs/$BAD_JOB_ID -H "Authorization: Bearer <token>" \
  | jq '{status, errorMessage}'
```

**Expected**: `202` on submit (this failure can only be discovered by opening the file, so it's not a
synchronous `400`/`422` the way the missing-nozzle-diameter case is), then `status: "Failed"` with a
populated `errorMessage` — **not** `"ReadyForReview"` with a `materialType: null` requirement. This is the
one behavioral difference between an STL and an unsliced 3MF that fails this way: the STL path (Scenario 4b)
always succeeds via the fixed fallback; a 3MF never falls back to it.

## Scenario 5 — Replace a part's file, keeping name/tags, either fully succeeds or changes nothing (User Story 4)

```bash
curl -s -X PUT http://localhost:8888/api/v1/parts/$PART_ID/file \
  -H "Authorization: Bearer <token>" -F "file=@bracket_v2.gcode" | tee replace_job.json
REPLACE_JOB_ID=$(jq -r .jobId replace_job.json)

curl -s -X POST http://localhost:8888/api/v1/part-processing-jobs/$REPLACE_JOB_ID/confirm \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{}' | tee replaced.json
```

**Expected**: `202` then `200` on confirm (no `name`/`tags` in the confirm body for a replace — `400` if
supplied), same `id`, updated `fileFormat`/derived data/thumbnails, `name`/`tags` unchanged. Repeat with a
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

## Scenario 7 — Real-time job/catalog updates, no polling; Save gated on nozzle diameter/material only (FR-009/FR-009b/FR-009c, Constitution Principle V)

With the web app open to the Parts catalog page, click "Add Part" and attach a large unsliced 3MF. Observe:
the dialog stays open, a "Processing" indicator appears immediately (`PartProcessingJobStatusChanged`,
`status: "Processing"`) and the Save button is disabled; type a name and add a tag while it's still
processing — confirm the fields accept input but Save stays disabled. The moment `status: "ReadyForReview"`
arrives — well before the estimate finishes for a large file — the derived nozzle diameter/material appear
inline, an "estimating…" indicator shows in place of print time/weight, and Save becomes enabled (assuming a
name has been entered). Click Save immediately, without waiting further: Confirm fires and the part appears
in the catalog (`PartUpserted`) still showing "estimating…". A few moments later, with no action taken and no
manual refresh, the catalog row updates in place to show the real print time/weight (`PartEstimateResolved`)
— confirming the entire flow, including the trailing update, is driven by SignalR pushes end to end.
