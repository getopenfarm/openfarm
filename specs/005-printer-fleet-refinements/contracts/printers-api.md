# Contract: Printers REPR endpoints — additions/replacements over `specs/002-printer-integration/contracts/printers-api.md`

This file documents only what this feature **adds or changes**. Every other endpoint in the base
contract (`POST /api/v1/printers`, `POST /api/v1/printers/verify-connection`,
`POST /api/v1/printers/{id}/mark-ready`, `GET /api/v1/printers`, `GET /api/v1/printers/{id}`) is
unchanged by this feature and remains governed by that file. `DELETE /api/v1/printers/{id}` is also
unchanged at the contract level — only which frontend surface calls it moves (User Story 2).

Same conventions as the base contract: REPR pattern, one FastEndpoints class per endpoint, errors as
RFC 7807 `ProblemDetails` via the existing global `DomainExceptionHandler`, JWT + Finbuckle
tenant-scoping, permission checks gate on the `permissions` catalog per the constitution.

## `PATCH /api/v1/printers/{id}` — replaces `PUT /api/v1/printers/{id}/connection`

Single edit operation covering name and (optionally) connection details. Uses `PATCH`, not `PUT`,
because the request is inherently a partial update — `ipAddress`/`accessCode` are optional, and the
access code is write-only (never round-tripped to the client), so a client can never resend the
printer's full current state as strict `PUT` replacement semantics would require. This is the first
`PATCH` endpoint in the API (every other edit endpoint, including the connection-only edit this one
replaces, uses `PUT`); see research.md §1a for why this deviation from existing precedent was judged
worth making. Requires `[Permission("printers:edit-connection")]`, reused unchanged from the
connection-only edit it replaces (spec Assumptions — no new permission). Never blocked by the
printer's current status (FR-001).

**Request — name-only change (FR-003: no re-verification)**
```jsonc
{ "name": "Printer 1 (renamed)" }
```

**Request — name + connection change (FR-001, FR-003: verify-before-save gate applies)**
```jsonc
{ "name": "Printer 1", "ipAddress": "192.168.1.51", "accessCode": "87654321" }
```
`ipAddress` and `accessCode` MUST be supplied together or not at all — supplying exactly one is a
`400` validation error ("provide both ipAddress and accessCode to change connection details, or
neither to keep the current connection").

**Request — zero-trust rejection (FR-002, FR-010)**: a request that additionally supplies `brand`,
`modelCode`, or `serialNumber` is accepted only if every supplied value matches the printer's current
value exactly; the product UI never sends these fields at all.
```jsonc
{ "name": "Printer 1", "brand": "Bambu", "modelCode": "X1C", "serialNumber": "01P00A123456789" }
```

**Response 200** — updated printer summary (same `PrinterSummaryResponse` shape as registration's 201
body and today's connection-edit's 200 body).

**Response 400** — `ipAddress`/`accessCode` supplied as a mismatched pair (one present, one absent),
or `name` empty. Standard FluentValidation `ProblemDetails`.

**Response 422** — either the connection-fields verification failed (same shape as
`verify-connection`'s 422, when `ipAddress`+`accessCode` were supplied), **or** a supplied `brand`/
`modelCode`/`serialNumber` doesn't match the printer's current value (FR-010) — `detail` names which
field(s) mismatched. The two 422 causes are distinguishable by `title`
("Printer connection could not be verified" vs. a new title such as "Printer identity fields cannot
be changed"), not by status code alone.

**Response 404** — printer not found (or not in caller's organization).

## `GET /api/v1/printers/catalog`

New. Backend-exposed printer catalog (User Story 3) — the same `(Brand, ModelCode)` pairs
`POST /api/v1/printers` and this feature's `PUT /api/v1/printers/{id}` validate against, sourced from
the same `PrinterModelCatalog` (FR-008: cannot drift from what registration accepts). Requires
`[Permission("printers:register")]`, reused unchanged (spec Assumptions — no new permission; same
access level a user already needs to reach the Add-printer dialog).

**Response 200**
```jsonc
{
  "brands": [
    { "brand": "Bambu", "models": ["X1", "X1C", "X1E", "P1P", "P1S", "A1", "A1MINI"] },
    { "brand": "System", "models": ["Simulation"] }
  ]
}
```

No error responses beyond the standard authenticated-but-unauthorized `403`/unauthenticated `401` —
this endpoint has no request body and no per-call failure mode of its own (US3 AC3's "clear
loading/error state" is a frontend concern for a network-level failure, not a distinct response
shape this endpoint defines).

## `DELETE /api/v1/printers/{id}` — contract unchanged, caller relocated (User Story 2)

Documented in full in `specs/002-printer-integration/contracts/printers-api.md`. This feature changes
**only** which frontend component invokes it — the printer detail page, not the fleet table/grid — and
adds no new response shape, status code, or validation rule. `FR-006`'s "same confirmation and
consequences as today's fleet-list removal" is satisfied by this endpoint being byte-for-byte the
same one, called from a new location.
