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
      "baseProfileName": null,
      "hasUnresolvedBaseInChain": false,
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
      "hasUnresolvedBaseInChain": false,
      "compatiblePrinterNames": ["Bambu Lab X1 Carbon 0.4 nozzle"],
      "isDuplicate": false
    }
  ]
}
```
Both arrays may be empty (FR-008 — successfully-opened bundle with nothing recognizable inside); this
is a `200`, not an error — the frontend renders an explicit "nothing importable was found" state rather
than treating an empty result as failure (`quickstart.md` Scenario 4).

No `rawDataJson` and no `supportsLiveIntegration` in this response: `rawDataJson` has no consumer here
— the review-list UI only needs name/type/base-name/duplicate-flag plus the derived display fields
(FR-003), and confirm re-parses the re-uploaded file itself rather than reading anything back from this
response (`research.md` §4) — omitting it also keeps the payload small for spec.md's Edge Cases concern
about "very large bundle[s] (many dozens of presets)" staying usable. `supportsLiveIntegration` doesn't
exist anywhere in this feature — cut entirely, not just from preview (`research.md` §8).

Every preset's derived display fields (`nozzleSizeMm`, `hasEnclosure`, `nozzleTempC`, etc.) are computed
by resolving and merging its base chain — matched by `(sourceFormat, name)` against sibling presets in
this same bundle, then against previously-imported catalog rows — not from that one preset's own content
alone, since real slicer exports carry only the settings a preset overrides (`research.md` §10).
`hasUnresolvedBaseInChain: true` means the chain was cut short by a declared base that never resolved
(at any level, not just this preset's own immediate one) — its derived fields may be missing whatever
that unresolved ancestor would have supplied. In the example above, the "Bambu PLA Basic – Red" override
shows complete derived fields because "Bambu PLA Basic" is presumably also in this bundle or already
imported; if it weren't, the same preset would show `"hasUnresolvedBaseInChain": true` and likely
missing `nozzleTempC`/`bedTempC`/etc.

`compatiblePrinterNames` (filament presets only, FR-016, `research.md` §11): the preset's declared
compatible-printer name(s), shown as plain text — unlike `baseProfileName`, this is **not** resolved
against the machine-profile catalog in this feature (considered and deliberately cut, not deferred by
omission — see `research.md` §11). The frontend can still tell the operator a declared name matches a
printer preset *in this same bundle*, entirely client-side, by cross-referencing `compatiblePrinterNames`
against this same response's `machinePresets[].name` — the same client-side-only pattern FR-006's "base
present but not selected" warning already uses.

**Response 422** (FR-007) — `ProblemDetails`, unrecognized file extension or an archive that fails to
open/parse:
```jsonc
{ "type": "...", "title": "Unsupported or corrupted profile bundle", "status": 422,
  "detail": "MyFile.zip could not be read as an OrcaSlicer or Bambu Studio profile bundle." }
```

## `POST /api/v1/profiles/imports/confirm`

Persist exactly the presets the operator selected (FR-004). Stateless relative to the preview call —
**re-uploads the same file** and re-parses it server-side rather than trusting client-echoed preset
content (`research.md` §4's revision note); nothing is looked up by a batch id because no batch id
exists, and no preset field crosses the wire except the operator's selection and duplicate-resolution
decisions.

**Request**: `multipart/form-data`
- `file` — the same bundle uploaded to `preview`.
- `selections` — a JSON-encoded string field (FastEndpoints binds the file part natively; the
  selection list travels as a plain string form field, `research.md` §6):
```jsonc
[
  { "type": "Machine", "name": "Bambu Lab X1 Carbon 0.4 nozzle", "duplicateAction": null },
  { "type": "Filament", "name": "Bambu PLA Basic – Red", "duplicateAction": "ImportAsNew" }
]
```
`duplicateAction` is required (`"Skip"` or `"ImportAsNew"`) for any key whose preview response had
`isDuplicate: true`; null/omitted otherwise. A key with `duplicateAction: "Skip"` is not created.

**Response 201**
```jsonc
{
  "createdMachineProfiles": [ { "id": "guid", "name": "Bambu Lab X1 Carbon 0.4 nozzle – Direct Drive", "baseProfileId": "guid-of-base-if-resolved" } ],
  "createdFilamentProfiles": [ { "id": "guid", "name": "Bambu PLA Basic – Red", "baseProfileId": "guid-of-base-if-resolved" } ]
}
```
Both arrays share the identical `{id, name, baseProfileId}` shape — either profile type can be an
override (FR-005/US2 Acceptance Scenario 1 is specifically a *machine*-profile base/override example),
so there's no reason for the two arrays to differ. `baseProfileId` is `null` when the preset declared
no base, or when its declared base didn't resolve — including the ambiguous-match case where more than
one existing/in-batch row shares the declared base's name (`research.md` §3).

Every created id in both arrays is what the extended `ActivityLogPostProcessor` (`research.md` §7)
iterates to write one `ActivityLogEntry` per profile, `EventType: "ProfileImported"`.

**Response 422** — a `selections` key's `(type, name)` doesn't match anything in the freshly re-parsed
`file` (`research.md` §4's consistency check — most often means the operator uploaded a different file
than the one they previewed):
```jsonc
{ "type": "...", "title": "Selection does not match the uploaded bundle", "status": 422,
  "detail": "No Filament preset named 'Bambu PLA Basic – Red' was found in the re-parsed bundle. Re-run preview and try again." }
```

## `GET /api/v1/profiles/machine`

List the machine profile catalog (FR-010, US3). `nozzleSizeMm`/`hasEnclosure`/etc. below are **derived
display fields** (`data-model.md`) — computed by the query handler by walking and merging each row's
resolved `BaseProfileId` ancestor chain on every request (`research.md` §10), not read from a stored
column, and not from that row's own `rawDataJson` alone (real exports are delta-only). (No
`supportsLiveIntegration` field — cut entirely for this phase, `research.md` §8.)

**Response 200**
```jsonc
{ "profiles": [ { "id": "guid", "name": "...", "sourceFormat": "OrcaSlicer", "nozzleSizeMm": 0.4, "hasEnclosure": true, "hasUnresolvedBaseInChain": false, "baseProfileId": null, "baseProfileName": null } ] }
```

## `GET /api/v1/profiles/machine/{id}`

Single machine profile's full raw data (FR-011) — this row's own `rawDataJson` only, never a merged
blob — plus the same chain-merged derived display fields as the list endpoint (shown once here, computed
the same way).

**Response 200**
```jsonc
{ "id": "guid", "name": "...", "sourceFormat": "OrcaSlicer", "nozzleSizeMm": 0.4, "hasEnclosure": true, "hasUnresolvedBaseInChain": false, "rawDataJson": "{ ... }", "baseProfileId": null, "baseProfileName": null, "createdAtUtc": "..." }
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

**Response 200** — same shape as `GET /api/v1/profiles/machine/{id}`, reflecting the saved
`rawDataJson` and every derived display field recomputed from the chain merge including it
(`data-model.md`'s "Derived display fields" — these are never cached, so the response is always current
by construction, not best-effort). Editing a profile that other profiles inherit *from* also changes
those descendants' derived fields on their very next read — nothing needs to be re-saved or
re-triggered on the descendant side (`research.md` §10).

**Response 422** (FR-013) — malformed JSON; the previously saved row is left unchanged.
```jsonc
{ "type": "...", "title": "Invalid profile JSON", "status": 422, "detail": "The submitted rawDataJson is not valid JSON: ..." }
```

## `GET /api/v1/profiles/filament`, `GET /api/v1/profiles/filament/{id}`, `PUT /api/v1/profiles/filament/{id}/raw-data`

Mirror the three machine-profile endpoints above exactly, scoped to `FilamentProfile` and its own field
set (`data-model.md`) — material/brand/colorHex/nozzleTempC/bedTempC/maxVolumetricSpeedMm3s/fanPercent
instead of nozzleSizeMm/bedSize/enclosure/supportedMaterials — **plus** `compatiblePrinterNames`
(FR-016, `research.md` §11), which has no machine-profile equivalent: a machine preset has nothing
analogous to declare, since it already *is* the description of one printer model.
