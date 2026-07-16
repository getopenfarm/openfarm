# Contract: Profiles REPR endpoints (`OpenFarm.Api`)

REPR pattern (Request DTO → Handler → Response DTO), one FastEndpoints class per endpoint, per the
constitution. All errors surface as RFC 7807 `ProblemDetails` via the existing global exception-handling
middleware — no endpoint-specific error shapes. All endpoints require an authenticated JWT with an
`organization_id` claim (Finbuckle-scoped). Per spec.md's Assumptions, no specific `permissions` catalog
entry is enforced yet (Phase 0's placeholder "authenticated-only" policy shim) — endpoints still carry
`[Permission("profiles:...")]` attributes so the string values are already correct once Phase 4 seeds the
real catalog, matching the precedent set in `specs/002-printer-integration/contracts/printers-api.md`.

## `POST /api/v1/profiles/imports/preview`

Upload a slicer bundle and get back everything it contains, without persisting anything (FR-001,
FR-002, FR-003). `multipart/form-data`, single field `file` (`research.md` §6).

**Request**: `multipart/form-data; file=<bundle>` — one of `.zip`, `.orca_printer`, `.orca_filament`,
`.bbscfg`, `.bbsflmt` (FR-001).

**Response 200**
```jsonc
{
  "sourceFileName": "MyProfiles.zip",
  "machinePresets": [
    {
      "name": "Bambu Lab X1 Carbon 0.4 nozzle",
      "sourceFormat": "OrcaSlicer",
      "nozzleSizeMm": 0.4,
      "bedSizeXMm": 256, "bedSizeYMm": 256, "maxHeightMm": 256,
      "hasEnclosure": true,
      "supportedMaterials": ["PLA", "PETG", "ABS", "TPU"],
      "supportsLiveIntegration": true,
      "baseProfileName": null,
      "rawDataJson": "{ ... }",
      "isDuplicate": false
    }
  ],
  "filamentPresets": [
    {
      "name": "Bambu PLA Basic – Red",
      "sourceFormat": "OrcaSlicer",
      "material": "PLA", "brand": "Bambu", "colorHex": "C12E1FFF",
      "nozzleSizeMm": 0.4, "nozzleTempC": 220, "bedTempC": 55,
      "maxVolumetricSpeedMm3s": 15, "fanPercent": 100,
      "baseProfileName": "Bambu PLA Basic",
      "rawDataJson": "{ ... }",
      "isDuplicate": false
    }
  ]
}
```
Both arrays may be empty (FR-008 — successfully-opened bundle with nothing recognizable inside); this
is a `200`, not an error — the frontend renders an explicit "nothing importable was found" state rather
than treating an empty result as failure (`quickstart.md` Scenario 4).

**Response 422** (FR-007) — `ProblemDetails`, unrecognized file extension or an archive that fails to
open/parse:
```jsonc
{ "type": "...", "title": "Unsupported or corrupted profile bundle", "status": 422,
  "detail": "MyFile.zip could not be read as an OrcaSlicer or Bambu Studio profile bundle." }
```

## `POST /api/v1/profiles/imports/confirm`

Persist exactly the presets the operator selected (FR-004). Stateless relative to the preview call —
the client echoes back the selected preview items verbatim (`research.md` §4); nothing is looked up by
a batch id because no batch id exists.

**Request**
```jsonc
{
  "sourceFileName": "MyProfiles.zip",
  "machinePresets": [
    {
      "name": "Bambu Lab X1 Carbon 0.4 nozzle", "sourceFormat": "OrcaSlicer",
      "nozzleSizeMm": 0.4, "bedSizeXMm": 256, "bedSizeYMm": 256, "maxHeightMm": 256,
      "hasEnclosure": true, "supportedMaterials": ["PLA", "PETG", "ABS", "TPU"],
      "supportsLiveIntegration": true, "baseProfileName": null, "rawDataJson": "{ ... }",
      "duplicateAction": null
    }
  ],
  "filamentPresets": [
    {
      "name": "Bambu PLA Basic – Red", "sourceFormat": "OrcaSlicer",
      "material": "PLA", "brand": "Bambu", "colorHex": "C12E1FFF",
      "nozzleSizeMm": 0.4, "nozzleTempC": 220, "bedTempC": 55,
      "maxVolumetricSpeedMm3s": 15, "fanPercent": 100,
      "baseProfileName": "Bambu PLA Basic", "rawDataJson": "{ ... }",
      "duplicateAction": "ImportAsNew"
    }
  ]
}
```
`duplicateAction` is required (`"Skip"` or `"ImportAsNew"`) for any item whose preview response had
`isDuplicate: true`; null/omitted otherwise. An item with `duplicateAction: "Skip"` is not created.

**Response 201**
```jsonc
{
  "createdMachineProfiles": [ { "id": "guid", "name": "Bambu Lab X1 Carbon 0.4 nozzle" } ],
  "createdFilamentProfiles": [ { "id": "guid", "name": "Bambu PLA Basic – Red", "baseProfileId": "guid-of-base-if-resolved" } ]
}
```
Every created id in both arrays is what the extended `ActivityLogPostProcessor` (`research.md` §7)
iterates to write one `ActivityLogEntry` per profile, `EventType: "ProfileImported"`.

## `GET /api/v1/profiles/machine`

List the machine profile catalog (FR-010, US3).

**Response 200**
```jsonc
{ "profiles": [ { "id": "guid", "name": "...", "sourceFormat": "OrcaSlicer", "nozzleSizeMm": 0.4, "hasEnclosure": true, "supportsLiveIntegration": true, "baseProfileId": null, "baseProfileName": null } ] }
```

## `GET /api/v1/profiles/machine/{id}`

Single machine profile's full raw data (FR-011).

**Response 200**
```jsonc
{ "id": "guid", "name": "...", "sourceFormat": "OrcaSlicer", "rawDataJson": "{ ... }", "baseProfileId": null, "baseProfileName": null, "createdAtUtc": "..." }
```

**Response 404** — `ProblemDetails`, unknown id (or id belongs to another org — Finbuckle's global
query filter makes this indistinguishable from "doesn't exist," same as every other tenant-scoped
lookup in this codebase).

## `PUT /api/v1/profiles/machine/{id}/raw-data`

Save an edited raw JSON body (FR-012/013).

**Request**
```jsonc
{ "rawDataJson": "{ \"nozzle_diameter\": [\"0.6\"], ... }" }
```

**Response 200** — same shape as `GET /api/v1/profiles/machine/{id}`, reflecting the saved value
(and any display fields the handler could re-derive from it — best-effort, `data-model.md`).

**Response 422** (FR-013) — malformed JSON; the previously saved row is left unchanged.
```jsonc
{ "type": "...", "title": "Invalid profile JSON", "status": 422, "detail": "The submitted rawDataJson is not valid JSON: ..." }
```

## `GET /api/v1/profiles/filament`, `GET /api/v1/profiles/filament/{id}`, `PUT /api/v1/profiles/filament/{id}/raw-data`

Mirror the three machine-profile endpoints above exactly, scoped to `FilamentProfile` and its own field
set (`data-model.md`) — material/brand/colorHex/nozzleTempC/bedTempC/maxVolumetricSpeedMm3s/fanPercent
instead of nozzleSizeMm/bedSize/enclosure/supportedMaterials/supportsLiveIntegration.
