# Contract: Assembly (BOM) REPR endpoints (`OpenFarm.Api`)

REPR pattern, one FastEndpoints class per endpoint, `[Permission("...")]` per Phase 0's placeholder-policy
precedent, all errors as RFC 7807 `ProblemDetails`. See `data-model.md` for entity shapes and
`research.md` for the reasoning behind explosion/validation being split across two different mechanisms.

**Line-level mutation, not whole-assembly PUT-replace.** Adding, editing, or removing one `BomLine` is
its own endpoint (`POST/PUT/DELETE .../lines[/...]`) rather than one endpoint that accepts a full
replacement line set and diffs it. This is a deliberate choice, not the default REPR shape used elsewhere
(`ReplacePartFileEndpoint`'s all-or-nothing replace, by contrast, suits Part precisely because a part's
file genuinely has no addressable sub-parts) — FR-015 requires the activity log to distinguish "line
added" from "line changed" from "line removed," and `spec.md`'s own acceptance scenarios describe the
operation as "add a line," never "resubmit the whole line set." A line-level shape gets FR-015's
granularity for free from the existing per-endpoint `IActivityLoggable` post-processor, with no
diffing logic needed anywhere.

**Every structural write (`POST`/`PUT`/`DELETE` on an assembly or its lines) is validated and persisted
inside one transaction that holds the per-organization advisory lock for its full duration**
(`research.md` R3) — this is an implementation detail invisible in the wire contract below (no client-
observable field), but explains why a `409` cycle-rejection can legitimately surface on a save that was
individually valid against everything the client itself had read moments earlier: it lost a race against
a concurrent save to a *different* assembly (FR-007a).

## Assemblies

### `POST /api/v1/assemblies`
`[Permission("assemblies:create")]`

**Request**:
```jsonc
{
  "name": "Enclosure",
  "description": "string | null",
  "lines": [
    { "targetKind": "Part", "targetId": "guid", "quantity": 2 },
    { "targetKind": "Assembly", "targetId": "guid", "quantity": 1 },
    { "targetKind": "NonPrintedComponent", "targetId": "guid", "quantity": 4.5 }
  ]
}
```
`lines` MUST contain at least one entry (FR-002). `quantity` MUST be a whole number `>= 1` for
`targetKind: "Part" | "Assembly"`, `> 0` (fractional allowed) for `"NonPrintedComponent"` (FR-004a/b).

**Response 201**: the created assembly, same shape as `GET /api/v1/assemblies/{id}` below.

**Response 400** — empty `name`; a line has an invalid `targetKind`/`quantity` combination.
**Response 404** — a line's `targetId` doesn't resolve to an existing Part/Assembly/NonPrintedComponent
in the caller's organization.
**Response 409** — a line's target appears more than once (FR-013); a new sub-assembly line would create
a cycle (FR-007) or exceed the 10-level depth limit (FR-006a) — response `detail` names the offending
containment path or states the limit, per FR-007/FR-006a's own wording requirement.

### `GET /api/v1/assemblies`
`[Permission("assemblies:view")]` — paginated list, mirrors `GET /parts`'s `page`/`pageSize` query
params and `{ items, totalCount }` envelope.

```jsonc
{
  "items": [ { "id": "guid", "name": "Enclosure", "description": "string | null", "lineCount": 3 } ],
  "totalCount": 1
}
```

### `GET /api/v1/assemblies/{id}`
`[Permission("assemblies:view")]` — one level of structure only (`research.md` R7 — client expands a
sub-assembly line by calling this same endpoint again with that line's `targetId`).

```jsonc
{
  "id": "guid",
  "name": "Enclosure",
  "description": "string | null",
  "lines": [
    {
      "id": "guid",
      "targetKind": "Part" /* | "Assembly" | "NonPrintedComponent" */,
      "targetId": "guid",
      "targetName": "Bracket",
      "quantity": 2
    }
  ],
  "createdAtUtc": "2026-08-06T00:00:00Z",
  "updatedAtUtc": "2026-08-06T00:00:00Z"
}
```

**Response 404** — no such assembly in the caller's organization.

### `PUT /api/v1/assemblies/{id}`
`[Permission("assemblies:edit")]` — renames/redescribes only; does not touch `lines` (use the line
endpoints below). `{ "name": "string", "description": "string | null" }` → `200` with the same shape as
`GET`. `404` if not found.

### `DELETE /api/v1/assemblies/{id}`
`[Permission("assemblies:delete")]`

**Response 204**.
**Response 404** — not found.
**Response 409** — blocked: this assembly is targeted by a `BomLine` on one or more other assemblies
(FR-011); response `detail` names every referencing assembly.

### `POST /api/v1/assemblies/{id}/lines`
`[Permission("assemblies:edit")]` — add one line. Body: a single line object (same shape as one entry
of `POST /api/v1/assemblies`'s `lines` array). `201` with the created line
(`{ id, targetKind, targetId, targetName, quantity }`). Same `400`/`404`/`409` cases as create, scoped to
this one line.

### `PUT /api/v1/assemblies/{id}/lines/{lineId}`
`[Permission("assemblies:edit")]` — change only that line's `quantity` (its target is immutable once
created — changing a target is remove-then-add, so it goes through cycle/depth validation as a fresh
add rather than an ambiguous in-place retarget). `{ "quantity": 3 }` → `200` with the updated line.
`404` if the assembly or line doesn't exist. `400` if the quantity violates FR-004a/b for that line's
target kind.

### `DELETE /api/v1/assemblies/{id}/lines/{lineId}`
`[Permission("assemblies:edit")]`

**Response 204**.
**Response 404** — assembly or line not found.
**Response 409** — this is the assembly's last remaining line (FR-002 — an assembly is never left with
zero lines; delete the assembly itself instead).

### `GET /api/v1/assemblies/{id}/explosion?quantity={n}`
`[Permission("assemblies:view")]` — FR-009, pure/read-only, nothing persisted. `quantity` required,
whole number `>= 1` (FR-009e).

```jsonc
{
  "printedParts": [ { "partId": "guid", "partName": "Knob", "quantity": 6 } ],
  "nonPrintedComponents": [ { "nonPrintedComponentId": "guid", "name": "M3x8 screw", "unit": "each", "quantity": 27 } ]
}
```

**Response 400** — `quantity` missing, zero, negative, or non-integer.
**Response 404** — no such assembly.

### `POST /api/v1/assemblies/{id}/buildable-units`
`[Permission("assemblies:view")]` — `POST`, not `GET`, because the request body carries the caller-
supplied good-quantity map (FR-010e — this phase never stores or sources these itself; Phase 8 will be
the first real caller). Still a pure read/computation — nothing persisted.

**Request**:
```jsonc
{ "goodQuantities": { "<partId-guid>": 10, "<partId-guid-2>": 12 } }
```
A part present in the assembly's exploded tree but omitted from `goodQuantities` is treated as `0`
(FR-010c's zero-good-quantity case), not rejected — the caller is describing what's on hand, and having
none of a required part is exactly the case this calculation exists to catch.

**Response 200**:
```jsonc
{ "isApplicable": true, "value": 4, "notApplicableReason": null }
```
or, when the exploded tree contains no printed parts at all (FR-010d):
```jsonc
{ "isApplicable": false, "value": null, "notApplicableReason": "Assembly contains no printed parts." }
```

**Response 404** — no such assembly.

## Non-Printed Components

### `POST /api/v1/non-printed-components`
`[Permission("non-printed-components:create")]`

**Request**: `{ "name": "M3x8 screw", "unit": "each", "unitCost": 0.02 }` — `unitCost` optional/nullable
(FR-005c). **Response 201**: the created component.
**Response 400** — empty `name`/`unit`.

### `GET /api/v1/non-printed-components`
`[Permission("non-printed-components:view")]` — paginated list, same envelope shape as `GET /assemblies`.

### `PUT /api/v1/non-printed-components/{id}`
`[Permission("non-printed-components:edit")]` — same body shape as create. `200` with the updated
component. `404` if not found.

### `DELETE /api/v1/non-printed-components/{id}`
`[Permission("non-printed-components:delete")]`

**Response 204**.
**Response 404** — not found.
**Response 409** — blocked: referenced by one or more `BomLine`s (FR-011); `detail` names the
referencing assemblies.
