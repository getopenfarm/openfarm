# Contract: Part Library REPR endpoints (`OpenFarm.Api`)

Supersedes the previous version of this file in full. Every add/replace, regardless of file type, now goes
through one uniform pipeline: `PartProcessingJob` (renamed from `PartSlicingJob`), with `Status: Processing
| ReadyForReview | Confirmed | Failed | Discarded` for every file format — there is no more synchronous
`201`-on-submit branch for an already-sliced file. **`filamentId` no longer exists on any request** — an
operator never selects a material at upload time; it always resolves from the file itself (`research.md`
§1/§7a/§13). **`name`/`tags` are not part of the initial request at all** — they (plus any narrow filament
overrides) are submitted together at the Confirm step, so an operator can keep editing them in the same
dialog while processing runs in the background. **`ReadyForReview` no longer implies every field is
present** — nozzle diameter and material resolve (and gate Confirm) independently of, and typically well
before, estimated print time/weight and per-plate thumbnails for a requires-slicing file; a
`preview.estimateStatus`/`estimateStatus` field distinguishes "still resolving" from "available" throughout
this contract (`research.md` §3b). REPR pattern, one FastEndpoints class per endpoint,
`[Permission("...")]` per Phase 0's placeholder-policy precedent, all errors as RFC 7807 `ProblemDetails`.
See `data-model.md` for entity shapes and `research.md` for the reasoning.

**Thumbnails are now per-plate, and travel as inline base64, never as a bare byte-stream URL.** This app
authenticates via JWT Bearer tokens only (no cookies), so a plain `<img src="...">` pointed at an
authenticated endpoint cannot attach the required `Authorization` header — the browser would request it
unauthenticated and get a `401`. Every response that includes a part or a job's preview therefore embeds a
`thumbnails: [{ plateIndex, thumbnailBase64 }]` array (each a base64-encoded PNG, renderable directly as
`data:image/png;base64,${thumbnailBase64}`), exactly mirroring the one existing precedent for this in the
codebase — `features/printers/components/CameraView.tsx` already renders a live camera frame the same way.
A paginated `GET /parts` list row keeps a single `thumbnailBase64: string | null` for just `plateIndex: 0`
(the catalog thumbnail), rather than inlining every plate's image into a bounded list response; the full
per-plate `thumbnails[]` array only appears on the detail/job-preview shapes below. There is no separate
`GET .../thumbnail` byte-stream endpoint (an earlier revision of this contract had one, and it would not
have actually been renderable as designed). This does inline a modest amount of payload into non-list
responses, but per-part/per-job, not per the full 500+-part catalog SC-005 talks about filtering — so it
stays small in practice.

## Parts

### `POST /api/v1/parts`
`[Permission("parts:create")]` — multipart/form-data (`AllowFileUploads()`). Starts processing; does **not**
create a `Part`.

**Request** (multipart fields):
```text
file: <binary>       # required — STL, 3MF, or G-code — the only field. No filamentId, name, or tags.
```
Material is always resolved from the file itself, never supplied here (`research.md` §1/§7a/§13):
- **STL**: no material can ever be present — always slices against a fixed internal fallback, producing a
  fully unconstrained ("any material") requirement (FR-002a).
- **Unsliced 3MF**: its own project-level filament assignment is parsed and used as the slicing target — see
  Response 422 below for what happens if that can't be parsed.
- **G-code / already-sliced 3MF**: material is parsed directly from embedded slice metadata, same as before.

No `name`, `tags`, or narrow filament overrides here either — see Confirm below.

**Response 202** (always, for every file format):
```jsonc
{ "jobId": "guid", "status": "Processing" }
```

**Response 400** — unrecognized file format.
**Response 422** — file requires slicing but no registered printer has a `MachineProfile` linked yet
(`NozzleDiameterResolver` returned nothing — FR-006a). Checked synchronously, before a `PartProcessingJob`
row is even created — a request that's going to be rejected this way never enters the async pipeline at all.

An unsliced 3MF whose own project-level material assignment can't be parsed is **not** a submit-time
rejection — reading it requires opening the file, so this failure surfaces asynchronously instead: the job
is created (`202` as above), then transitions straight to `Status: "Failed"` with an `errorMessage`
(`GET .../part-processing-jobs/{id}` or the `PartProcessingJobStatusChanged` push — see below) rather than
ever reaching `ReadyForReview`. An STL is never subject to this failure mode at all (FR-002a's fallback
always succeeds for it).

### `GET /api/v1/parts`
`[Permission("parts:list")]`

Query params: `tag` (optional, repeatable — filters to parts carrying **all** given tags), `page`,
`pageSize`.

**Response 200**:
```jsonc
{
  "parts": [
    { "id", "name", "fileFormat", "requiresSlicing", "nozzleDiameterMm",
      "estimateStatus": "Available", "estimatedPrintTimeMinutes": 47, "estimatedFilamentWeightGrams": 18.4,
      "thumbnailBase64": "iVBORw0KGgo..." | null,
      "materialTypes": ["PETG"], "tags": [...] }
  ],
  "totalCount": 512
}
```
`materialTypes` is the distinct set of non-null `filamentRequirements[].materialType` values, for compact
catalog display — the full per-slot breakdown is only on the detail endpoint. An "any material" part
(FR-002a, STL-only) has `materialTypes: []` (empty, not `[null]`) — the catalog UI shows an "Any material"
badge for that case instead of a material-type chip. `estimateStatus` is `"Resolving"` | `"Available"` |
`"Unavailable"` (`research.md` §3b); `estimatedPrintTimeMinutes`/`estimatedFilamentWeightGrams` are `null`
whenever it isn't `"Available"` — the catalog UI shows a small "estimating…"/"unavailable" indicator instead
of the numbers in those cases, rather than blocking the row from appearing. `thumbnailBase64` is
`plateIndex: 0`'s image only, scoped to the current page (bounded by `pageSize`), not the full catalog.

### `GET /api/v1/parts/{id}`
`[Permission("parts:read")]`

**Response 200**:
```jsonc
{
  "id": "guid", "name": "Widget Bracket",
  "fileFormat": "GCode", "requiresSlicing": false,
  "originalFileName": "bracket.gcode", "fileSizeBytes": 482913,
  "nozzleDiameterMm": 0.4,
  "estimateStatus": "Available",
  "estimatedPrintTimeMinutes": 47, "estimatedFilamentWeightGrams": 18.4,
  "filamentRequirements": [
    { "id": "guid", "materialType": "PETG", "detectedColorHex": "#000000", "detectedSubBrand": null,
      "narrowFilamentVariantId": null, "narrowFilamentProfileId": null,
      "narrowFilamentVariantUnresolved": false, "narrowFilamentProfileUnresolved": false }
  ],
  "plateCount": 1,
  "thumbnails": [ { "plateIndex": 0, "thumbnailBase64": "iVBORw0KGgo..." } ],
  "tags": ["Brackets"]
}
```
`narrowFilamentVariantUnresolved`/`narrowFilamentProfileUnresolved` are `true` when that requirement's
narrow override was set but the referenced catalog row no longer exists (spec.md's soft-reference edge
case, `research.md` §7). Each `filamentRequirements[]` entry represents every plate/AMS-slot the file
detected sharing that exact `(materialType, detectedColorHex)` pair — **not** one entry per plate/slot. A
4-plate file that's uniformly one material+color still has exactly one entry; a 4-plate file with 3 distinct
pairs (e.g. two plates sharing black PETG, one plate white PLA, one plate red PETG) has exactly 3 entries,
regardless of which/how many plates share each pair. `detectedColorHex`/`detectedSubBrand` are best-effort
and purely informational/grouping data — never a matching criterion (matching runs on `materialType`, or the
narrow override if one is set) — and may be `null` if not parseable.

`estimateStatus` is `"Resolving"` | `"Available"` | `"Unavailable"` (`research.md` §3b) —
`estimatedPrintTimeMinutes`/`estimatedFilamentWeightGrams` are `null` whenever it isn't `"Available"`.
`"Resolving"` means the part was confirmed before its requires-slicing estimate finished (FR-009c/FR-009d);
`"Unavailable"` means that deferred slice subsequently failed — permanent, no retry in this phase. Neither
value blocks the part from existing or being fully usable otherwise; both are expected, valid states, not
error conditions to work around client-side.

`thumbnails[]` holds one entry per plate that successfully produced a thumbnail — `plateCount` (`>= 1`,
always `1` for an STL) tells the client how many plate slots exist in total, so a plate with no entry in
`thumbnails[]` renders as an explicit "no thumbnail for this plate" placeholder rather than looking like a
missing plate (FR-014a — best-effort per plate, never blocks saving). `thumbnails` MAY still be filling in
after the part is already returned by this endpoint, for a requires-slicing part confirmed early — the
client should treat a shorter-than-`plateCount` array as "some plates still resolving," not an error.

An STL added ("any material," FR-002a — the only format this can happen for) has exactly one
`filamentRequirements[]` entry with `materialType: null` (and no `detectedColorHex`/`detectedSubBrand` —
those only ever come from parsing an already-sliced file's real metadata):
```jsonc
{ "id": "guid", "materialType": null, "detectedColorHex": null, "detectedSubBrand": null,
  "narrowFilamentVariantId": null, "narrowFilamentProfileId": null,
  "narrowFilamentVariantUnresolved": false, "narrowFilamentProfileUnresolved": false }
```
This can never occur for a 3MF (sliced or unsliced) or a G-code file — their `materialType` is always a
real, non-null value; an unparseable one rejects the add/replace outright instead (FR-006).
**Response 404** — no such part.

### `GET /api/v1/parts/{id}/file`
`[Permission("parts:read")]`

**Response 200**: raw file bytes as originally uploaded, `Content-Disposition` using `OriginalFileName`.

### `PUT /api/v1/parts/{id}/file`
`[Permission("parts:replace-file")]` — same request shape as `POST /api/v1/parts` (`file` only — no
`name`/`tags` here either, since a replace never touches them). Starts processing against the existing
part; does **not** change anything on the `Part` yet.

**Response 202** (always). **Response 404** — no such part. **Response 400/422** — same validation as
`POST`.

### `POST /api/v1/parts/deletion-check`

Permission: `parts:delete`. Read-only preview; no activity event or catalog/history changes.
Request: `{ "partIds": ["uuid", "uuid"] }` containing 1–200 unique, non-empty IDs.
Invalid batches return `400`; unauthenticated/unauthorized requests return `401`/`403`.

**Response 200**: `{ "parts": [...] }`, one result per requested ID, in request order:

```json
{
  "partId": "uuid",
  "name": "Slab",
  "status": "Blocked",
  "blockers": [
    { "code": "AssemblyReference", "entityType": "Assembly", "entityId": "uuid", "name": "Slab Ass" }
  ],
  "legacyJobsLosingRecovery": 0
}
```

Statuses are `Eligible`, `Blocked`, and `Absent`. Missing and foreign-tenant IDs both return
`Absent`, null name, no blockers, and zero impact. Blockers identify all direct assembly
references (`AssemblyReference`), active/complete unlimited demand (`UnlimitedRun`), and
unfinished jobs without snapshots (`UnfinishedLegacyJob`). Entity types are `Assembly`,
`ProductionRun`, and `PrintJob`. `legacyJobsLosingRecovery` counts terminal jobs without
snapshots whose source actions would become permanently unavailable if deletion proceeds.
Verified snapshots do not block removal or count toward that impact.

The preview is advisory. DELETE re-evaluates this same policy under its part-row lock and
retains database FK safeguards. Preflight runs before any confirmation appears. If no
parts can be removed, show one dismissible Sonner toast per affected part, titled `Cannot remove <part name>`
with its reasons and dependency links underneath; no inline callout, modal, or footer is shown. Dependency blockers use warning severity (yellow); request and permission failures use error severity (red).
Detailed feedback stays visible until dismissed. Successful removal counts use a separate success toast.
If any parts can be removed, show the shared destructive confirmation for those parts only,
with a short list identifying parts that will remain. Keep applicable legacy-job warnings.

Blocked and failed parts remain selected. After partial removal or a request failure, close the
confirmation and report the outcome in a toast. A new removal attempt always performs a fresh check
before a separate confirmation; expose no pipeline status table, zero-count summary, check, or
retry controls. A missing part is reconciled as already absent, not counted as a confirmed
removal. Access failures stop the remaining batch. Removed and absent IDs leave selection and
detail caches; list refresh failures do not change deletion results. Preserve pending protection,
abort stale preflight requests, and restore focus to the initiating action or its fallback.

### `DELETE /api/v1/parts/{id}`
`[Permission("parts:delete")]`

**Response 204** — removes the catalog record and its own tag associations, filament requirements,
and thumbnails. Content-addressed file bytes are retained because other parts, processing jobs,
and pinned production revisions can share them; reference-aware garbage collection is deferred.
**Response 404** — no such part. **Response 409** — referenced by an assembly, an Active/Complete
unlimited run, or an unfinished unresolved legacy job. Errors name referencing assemblies or runs. Verified
finite snapshots and terminal unlimited history do not block removal.

Deletion conflicts add stable ProblemDetails extensions: `code: "PartDeletionBlocked"` with
`blockers` matching the preview shape; `PartConcurrencyConflict` requires another check;
`PartDependenciesChanged` covers the database reference backstop. Existing human-readable
ProblemDetails fields and HTTP status behavior remain compatible. A post-commit SignalR
notification failure is logged and does not change a successful database deletion into a failure.

### `POST /api/v1/parts/{id}/tags`
`[Permission("parts:tag")]`

**Request**: `{ "name": "Brackets" }` — get-or-creates the `Tag`. (For retagging an *existing* part outside
the add/replace flow — the initial add's tags are set via Confirm, below.)
**Response 200**: `{ "tags": ["Brackets", "..."] }`.

### `DELETE /api/v1/parts/{id}/tags/{tagId}`
`[Permission("parts:tag")]`

**Response 200**: `{ "tags": [...] }`. **Response 404** — part or tag association not found.

## Tags

### `GET /api/v1/tags`
`[Permission("tags:list")]`

**Response 200**: `{ "tags": [ { "id": "guid", "name": "Brackets" } ] }`.

## Part Processing Jobs (Processing → Ready for Review → Confirm/Discard, uniform for every file type)

### `GET /api/v1/part-processing-jobs/{id}`
`[Permission("parts:read")]`

**Response 200**:
```jsonc
{
  "id": "guid", "kind": "Add", "status": "ReadyForReview", "partId": null,
  "preview": {
    "nozzleDiameterMm": 0.4,
    "filamentRequirements": [
      { "requirementIndex": 0, "materialType": "PETG", "detectedColorHex": "#000000", "detectedSubBrand": null }
    ],
    "estimateStatus": "Resolving",
    "estimatedPrintTimeMinutes": null, "estimatedFilamentWeightGrams": null,
    "plateCount": 1,
    "thumbnails": []
  },
  "errorMessage": null
}
```
`status: "Processing"` → `preview: null`. `status: "Failed"` → `preview: null` (even if it had previously
been non-null while the job was briefly `ReadyForReview` — a failure that occurs after that point discards
the preview along with the rest of the job, `research.md` §3b), `errorMessage` populated. `status:
"Confirmed"` → `partId` set; `preview` still reflects whatever had been derived *as of confirm time* — any
further estimate/thumbnail resolution after that point updates the `Part` itself (`GET /parts/{id}`), not
this job's preview (see the SignalR section below). `status: "Discarded"` → terminal, nothing created.

`preview.nozzleDiameterMm`/`preview.filamentRequirements` are always present once `status` first becomes
`"ReadyForReview"` — that transition no longer waits on the estimate (`research.md` §3b). A multi-color
and/or multi-plate already-sliced file's `filamentRequirements[]` includes one entry per **distinct detected
`(materialType, detectedColorHex)` pair** across every plate/slot in the file — not one per plate/slot —
exactly like the eventual `Part`'s own shape (`research.md` §7). `requirementIndex` is a stable 0-based key
(first-seen order) referenced by the `Confirm` request below.

`preview.estimateStatus` is `"Resolving"` | `"Available"` (never `"Unavailable"` at the job level — a
deferred failure here instead flips the whole job to `status: "Failed"` if unconfirmed, or targets the
`Part`'s own `estimateStatus` if already confirmed, `research.md` §3b). `estimatedPrintTimeMinutes`/
`estimatedFilamentWeightGrams` are `null` while `estimateStatus = "Resolving"`. For an already-sliced file,
`estimateStatus` is `"Available"` from the moment `status` first becomes `"ReadyForReview"` (everything
parses together); for a requires-slicing file it typically starts `"Resolving"` and flips to `"Available"`
moments-to-minutes later as `ISlicerEngine` finishes. `preview.thumbnails[]`/`preview.plateCount` follow the
identical per-plate shape as `GET /parts/{id}`'s `thumbnails`/`plateCount` fields, and fill in on the same
schedule as the estimate.
**Response 404** — no such job.

Primary UX is the SignalR push below, which carries this identical `preview` shape directly — this endpoint
exists for the initial fetch immediately after receiving a `202` (in case the job resolves before the
frontend's subscription is even established) and as a reconnect-safe fallback, not for polling.

### `POST /api/v1/part-processing-jobs/{id}/confirm`
`[Permission("parts:create")]` for an `Add`-kind job, `[Permission("parts:replace-file")]` for a
`Replace`-kind job. This is the **Save** action — the point at which the operator's concurrently-edited
name/tags are finally submitted, together with any narrow filament overrides.

**Request** (`Add`):
```jsonc
{
  "name": "Widget Bracket",                       // required for Add
  "tags": ["Brackets"],                            // optional, get-or-create by name
  "filamentRequirementOverrides": [                // optional
    { "requirementIndex": 0, "narrowFilamentVariantId": "guid" }
  ]
}
```
**Request** (`Replace`): same shape minus `name`/`tags` (a replace never touches them — `400` if supplied).
`filamentRequirementOverrides[].requirementIndex` must match one of the job's preview filament requirements;
at most one of `narrowFilamentVariantId`/`narrowFilamentProfileId` per entry. An override applies to the
**whole** grouped requirement — i.e. every plate/slot the file detected sharing that requirement's material
+ color pair — not to one plate individually; there is no finer-grained per-plate override beneath that.

**Response 201** (`Add`) / **200** (`Replace`): the full `Part` shape, same as `GET /api/v1/parts/{id}`.
**Response 400** — `Add` missing `name`; `Replace` with `name`/`tags` present; an override entry with both
narrow-override fields set, or with a `requirementIndex` not matching a preview requirement.
**Response 409** — job is not currently `ReadyForReview` (already confirmed/discarded, or still processing).
**Response 404** — no such job, or a supplied narrow-override reference doesn't resolve.

### `DELETE /api/v1/part-processing-jobs/{id}/confirm` (discard / Cancel)
Same permission as the confirm endpoint above.

**Response 204** — job marked `Discarded`; nothing saved (for a `Replace`, the existing part is untouched).
**Response 409** — job is not currently `ReadyForReview`.

Discard is also the correct action for an operator-initiated "Cancel" while a job is still `Processing` —
in that case the request instead cancels the in-flight background work (best-effort; a slicer CLI process
already running is given a chance to terminate) and moves the job straight to `Discarded`.

## Printer ↔ Machine Profile link

### `PUT /api/v1/printers/{id}/machine-profile`
`[Permission("printers:link-machine-profile")]`

**Request**: `{ "machineProfileId": "guid" }` or `{ "machineProfileId": null }` to unlink.
**Response 200**: `{ "printerId": "guid", "machineProfileId": "guid" | null }`.
**Response 404** — printer or `machineProfileId` doesn't resolve.

## Slicing Defaults (Organization Setting)

### `GET /api/v1/settings/slicing-defaults`
`[Permission("settings:slicing-defaults:read")]`

**Response 200**: `{ "defaultSlicerEngine": "OrcaSlicer" }`

### `PUT /api/v1/settings/slicing-defaults`
`[Permission("settings:slicing-defaults:update")]`

**Request**: `{ "defaultSlicerEngine": "OrcaSlicer" | "BambuStudio" }`
**Response 200**: `{ "defaultSlicerEngine": "..." }`

## SignalR (`/hubs/farm`, existing hub — new events only)

- **`PartProcessingJobStatusChanged`** — `{ jobId, status, partId?, errorMessage?, preview? }`. Pushed on
  every `PartProcessingJob` status transition, for every file type uniformly. `preview` (same shape as
  `GET .../part-processing-jobs/{id}`'s `preview` field, including `thumbnails[]`) is included on the
  `Processing → ReadyForReview` transition, carrying nozzle diameter/material/filament requirements
  immediately (`estimateStatus` may still be `"Resolving"` at this point, `research.md` §3b) — and again,
  **while the job is still `ReadyForReview` and unconfirmed**, on the follow-up moment its
  `estimateStatus`/estimate fields/`thumbnails[]` update from the trailing continuation, so an open review
  panel refreshes live without a second `GET`. The terminal `ReadyForReview → Confirmed | Discarded` /
  `Processing → Failed` / `ReadyForReview → Failed` transitions omit `preview` (nothing new to show beyond
  `status`/`errorMessage`).
- **`PartUpserted`** — `{ partId }`. Pushed when a `PartProcessingJob` is `Confirmed`, so any open
  catalog/detail view invalidates without polling.
- **`PartEstimateResolved`** — `{ partId }` (new, `research.md` §3b). Pushed when the trailing background
  continuation successfully populates a `Part`'s estimate/thumbnails **after** it was already confirmed with
  `estimateStatus: "Resolving"` — i.e. only for a requires-slicing part saved early (FR-009c/FR-009d). An
  open catalog/detail view re-fetches (or invalidates) the same way it would for `PartUpserted`; kept as a
  separate event name rather than reusing `PartUpserted` so the frontend can, if it chooses, show a subtle
  "estimate ready" transition rather than treating it as a generic refresh.
- **`PartEstimateUnavailable`** — `{ partId }` (new, `research.md` §3b). Pushed when that same continuation
  instead fails — the `Part` itself is untouched otherwise, only `estimateStatus` flips to `"Unavailable"`
  (permanent, FR-009d). Distinct from `PartEstimateResolved` so the frontend can render the two outcomes
  differently without inspecting `GET /parts/{id}` first.
- **`PartDeleted`** — `{ partId }`.

All five follow the exact `IHubContext<FarmHub>` + `FarmHub.GroupName(organizationId)` +
`Application`-defined-publisher-interface (`IPartEventPublisher`) shape `FarmHubPrinterEventPublisher`
already establishes for printer events — no hub-level changes.

### Production snapshot deletion amendment (October 13, 2026)

Verified finite jobs do not block deletion. Active or Complete unlimited runs and unfinished unresolved legacy
jobs do; dependency errors name the referencing runs. Cancelled or Closed unlimited history permits
removal once retained jobs have verified snapshots or are terminal. Historical IDs/names and stored bytes remain.
Creation/deletion races are serialized by source-row locks and restrictive library foreign keys.

### Finished legacy jobs

Cancelled, Failed and Completed jobs with missing snapshots permit source part removal. Deletion
preserves history and marks missing snapshots permanently Unavailable: retry and download return 409.
Valid snapshots remain independent. Unfinished unresolved jobs, assemblies and eligible unlimited
runs still protect their source. Cancelling one unlimited job does not cancel its owning run.

## List sorting

Applies to `GET /api/v1/parts`.

Optional `sortBy=name` and `sortDirection=asc|desc` order the entire filtered
collection before pagination. Omitting sorting retains newest-first ordering;
`sortBy` alone means ascending. All orders use ascending entity ID as a stable
tie-breaker; name sorting is case-insensitive and lexical. Unknown fields,
invalid directions, and a direction without a field return `400`. Existing
page/pageSize limits and response shapes are unchanged.
