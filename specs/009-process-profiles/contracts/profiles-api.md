# Contract: Process Profiles HTTP Surface

Extends the feature-003 Profiles API and the feature-005 printer endpoints. All endpoints are
FastEndpoints classes under `OpenFarm.Api`, JWT-authenticated, permission-gated, activity-logged on
mutation, and surface failures as RFC 7807 `ProblemDetails`. `origin` values on the wire: `"System"` |
`"User"`.

---

## GET `/api/v1/profiles/machine/search`, `/api/v1/profiles/process/search`, `/api/v1/profiles/filament/search`

The combined System+User picker query backing the printer page (machine, process) and part detail page
(filament) — replaces an unbounded `ListAsync` for these new call sites (research.md §6). The existing
`GET /api/v1/profiles/machine` / `/filament` (unbounded, feature 003) are unchanged and stay scoped to
`origin=User` only for the import/browse page (FR-013) — these are new, additional endpoints, not
replacements.

**Permission**: same read policy as the existing profile-browse endpoints.

**Query parameters** (all optional except pagination):

| Param | Type | Notes |
|---|---|---|
| `q` | string | free-text match against `Name` |
| `compatibleWithMachineName` | string | process/filament only: filters to rows whose `compatibleMachineNames` contains this exact machine preset name (or is universally compatible, i.e. empty, for filament's vendor-generic bases) |
| `materialType` | string | filament only |
| `page` | int, default 1 | |
| `pageSize` | int, default 25, max 100 | |

Every result row is `isSelectable = true AND isCurrentlyOffered = true` **or** `origin = "User"` — a
`User` row has no such gating (unchanged from today).

**200 Response**

```jsonc
{
  "items": [
    {
      "id": "b3f...", "name": "Bambu PLA Basic @BBL X1C", "origin": "System",
      "vendorName": "Bambulab", "materialType": "PLA"          // filament-only field
    },
    { "id": "a91...", "name": "My Tuned PLA", "origin": "User" }
  ],
  "page": 1, "pageSize": 25, "totalCount": 187
}
```

- Every item's `origin` MUST be present so the frontend can render the "System: …" / "User: …" label
  (SC-008) without a second lookup.

---

## PUT `/api/v1/printers/{id}/process-profile`

Selects a printer's process profile — parallel to the existing `LinkPrinterMachineProfileEndpoint`.

**Permission**: same as `LinkPrinterMachineProfileEndpoint`.

**Request**

```jsonc
{ "processProfileId": "b3f..." }   // or { "processProfileId": null } to clear (FR-016 logs a clear the same as a change)
```

**Validation / responses**

| Case | Result |
|---|---|
| printer has no `machineProfileId` set | `422` ProblemDetails — `"Select a machine profile before selecting a process profile."` (FR-007) |
| `processProfileId` doesn't exist or `origin != System` | `400` ProblemDetails |
| process profile's `compatibleMachineNames` doesn't contain the printer's machine profile `Name` | `422` ProblemDetails — `"'<process>' is not compatible with '<machine>'."` (FR-006) |
| valid | `200`, activity-logged (`PrinterProcessProfileLinked`/`Unlinked`, entity `Printer`) |

---

## GET `/api/v1/printers/{id}/process-profile-choices`

Backs the process-profile picker's compatibility filtering (User Story 2, Acceptance Scenario 2/5) — same
shape as the `/search` endpoints above but pre-filtered to the printer's currently-selected machine and
returning an explicit "unavailable" signal when no machine is selected yet, so the frontend doesn't have to
infer it from an empty list vs. a real empty-choices state.

**200 Response**

```jsonc
{ "available": true, "items": [ /* same item shape as /search, no origin=User entries ever (FR-011) */ ] }
// or, when the printer has no machine profile selected:
{ "available": false, "items": [] }
```

---

## PUT `/api/v1/parts/{id}/filament-requirements/{requirementIndex}/narrow`

Standalone narrow/change/clear for one filament requirement from the part detail page (FR-012) — the new
surface data-model.md's `PartFilamentRequirement` section calls for, distinct from the existing
one-time confirm-flow override on `ConfirmPartProcessingJobEndpoint`.

**Permission**: same as existing part-mutation endpoints.

**Request** (mutually exclusive, mirrors `ConfirmPartProcessingJobCommandValidator`'s existing rule)

```jsonc
{ "narrowFilamentVariantId": null, "narrowFilamentProfileId": "b3f..." }
// or { "narrowFilamentVariantId": null, "narrowFilamentProfileId": null }  // clear
```

**Validation / responses**

| Case | Result |
|---|---|
| both `narrowFilamentVariantId` and `narrowFilamentProfileId` set | `400` ProblemDetails (validation) |
| `requirementIndex` doesn't exist on this part | `404` |
| `narrowFilamentProfileId` doesn't exist | `400` ProblemDetails |
| valid | `200`, returns the updated requirement (same shape `GetPartEndpoint` already returns), activity-logged (FR-016) |

---

## Removed endpoints (FR-009/FR-011/FR-017)

| Endpoint | File |
|---|---|
| `GET/PUT /api/v1/settings/slicing-profile-defaults` | `GetSlicingProfileDefaultsEndpoint.cs`, `UpdateDefaultMachineProfileEndpoint.cs`, `UpdateOrganizationFilamentDefaultEndpoint.cs` |
| `PUT /api/v1/profiles/machine/{id}/default-process` | `SetMachineProfileDefaultProcessEndpoint.cs` |
| process-profile bundle import path | folded out of `ProfileBundleParser`/`ConfirmProfileImportCommandHandler`/`PreviewProfileImportCommandHandler` — process presets are never accepted from an uploaded bundle |
| `PUT /api/v1/profiles/process/{id}/raw-data` | (no such endpoint exists standalone today per the survey — process raw-editing goes through the same generic raw-data path as machine/filament; that path is removed for `ProcessProfile` specifically, kept for `MachineProfile`/`FilamentProfile` `User` rows) |

Every removed endpoint's request/response types and command/handler/validator classes are deleted in the
same PR that removes them (constitution's "the two states never coexist silently past that PR").
