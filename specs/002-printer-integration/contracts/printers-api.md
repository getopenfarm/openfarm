# Contract: Printers REPR endpoints (`OpenFarm.Api`)

REPR pattern (Request DTO → Handler → Response DTO), one FastEndpoints class per endpoint, per the
constitution. All errors surface as RFC 7807 `ProblemDetails` via the existing global exception-handling
middleware — no endpoint-specific error shapes. All endpoints require an authenticated JWT with
`organization_id` claim (Finbuckle-scoped); permission checks gate on the `permissions` catalog, not
role names, per the constitution's RBAC principle.

## `POST /api/v1/printers/verify-connection`

Pre-flight check the add/edit form calls before enabling submit (FR-001a/FR-002, research.md §7).
Does **not** persist anything.

**Request**
```jsonc
{ "ipAddress": "192.168.1.50", "serialNumber": "01P00A123456789", "accessCode": "12345678" }
```

**Response 200** (verified reachable)
```jsonc
{ "verified": true }
```

**Response 422** (unreachable / bad access code) — `ProblemDetails`
```jsonc
{ "type": "...", "title": "Printer connection could not be verified", "status": 422,
  "detail": "Could not authenticate with the printer at 192.168.1.50 within 5 seconds. Check the IP address and LAN access code." }
```

## `POST /api/v1/printers`

Register a new printer. FR-001a: server-side re-runs the same verify check as
`verify-connection` before persisting — a stale client-side "verified" state is never trusted alone.
`brand`/`modelCode` must resolve against the `PrinterModelCatalog` (`data-model.md`'s `PrinterModel`
section) — not accepted as free text.

**Request**
```jsonc
{ "name": "Printer 1", "brand": "Bambu", "modelCode": "P1S", "ipAddress": "192.168.1.50", "serialNumber": "01P00A123456789", "accessCode": "12345678" }
```

**Response 201**
```jsonc
{ "id": "guid", "name": "Printer 1", "brand": "Bambu", "modelCode": "P1S", "ipAddress": "192.168.1.50", "serialNumber": "01P00A123456789", "status": "Idle", "isReachable": false }
```

**Response 409** (duplicate serial number, FR-001b) — `ProblemDetails`, `detail` identifies the
conflicting printer by name.

**Response 422** — verification failed server-side (same shape as `verify-connection`'s 422), **or**
`(brand, modelCode)` doesn't resolve to a known `PrinterModelCatalog` entry — `detail` names the
unrecognized combination rather than a generic validation failure.

## `PUT /api/v1/printers/{id}/connection`

Edit connection details (IP/access code) on an existing printer. Same verify-before-save gate as
registration (FR-002) — request/response shapes mirror `POST /api/v1/printers`'s connection fields
and 422 response.

**Request**
```jsonc
{ "ipAddress": "192.168.1.51", "accessCode": "87654321" }
```

**Response 200** — updated printer summary (same shape as registration's 201 body).

## `DELETE /api/v1/printers/{id}`

Remove a printer. **Never blocked by current print status** (FR-002/clarification) — the frontend
shows a confirmation warning before calling this, but the endpoint itself performs no status check.

**Response 204.**

## `POST /api/v1/printers/{id}/mark-ready`

The only manual `PrinterStatus` transition this phase supports: `AwaitingClear → Idle`.

**Response 200** — updated printer summary with `"status": "Idle"`.

**Response 409** (illegal transition — printer isn't currently `AwaitingClear`) — `ProblemDetails`,
`detail` states the current status and why the transition is rejected (FR-003, SC-004).

## `GET /api/v1/printers`

Fleet list.

**Response 200**
```jsonc
{
  "printers": [
    { "id": "guid", "name": "Printer 1", "brand": "Bambu", "modelCode": "P1S", "ipAddress": "192.168.1.50", "serialNumber": "01P00A123456789", "status": "Printing", "isReachable": true, "printProgressPercent": 42 }
  ]
}
```

**Implementation note (added during Phase 3 build)**: `ipAddress`/`serialNumber` were added to this
response beyond the original sketch above — `EditPrinterDialog`'s verify-before-save gate needs a
printer's current IP and serial to prefill/re-verify without a separate detail-endpoint round trip,
and `GetPrinterDetailEndpoint` doesn't exist until Phase 4 (US2).

## `GET /api/v1/printers/{id}`

Printer detail — full telemetry + AMS snapshot.

**Response 200**
```jsonc
{
  "id": "guid", "name": "Printer 1", "brand": "Bambu", "modelCode": "P1S", "ipAddress": "192.168.1.50", "serialNumber": "01P00A123456789",
  "status": "Printing", "isReachable": true,
  "telemetry": { "nozzleTempC": 219.4, "bedTempC": 59.8, "printProgressPercent": 42, "currentStageLabel": "printing" },
  "feedReadings": [ { "kind": "MultiMaterialUnit", "unitIndex": 0, "displayLabel": "AMS 1", "slots": [ { "slotPosition": 0, "extruderIndex": 0, "displayLabel": "AMS 1 · Slot 1", "empty": false, "materialType": "PLA", "colorHex": "1A1A1AFF", "remainPercent": 62, "active": true, "rfidId": null } ] } ]
}
```

**Response 404** — printer not found (or not in caller's organization, per Finbuckle's query filter —
indistinguishable from "not found" to the caller, same as every other tenant-scoped resource).
