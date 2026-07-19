# Contract: Part Library REPR endpoints (`OpenFarm.Api`)

Supersedes the previous version of this file in full. Every add/replace, regardless of file type, now goes
through one uniform pipeline: `PartProcessingJob` (renamed from `PartSlicingJob`), with `Status: Processing
| ReadyForReview | Confirmed | Failed | Discarded` for every file format — there is no more synchronous
`201`-on-submit branch for an already-sliced file. Attaching the file (plus a Filament, when the file needs
slicing) is the only thing needed to start processing; **`name`/`tags` are not part of the initial
request at all** — they (plus any narrow filament overrides) are submitted together at the Confirm step,
so an operator can keep editing them in the same dialog while processing runs in the background. REPR
pattern, one FastEndpoints class per endpoint, `[Permission("...")]` per Phase 0's placeholder-policy
precedent, all errors as RFC 7807 `ProblemDetails`. See `data-model.md` for entity shapes and `research.md`
for the reasoning.

## Parts

### `POST /api/v1/parts`
`[Permission("parts:create")]` — multipart/form-data (`AllowFileUploads()`). Starts processing; does **not**
create a `Part`.

**Request** (multipart fields):
```text
file: <binary>       # required — STL, 3MF, or G-code
filamentId: guid?    # required iff the uploaded file requires slicing — the in-app slicing target only;
                      # not stored on the eventual Part directly (see data-model.md's PartFilamentRequirement
                      # note). Ignored/unused for an already-sliced file.
```
No `name`, `tags`, or narrow filament overrides here — see Confirm below.

**Response 202** (always, for every file format):
```jsonc
{ "jobId": "guid", "status": "Processing" }
```

**Response 400** — unrecognized file format; `filamentId` missing while the file requires slicing.
**Response 404** — `filamentId` doesn't resolve.
**Response 422** — file requires slicing but no registered printer has a `MachineProfile` linked yet
(`NozzleDiameterResolver` returned nothing — FR-006b).

Both classification (`IPartFileClassifier`) and the FR-006b nozzle-diameter check run synchronously, before
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
      "estimatedPrintTimeMinutes", "estimatedFilamentWeightGrams", "thumbnailAvailable",
      "materialTypes": ["PETG"], "tags": [...] }
  ],
  "totalCount": 512
}
```
`materialTypes` is the distinct set of `filamentRequirements[].materialType` values, for compact catalog
display — the full per-slot breakdown is only on the detail endpoint.

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
    { "id": "guid", "slotIndex": null, "materialType": "PETG",
      "narrowFilamentVariantId": null, "narrowFilamentProfileId": null,
      "narrowFilamentVariantUnresolved": false, "narrowFilamentProfileUnresolved": false }
  ],
  "thumbnailAvailable": true,
  "tags": ["Brackets"]
}
```
`narrowFilamentVariantUnresolved`/`narrowFilamentProfileUnresolved` are `true` when that requirement's
narrow override was set but the referenced catalog row no longer exists (spec.md's soft-reference edge
case, `research.md` §7). A multi-color part has multiple `filamentRequirements[]` entries (`slotIndex: 0,
1, 2, ...`).
**Response 404** — no such part.

### `GET /api/v1/parts/{id}/thumbnail`
`[Permission("parts:read")]`

**Response 200**: raw PNG bytes, `Content-Type: image/png` (always — normalized at ingestion, `research.md`
§8).
**Response 404** — part has no thumbnail (FR-014a — a valid, non-error state).

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
    "thumbnailAvailable": true,
    "filamentRequirements": [ { "slotIndex": null, "materialType": "PETG" } ]
  },
  "errorMessage": null
}
```
`status: "Processing"` → `preview: null`. `status: "Failed"` → `preview: null`, `errorMessage` populated.
`status: "Confirmed"` → `partId` set. `status: "Discarded"` → terminal, nothing created. A multi-color
already-sliced file's preview includes multiple `filamentRequirements[]` entries, exactly like the eventual
`Part`'s own shape.
**Response 404** — no such job.

Primary UX is the SignalR push below; this endpoint exists for the initial fetch after receiving a `202`
and as a reconnect-safe fallback, not for polling.

### `GET /api/v1/part-processing-jobs/{id}/thumbnail`
`[Permission("parts:read")]`

**Response 200**: raw PNG bytes of the preview thumbnail (available once `Status = ReadyForReview`).
**Response 404** — job not yet `ReadyForReview`, or no preview thumbnail was derived.

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
    { "slotIndex": null, "narrowFilamentVariantId": "guid" }
  ]
}
```
**Request** (`Replace`): same shape minus `name`/`tags` (a replace never touches them — `400` if supplied).
`filamentRequirementOverrides[].slotIndex` must match one of the job's preview filament requirements; at
most one of `narrowFilamentVariantId`/`narrowFilamentProfileId` per entry.

**Response 201** (`Add`) / **200** (`Replace`): the full `Part` shape, same as `GET /api/v1/parts/{id}`.
**Response 400** — `Add` missing `name`; `Replace` with `name`/`tags` present; an override entry with both
narrow-override fields set, or with a `slotIndex` not matching a preview requirement.
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

- **`PartProcessingJobStatusChanged`** — `{ jobId, status, partId?, errorMessage? }`. Pushed on every
  `PartProcessingJob` status transition, for every file type uniformly: `Processing → ReadyForReview` (so
  the frontend's Save button can enable the moment it's ready) and the terminal
  `ReadyForReview → Confirmed | Discarded` / `Processing → Failed` transitions.
- **`PartUpserted`** — `{ partId }`. Pushed when a `PartProcessingJob` is `Confirmed`, so any open
  catalog/detail view invalidates without polling.
- **`PartDeleted`** — `{ partId }`.

All three follow the exact `IHubContext<FarmHub>` + `FarmHub.GroupName(organizationId)` +
`Application`-defined-publisher-interface (`IPartEventPublisher`) shape `FarmHubPrinterEventPublisher`
already establishes for printer events — no hub-level changes.
