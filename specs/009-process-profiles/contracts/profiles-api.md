# Contract: Process Profiles HTTP Surface

Extends the feature-003 Profiles API and the feature-005 printer endpoints. All endpoints are
FastEndpoints classes under `OpenFarm.Api`, JWT-authenticated, permission-gated, activity-logged on
mutation, and surface failures as RFC 7807 `ProblemDetails`. `origin` values on the wire: `"System"` |
`"User"`.

Revised after review to minimize new routes: rather than adding parallel `/search` endpoints or
printer/part-scoped sub-routes, this extends the three existing list endpoints in place and folds the two
new printer-profile mutations into the existing printer edit endpoint.

---

## GET `/api/v1/profiles/machine`, `/api/v1/profiles/process`, `/api/v1/profiles/filament`

**Same routes feature 003 already ships** — extended in place with optional query parameters rather than
a separate `/search` endpoint, so there is exactly one GET per profile kind, not two.

**Permission**: unchanged (`profiles:list-machine` / `profiles:list-process` / `profiles:list-filament`).

**Query parameters** (all optional):

| Param | Type | Notes |
|---|---|---|
| `origin` | `"System"` \| `"User"` \| `"All"`, default `"User"` | Defaulting to `"User"` preserves today's exact behavior (feature 003's browse page, FR-013) with **zero query-string change** for that caller. The new printer-page/part-detail pickers explicitly pass `origin=All`. |
| `q` | string | free-text match against `Name` |
| `compatibleWithMachineName` | string | process/filament only: filters to rows whose `compatibleMachineNames` contains this exact machine preset name (or is universally compatible, i.e. empty, for filament's vendor-generic bases) |
| `printerId` | Guid | **process only** — sugar over `compatibleWithMachineName`: resolves the printer's current `machineProfileId` server-side and filters to compatible process profiles, so the frontend never has to look up a machine profile's name itself. Replaces the separate `GET /api/v1/printers/{id}/process-profile-choices` endpoint from the prior draft of this contract (see removal note below). |
| `materialType` | string | filament only |
| `page` | int | omit to get every matching row in one response (today's browse-page behavior — safe because User-origin catalogs stay small by construction); provide to paginate (the picker's large-vendor-scale case, SC-010) |
| `pageSize` | int, max 100 | only meaningful together with `page` |

Every result row is `isSelectable = true AND isCurrentlyOffered = true` **or** `origin = "User"` — a
`User` row has no such gating (unchanged from today).

**200 Response** — one envelope shape regardless of whether pagination was requested (`page`/`totalCount`
simply describe "one page containing everything" when omitted, so the frontend has exactly one shape to
handle, not two):

```jsonc
{
  "items": [
    {
      "id": "b3f...", "name": "Bambu PLA Basic @BBL X1C", "origin": "System",
      "vendorName": "Bambulab", "materialType": "PLA"          // filament-only field
    },
    { "id": "a91...", "name": "My Tuned PLA", "origin": "User" }
  ],
  "page": 1, "pageSize": 187, "totalCount": 187
}
```

- Every item's `origin` MUST be present so the frontend can render the "System: …" / "User: …" label
  (SC-008) without a second lookup.
- `GET /api/v1/profiles/process?printerId={id}` on a printer with no `machineProfileId` set returns
  `{ "available": false, "items": [], "page": 1, "pageSize": 0, "totalCount": 0 }` — the `available` field is
  present only on this `printerId`-driven call shape, giving the frontend the same explicit "nothing to
  select yet" signal the removed dedicated endpoint provided (FR-007).

**Removed from the prior draft of this contract**: `GET /api/v1/profiles/{kind}/search` (folded into the
existing routes above) and `GET /api/v1/printers/{id}/process-profile-choices` (folded into
`GET /api/v1/profiles/process?printerId={id}` above).

---

## PATCH `/api/v1/printers/{id}`

**Extends the existing `EditPrinterEndpoint`** (name + optional connection details, `EditPrinterRequest`)
rather than adding separate `PUT .../machine-profile` / `PUT .../process-profile` endpoints — one printer
configuration form, one save call, matching how IP/access code already work on this same endpoint and
directly addressing the requested UX consolidation.

**Request** (new fields added to the existing `EditPrinterRequest`; every existing field/rule is
unchanged):

```jsonc
{
  "name": "Printer 3",
  "ipAddress": "10.0.0.12", "accessCode": "...",     // existing, unchanged
  "brand": "Bambu", "modelCode": "X1C", "serialNumber": "...",  // existing, unchanged
  "machineProfileId": "b3f...",   // NEW — omit/null to clear the link, same as today's dedicated endpoint
  "processProfileId": "a91..."    // NEW — omit/null to clear the link
}
```

`machineProfileId`/`processProfileId` are applied exactly as sent (`null` = clear) — **not** "omitted means
leave unchanged" the way the paired `ipAddress`/`accessCode` fields work. This keeps their semantics
identical to today's dedicated link endpoints and avoids a partial-vs-omitted ambiguity: the printer
configuration form is expected to submit its full current state (as it already does for every other field
on this endpoint), not a sparse diff.

**Validation / responses** (adds to the existing rule set):

| Case | Result |
|---|---|
| `processProfileId` set while `machineProfileId` is (or is being set to) null | `422` ProblemDetails — `"Select a machine profile before selecting a process profile."` (FR-007) |
| `machineProfileId`/`processProfileId` doesn't exist, or wrong `origin` (a process profile must be `System`) | `400` ProblemDetails |
| `processProfileId`'s `compatibleMachineNames` doesn't contain the resolved machine profile's `Name` | `422` ProblemDetails — `"'<process>' is not compatible with '<machine>'."` (FR-006) |
| valid | `200`, same `PrinterSummaryResponse` shape as today; activity-logged per changed aspect (`PrinterEdited` for name/connection, `PrinterMachineProfileLinked`/`Unlinked`, `PrinterProcessProfileLinked`/`Unlinked` for the profile fields — FR-016) |

**Permission note (explicit tradeoff)**: this consolidation gates the whole endpoint on the existing
`printers:edit-connection` permission, replacing `printers:link-machine-profile` as a separate, narrower
gate. Anyone who can edit a printer's connection details can now also change its machine/process profile
in the same call. Flagged here as a deliberate simplification, not a silent scope change — a narrower
permission model was possible (checking `printers:link-machine-profile` only when those two fields are
present) but adds real complexity for a permission split nothing in this feature's requirements calls for.

**Removed from the prior draft of this contract**: `PUT /api/v1/printers/{id}/process-profile` (folded in
above). The existing `PUT /api/v1/printers/{id}/machine-profile` (`LinkPrinterMachineProfileEndpoint`) is
also removed, folded into the same consolidated `PATCH`.

---

## PATCH `/api/v1/parts/{id}/filament-requirements/{requirementId}`

Standalone narrow/change/clear for one filament requirement from the part detail page (FR-012), addressed
by the requirement's own real `Id` — already returned as `PartFilamentRequirementDetail.Id` by the existing
`GetPartEndpoint`, so no positional index needs inventing. Uses `PATCH`, not `PUT`, for the same reason
`EditPrinterEndpoint` does: every request here is a partial modification by construction.

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
| `requirementId` doesn't exist on this part | `404` |
| `narrowFilamentProfileId` doesn't exist | `400` ProblemDetails |
| valid | `200`, returns the updated requirement (same shape `GetPartEndpoint` already returns), activity-logged (FR-016) |

---

## Removed endpoints (FR-009/FR-011/FR-017)

| Endpoint | File |
|---|---|
| `GET/PUT /api/v1/settings/slicing-profile-defaults` | `GetSlicingProfileDefaultsEndpoint.cs`, `UpdateDefaultMachineProfileEndpoint.cs`, `UpdateOrganizationFilamentDefaultEndpoint.cs` |
| `PUT /api/v1/profiles/machine/{id}/default-process` | `SetMachineProfileDefaultProcessEndpoint.cs` |
| `PUT /api/v1/printers/{id}/machine-profile` | `LinkPrinterMachineProfileEndpoint.cs` — folded into `PATCH /api/v1/printers/{id}` above |
| process-profile bundle import path | folded out of `ProfileBundleParser`/`ConfirmProfileImportCommandHandler`/`PreviewProfileImportCommandHandler` — process presets are never accepted from an uploaded bundle |
| `PUT /api/v1/profiles/process/{id}/raw-data` | (no such endpoint exists standalone today per the survey — process raw-editing goes through the same generic raw-data path as machine/filament; that path is removed for `ProcessProfile` specifically, kept for `MachineProfile`/`FilamentProfile` `User` rows) |

Every removed endpoint's request/response types and command/handler/validator classes are deleted in the
same PR that removes them (constitution's "the two states never coexist silently past that PR").
