# Contract: Part Library REPR endpoints (`OpenFarm.Api`)

Supersedes the previous version of this file in full. Every add/replace, regardless of file type, now goes
through one uniform pipeline: `PartProcessingJob` (renamed from `PartSlicingJob`), with `Status: Processing
| ReadyForReview | Confirmed | Failed | Discarded` for every file format — there is no more synchronous
`201`-on-submit branch for an already-sliced file. Attaching the file is the only thing needed to start
processing — a Filament is **always optional**, even when the file needs slicing: omitting it means "any
material" (FR-002a), not a rejection. **`name`/`tags` are not part of the initial request at all** — they
(plus any narrow filament overrides) are submitted together at the Confirm step, so an operator can keep
editing them in the same dialog while processing runs in the background. REPR pattern, one FastEndpoints
class per endpoint, `[Permission("...")]` per Phase 0's placeholder-policy precedent, all errors as RFC 7807
`ProblemDetails`. See `data-model.md` for entity shapes and `research.md` for the reasoning.

**Thumbnails travel as inline base64, never as a bare byte-stream URL.** This app authenticates via JWT
Bearer tokens only (no cookies), so a plain `<img src="...">` pointed at an authenticated endpoint cannot
attach the required `Authorization` header — the browser would request it unauthenticated and get a `401`.
Every response that includes a part or a job's preview therefore embeds the thumbnail as
`thumbnailBase64: string | null` (a base64-encoded PNG, renderable directly as
`data:image/png;base64,${thumbnailBase64}`), exactly mirroring the one existing precedent for this in the
codebase — `features/printers/components/CameraView.tsx` already renders a live camera frame the same way.
There is no separate `GET .../thumbnail` byte-stream endpoint (an earlier revision of this contract had
one, and it would not have actually been renderable as designed). This does inline a small amount of
payload into paginated `GET /parts` responses, but that's bounded by the endpoint's own `pageSize` — never
by the full 500+-part catalog SC-005 talks about filtering — so it stays small in practice.

## Parts

### `POST /api/v1/parts`
`[Permission("parts:create")]` — multipart/form-data (`AllowFileUploads()`). Starts processing; does **not**
create a `Part`.

**Request** (multipart fields):
```text
file: <binary>       # required — STL, 3MF, or G-code
filamentId: guid?    # always optional, including when the uploaded file requires slicing — the in-app
                      # slicing target only; not stored on the eventual Part directly (see data-model.md's
                      # PartFilamentRequirement note). Ignored/unused for an already-sliced file. Omitted for
                      # a requires-slicing file means "any material" (FR-002a) — the system slices against a
                      # fixed internal fallback material and records the resulting requirement as fully
                      # unconstrained (materialType: null in the eventual Part/preview), not a rejection.
```
No `name`, `tags`, or narrow filament overrides here — see Confirm below.

**Response 202** (always, for every file format):
```jsonc
{ "jobId": "guid", "status": "Processing" }
```

**Response 400** — unrecognized file format.
**Response 404** — `filamentId` was supplied but doesn't resolve.
**Response 422** — file requires slicing but no registered printer has a `MachineProfile` linked yet
(`NozzleDiameterResolver` returned nothing — FR-006a). This check applies regardless of whether
`filamentId` was supplied — "any material" still needs a nozzle-diameter target to slice against.

Both classification (`IPartFileClassifier`) and the FR-006a nozzle-diameter check run synchronously, before
a `PartProcessingJob` row is even created — a request that's going to be rejected never enters the async
pipeline at all.

### `GET /api/v1/parts`
`[Permission("parts:list")]`

Query params: `tag` (optional, repeatable — filters to parts carrying **all** given tags), `page`,
`pageSize`.

**Response 200**:
```jsonc
{
  "parts": [
    { "id", "name", "fileFormat", "requiresSlicing", "nozzleDiameterMm",
      "estimatedPrintTimeMinutes", "estimatedFilamentWeightGrams", "thumbnailBase64": "iVBORw0KGgo..." | null,
      "materialTypes": ["PETG"], "tags": [...] }
  ],
  "totalCount": 512
}
```
`materialTypes` is the distinct set of non-null `filamentRequirements[].materialType` values, for compact
catalog display — the full per-slot breakdown is only on the detail endpoint. An "any material" part
(FR-002a) has `materialTypes: []` (empty, not `[null]`) — the catalog UI shows an "Any material" badge for
that case instead of a material-type chip. `thumbnailBase64` is scoped to the
current page (bounded by `pageSize`), not the full catalog.

### `GET /api/v1/parts/{id}`
`[Permission("parts:read")]`

**Response 200**:
```jsonc
{
  "id": "guid", "name": "Widget Bracket",
  "fileFormat": "GCode", "requiresSlicing": false,
  "originalFileName": "bracket.gcode", "fileSizeBytes": 482913,
  "nozzleDiameterMm": 0.4,
  "estimatedPrintTimeMinutes": 47, "estimatedFilamentWeightGrams": 18.4,
  "filamentRequirements": [
    { "id": "guid", "materialType": "PETG", "detectedColorHex": "#000000", "detectedSubBrand": null,
      "narrowFilamentVariantId": null, "narrowFilamentProfileId": null,
      "narrowFilamentVariantUnresolved": false, "narrowFilamentProfileUnresolved": false }
  ],
  "thumbnailBase64": "iVBORw0KGgo...",
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
narrow override if one is set) — and may be `null` if not parseable. `thumbnailBase64` is `null` when no
thumbnail was derived (FR-014a — a valid, non-error state, not a broken part).

An STL/unsliced-3MF added with no `filamentId` ("any material," FR-002a) has exactly one
`filamentRequirements[]` entry with `materialType: null` (and no `detectedColorHex`/`detectedSubBrand` —
those only ever come from parsing an already-sliced file's real metadata):
```jsonc
{ "id": "guid", "materialType": null, "detectedColorHex": null, "detectedSubBrand": null,
  "narrowFilamentVariantId": null, "narrowFilamentProfileId": null,
  "narrowFilamentVariantUnresolved": false, "narrowFilamentProfileUnresolved": false }
```
This can never occur for an already-sliced file — its `materialType` is always a real, non-null value
(FR-006c rejects an unparseable one outright).
**Response 404** — no such part.

### `GET /api/v1/parts/{id}/file`
`[Permission("parts:read")]`

**Response 200**: raw file bytes as originally uploaded, `Content-Disposition` using `OriginalFileName`.

### `PUT /api/v1/parts/{id}/file`
`[Permission("parts:replace-file")]` — same request shape as `POST /api/v1/parts` (`file`, `filamentId?` —
no `name`/`tags` here either, since a replace never touches them). Starts processing against the existing
part; does **not** change anything on the `Part` yet.

**Response 202** (always). **Response 404** — no such part. **Response 400/422** — same validation as
`POST`.

### `DELETE /api/v1/parts/{id}`
`[Permission("parts:delete")]`

**Response 204**. **Response 404** — no such part.

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
    "estimatedPrintTimeMinutes": 47, "estimatedFilamentWeightGrams": 18.4,
    "thumbnailBase64": "iVBORw0KGgo...",
    "filamentRequirements": [
      { "requirementIndex": 0, "materialType": "PETG", "detectedColorHex": "#000000", "detectedSubBrand": null }
    ]
  },
  "errorMessage": null
}
```
`status: "Processing"` → `preview: null`. `status: "Failed"` → `preview: null`, `errorMessage` populated.
`status: "Confirmed"` → `partId` set. `status: "Discarded"` → terminal, nothing created. A multi-color
and/or multi-plate already-sliced file's preview includes one `filamentRequirements[]` entry per **distinct
detected `(materialType, detectedColorHex)` pair** across every plate/slot in the file — not one per
plate/slot — exactly like the eventual `Part`'s own shape (`research.md` §7). `requirementIndex` is a
stable 0-based key (first-seen order) referenced by the `Confirm` request below. `preview.thumbnailBase64`
is `null` if no thumbnail was derived.
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
  `GET .../part-processing-jobs/{id}`'s `preview` field, including `thumbnailBase64`) is included **only**
  on the `Processing → ReadyForReview` transition — carrying the full derived result directly in the push
  means the frontend's review panel can render immediately, with no follow-up `GET` needed in the live
  (subscribed) case. The terminal `ReadyForReview → Confirmed | Discarded` / `Processing → Failed`
  transitions omit `preview` (nothing new to show beyond `status`/`errorMessage`).
- **`PartUpserted`** — `{ partId }`. Pushed when a `PartProcessingJob` is `Confirmed`, so any open
  catalog/detail view invalidates without polling.
- **`PartDeleted`** — `{ partId }`.

All three follow the exact `IHubContext<FarmHub>` + `FarmHub.GroupName(organizationId)` +
`Application`-defined-publisher-interface (`IPartEventPublisher`) shape `FarmHubPrinterEventPublisher`
already establishes for printer events — no hub-level changes.
