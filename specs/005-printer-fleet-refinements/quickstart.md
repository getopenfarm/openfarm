# Phase 1 Quickstart: Printer Fleet Management Refinements

Validation guide for all four user stories. See `data-model.md` for entity/exception shapes and
`contracts/printers-api.md` for exact request/response bodies. Requires a printer already registered
(see `specs/002-printer-integration/quickstart.md` Scenario 2) — reuse that flow against
`FakePrinterConnector` for setup below.

## Prerequisites

```bash
cp .env.example infra/.env
cd infra && docker compose up   # postgres + api (dotnet watch) + web (bun run dev)
curl -f http://localhost:8888/health/ready
```

Log in as the seeded operator and keep the JWT for the calls below. Register a test printer if one
doesn't already exist:

```bash
curl -s -X POST http://localhost:8888/api/v1/printers \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Test Printer", "brand": "Bambu", "modelCode": "P1S", "ipAddress": "10.0.0.50", "serialNumber": "FAKE-0001", "accessCode": "12345678"}' | jq .
```

Note the returned `id` for use below.

## Scenario 1 — Rename without re-verification (User Story 1, FR-003, acceptance scenario 1)

```bash
curl -i -X PATCH http://localhost:8888/api/v1/printers/{id} \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Renamed Printer"}'
```

**Expected**: `200`, updated summary shows `"name": "Renamed Printer"`. In the browser: open the
printer's detail page, use its edit action, change only the name — Save is enabled immediately with
no "verifying…" state and no access-code re-entry required.

## Scenario 2 — Edit connection details, verify-before-save still applies (FR-003, acceptance scenario 2)

```bash
curl -i -X PATCH http://localhost:8888/api/v1/printers/{id} \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Renamed Printer", "ipAddress": "10.0.0.51", "accessCode": "wrong-code"}'
```

**Expected**: `422` against a `FakePrinterConnector` configured to reject this credential — the
printer's `ipAddress` is unchanged in the fleet list. Retry with the correct access code:

```bash
curl -i -X PATCH http://localhost:8888/api/v1/printers/{id} \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Renamed Printer", "ipAddress": "10.0.0.51", "accessCode": "12345678"}'
```

**Expected**: `200`; the printer's live connection reconnects using the new IP (FR-004) — confirm via
the detail page's reachability indicator flipping through a brief reconnect.

Mismatched-pair negative check (FR-003's "both or neither" rule, contracts/printers-api.md):

```bash
curl -i -X PATCH http://localhost:8888/api/v1/printers/{id} \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Renamed Printer", "ipAddress": "10.0.0.52"}'
```

**Expected**: `400` — `accessCode` required whenever `ipAddress` is supplied.

## Scenario 3 — Zero-trust rejection of immutable fields (FR-010, acceptance scenario 6, SC-007)

```bash
curl -i -X PATCH http://localhost:8888/api/v1/printers/{id} \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Renamed Printer", "serialNumber": "SOMETHING-ELSE"}'
```

**Expected**: `422`, `detail` names `serialNumber` as the mismatched field; the printer's serial
number in the fleet list is unchanged. Confirm no activity-log entry beyond the standard rejected
request (clarification, 2026-07-17 — no dedicated audit path):

```bash
curl -s "http://localhost:8888/api/v1/activity-log?page=1&pageSize=25" -H "Authorization: Bearer <token>" \
  | jq '.items[] | select(.entityType == "Printer")'
```

**Expected**: no new entry corresponding to the rejected request (it never reached a mutation).

## Scenario 4 — Edit/Remove only on the detail page (User Story 2, SC-003)

In the browser: open the fleet list in **table** view — inspect a row's menu; confirm no Edit or
Remove option, only navigation to the detail page (and "Mark ready" for an `AwaitingClear` printer).
Switch to **grid** view — confirm the same on a card's hover overlay.

Open the printer's detail page: confirm both a Settings/Edit action and a Remove action are present
and functional.

```bash
curl -i -X DELETE http://localhost:8888/api/v1/printers/{id} -H "Authorization: Bearer <token>"
```

**Expected**: `204`. In the browser, using Remove from the detail page: confirm the same confirmation
step as before, and that the operator lands back on the fleet list afterward (SC-004), with an
activity-log entry (`EventType: "PrinterRemoved"`) exactly as today's fleet-list removal produced.

## Scenario 5 — Catalog-driven Add-printer dialog (User Story 3, SC-005)

```bash
curl -s http://localhost:8888/api/v1/printers/catalog -H "Authorization: Bearer <token>" | jq .
```

**Expected**: `200`, brands/models matching `PrinterModelCatalog`'s current contents
(`data-model.md`). In the browser: open the Add-printer dialog — confirm Brand/Model options match
this response exactly, with no reference to the old hardcoded frontend list (verify by grepping
`web/src/features/printers` for `PRINTER_BRANDS`/`MODELS_BY_BRAND` and confirming zero results after
implementation).

Error-state check: temporarily stop the API (`docker compose stop api`), open the Add-printer dialog.

**Expected**: a clear loading-then-error state, submission disabled — not an empty or broken
selector (US3 AC3). Restart the API afterward (`docker compose start api`).

## Scenario 6 — SignalR contract audit (User Story 4, SC-006)

This story is a code review, not a runtime scenario. Confirm as part of implementation review:

- `api/src/OpenFarm.Api/Hubs/FarmHub.cs` and `FarmHubPrinterEventPublisher.cs` — every broadcast field
  (`status`, `isReachable`, `nozzleTempC`, `bedTempC`, `printProgressPercent`, `currentStageLabel`,
  `frameJpegBase64`) reads as canonical, not brand-keyed.
- `web/src/features/printers/hooks/useFarmHub.ts` — no branch keyed on `brand`/`modelCode`.

**Expected outcome** (research.md §6): zero code changes required; this quickstart scenario passes by
confirming the audit finding still holds against the code as actually implemented, not by exercising
new behavior.

## Automated verification

```bash
dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~Printer"
cd web && bunx vitest run src/features/printers && bunx tsc -b
```
