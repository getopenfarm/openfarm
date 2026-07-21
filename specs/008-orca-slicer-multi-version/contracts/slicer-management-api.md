# Contract: Slicer Management HTTP Surface

Extends the Phase 4 Part Library API. All endpoints are FastEndpoints classes under `OpenFarm.Api`,
JWT-authenticated, permission-gated, and surface failures as RFC 7807 `ProblemDetails`. No endpoint,
request, or response ever contains a filesystem path, PATH, or binary reference (FR-001/FR-020) — the
only slicer identifiers on the wire are **engine** and **version** strings (and, read-only, a platform).

Engine values: `OrcaSlicer` | `BambuStudio` | `SuperSlicer`. Version values: exact upstream release
strings, verbatim (e.g. `"2.2.0"`, `"2.5.59.13"`). Platform values: `Linux` | `Windows` | `MacOs`.

---

## GET `/api/v1/settings/slicing-defaults`

Returns the org's effective default plus the full list of available `(engine, version, platform)` slicers
the backend discovered — the data source for the settings page and the per-part override picker.

**Permission**: `settings:slicing-defaults:read` (or the existing read policy used for settings).

**200 Response**

```jsonc
{
  "default": {                       // the EFFECTIVE default (explicit if set, else newest-available fallback); null if no slicer is available for the host
    "engine": "OrcaSlicer",
    "version": "2.2.0",
    "isExplicit": false              // false ⇒ auto-defaulted (FR-003); true ⇒ admin-set
  },
  "available": [                     // host-platform-usable slicers only; may be empty
    { "engine": "OrcaSlicer", "version": "2.3.0", "platform": "Linux" },
    { "engine": "OrcaSlicer", "version": "2.2.0", "platform": "Linux" },
    { "engine": "SuperSlicer", "version": "2.5.59.13", "platform": "Linux" }
  ]
}
```

- `available` is ordered by engine then version descending (newest first) using `SlicerVersionComparer`.
- `default` is `null` only when `available` is empty (FR-011) — the UI shows the actionable "no slicer
  available" state.

---

## PUT `/api/v1/settings/slicing-defaults`

Sets the org default `(engine, version)`. Admin-only; the change is written to the activity log
(`SlicingDefaultsUpdated`, entity `Organization`) by the post-processor (FR-019).

**Permission**: `settings:slicing-defaults:update`.

**Request**

```jsonc
{ "engine": "SuperSlicer", "version": "2.5.59.13" }
```

**Validation / responses**

| Case | Result |
|------|--------|
| `engine` not a known member | `400` ProblemDetails (validation) |
| `version` empty | `400` ProblemDetails (validation) |
| `(engine, version)` not present for the host platform | `422` ProblemDetails — `"OrcaSlicer 9.9.9 is not an available slicer."` (FR-007) |
| valid & available | `200` — same shape as GET, `default.isExplicit = true` |

---

## POST `/api/v1/parts` (changed) and PUT `/api/v1/parts/{id}/file` (changed)

The add / replace endpoints gain an **optional** per-part version alongside the existing optional engine
override (FR-004). Multipart form fields (existing `File` unchanged):

| Field | Type | Notes |
|-------|------|-------|
| `File` | file | unchanged |
| `SlicerEngine` | string? | existing — optional engine override |
| `SlicerVersion` | string? | **new** — optional version override; only meaningful with a requires-slicing file |

**Resolution & validation** (both endpoints, via the shared `ResolveSlicerSelection` service):

- Neither field present → the effective org default is used (FR-004).
- `SlicerEngine` present, `SlicerVersion` absent → the newest available version **of that engine** for the
  host (FR-003 semantics applied to the chosen engine).
- Both present → that exact pair.
- Any resolved pair not present for the host platform (e.g. a stale client) → **`422` ProblemDetails**
  naming the engine+version, **before** any job/processing starts (FR-006/US2 scenario 3) — never a hung
  job.
- A requires-slicing file when **no** slicer is available at all → `422`/`409` ProblemDetails "no slicer
  available" (FR-006/US1 scenario 4).
- Files that don't require slicing ignore both fields (unchanged behavior).

Success is unchanged: `202 Accepted` with `{ "jobId", "status" }`. The resolved engine+version is recorded
on the job and, on confirm, the Part (FR-005) — see the job/part read contracts below.

---

## GET `/api/v1/part-processing-jobs/{id}` (changed) & GET `/api/v1/parts/{id}` (changed)

Both read models expose the recorded engine+version so the UI can display what a slice was (or is being)
produced with (FR-005). Added fields (nullable — `null` for non-slicing files and legacy pre-feature
rows):

```jsonc
// on the job response and the part response
"slicer": { "engine": "SuperSlicer", "version": "2.5.59.13" }  // null when not applicable
```

No path/binary field is added anywhere.

---

## Error shape

All non-2xx responses are RFC 7807 `ProblemDetails` (existing global handler). Slicer-availability failures
use a typed domain exception (`SlicerUnavailableException`) mapped to `422`, with a `detail` that names the
engine and version and is actionable for a self-hosting operator (FR-021), e.g.
`"SuperSlicer 2.5.59.13 is not bundled for this platform (Linux)."`
