# Phase 1 Quickstart: Fleet Management + Live Bambu Integration

Validation guide proving User Story 1 (register a printer, watch its live status with no manual
refresh) end-to-end. See `data-model.md` for entity shapes and `contracts/` for exact request/response
bodies. Physical-hardware validation against a real Bambu printer is a separate manual step (last
section) — everything else here uses `FakePrinterConnector`, since CI never touches real hardware
(per constitution Principle II).

## Prerequisites

```bash
cp .env.example infra/.env
cd infra && docker compose up   # postgres + api (dotnet watch, applies the AddPrinters migration) + web (bun run dev)
curl -f http://localhost:8888/health/ready
```

Log in as the seeded operator (see `specs/001-foundation/quickstart.md` Scenario 1) and keep the JWT
for the calls below.

## Scenario 1 — Verify-before-add gating (User Story 1, FR-001a)

```bash
curl -i -X POST http://localhost:8888/api/v1/printers/verify-connection \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"ipAddress": "10.0.0.99", "serialNumber": "FAKE-0001", "accessCode": "wrong"}'
```

**Expected**: `422` — verification fails against the dev/test `FakePrinterConnector` when configured
to simulate an unreachable/bad-credential printer. In the browser's Add Printer dialog, confirm the
submit button stays disabled until this call succeeds — no printer is created from an unverified form
state (SC-007).

Now verify against a `FakePrinterConnector`-registered fixture that simulates a real reachable
printer:

```bash
curl -i -X POST http://localhost:8888/api/v1/printers/verify-connection \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"ipAddress": "10.0.0.50", "serialNumber": "FAKE-0001", "accessCode": "12345678"}'
```

**Expected**: `200`, `{"verified": true}`.

## Scenario 2 — Register and see it in the fleet list (User Story 1)

```bash
curl -i -X POST http://localhost:8888/api/v1/printers \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Test Printer", "brand": "Bambu", "modelCode": "P1S", "ipAddress": "10.0.0.50", "serialNumber": "FAKE-0001", "accessCode": "12345678"}'
```

**Expected**: `201`, `status: "Idle"`, `isReachable` initially `false` until the connector's first
message arrives.

```bash
curl -s http://localhost:8888/api/v1/printers -H "Authorization: Bearer <token>" | jq .
```

**Expected**: the new printer appears within moments (SC-001: under 2 minutes end-to-end).

Duplicate-serial negative check:

```bash
curl -i -X POST http://localhost:8888/api/v1/printers \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"name": "Duplicate", "brand": "Bambu", "modelCode": "P1S", "ipAddress": "10.0.0.51", "serialNumber": "FAKE-0001", "accessCode": "12345678"}'
```

**Expected**: `409`, `detail` names the conflicting printer "Test Printer" (FR-001b). Re-run with a
different serial but the *same* IP as the first printer — expect `201` (duplicate IPs are permitted).

## Scenario 3 — Live status update with no manual refresh (User Story 1, SC-002)

In the browser: open the fleet list page, confirm "Test Printer" shows `Idle`.

Using the dev-only fake-connector control (drives `FakePrinterConnector`'s simulated MQTT feed —
exact trigger mechanism is an implementation-time detail, e.g. a test-only endpoint or a fixture
replay script), simulate the printer reporting `gcode_state: "running"`.

**Expected**: within 5 seconds, the fleet list's "Test Printer" row updates to `Printing` **without**
the page being refreshed — confirm via the browser's network tab that no polling request fired; the
update arrives over the `/hubs/farm` WebSocket as a `PrinterStatusChanged` event (contracts/farm-hub.md).

Simulate `gcode_state: "finish"`.

**Expected**: status becomes `AwaitingClear` (not `Idle`) — FR-003 edge case, confirmed live.

## Scenario 4 — Mark ready / illegal transition (User Story 4)

```bash
curl -i -X POST http://localhost:8888/api/v1/printers/{id}/mark-ready \
  -H "Authorization: Bearer <token>"
```

**Expected**: `200`, `status: "Idle"`. Check the activity log:

```bash
curl -s "http://localhost:8888/api/v1/activity-log?page=1&pageSize=25" -H "Authorization: Bearer <token>" \
  | jq '.items[] | select(.entityType == "Printer")'
```

**Expected**: entries for both the connector-reported `AwaitingClear` transition (`actorType:
"System"`) and this manual "mark ready" transition (`actorType: "Operator"`) — SC-003's 100%-logged
requirement.

Illegal-transition negative check: call `mark-ready` again immediately (printer is now `Idle`, not
`AwaitingClear`).

**Expected**: `409`, human-readable `detail` explaining the printer isn't awaiting clear (SC-004).

## Scenario 5 — Live telemetry + AMS (User Story 2)

Open "Test Printer"'s detail page. Simulate a `print` MQTT delta via the fake connector including
nozzle/bed temperatures, progress percentage, and one populated AMS tray.

**Expected**: within 5 seconds, temperatures/progress update live on the detail page, and the AMS
slot grid shows the simulated tray's material/color — no refresh needed (`PrinterTelemetryUpdated`
event, contracts/farm-hub.md).

## Scenario 6 — Camera graceful degradation (User Story 3)

Open the detail page for a printer whose fake connector is configured to never emit
`CameraFrameReceived`.

**Expected**: the rest of the page (status, telemetry) works normally; the camera area shows a clear
"unavailable" state rather than an error or blank/broken layout (FR-009).

## Physical hardware validation (manual, not CI — constitution Principle II)

Once `BambuLanConnector` is implemented (M0.5), validate against a real printer in LAN-only +
Developer Mode:

1. Register the real printer through the UI using its actual IP/serial/access code — confirm the
   verify-before-add gate genuinely rejects a wrong access code and accepts the correct one.
2. Start a print from the printer's own touchscreen (not through OpenFarm — FR-011 forbids dispatch
   this phase) and confirm the fleet list flips to `Printing` live.
3. Let the print finish; confirm `AwaitingClear`, then use "Mark ready" and confirm `Idle`.
4. Disconnect the printer from the network mid-print; confirm `isReachable` flips to `false` within
   ~30-40 seconds (FR-005) while the last-known `Status` value is preserved, not reset.
5. Open the detail page during an active print; confirm a live camera frame appears within 3 seconds
   (SC-005) — for whichever camera transport (`CAMERA_IMAGE` or `CAMERA_RTSP`) that specific printer
   model uses (research.md §4).

## Automated verification (run instead of manual curl once implemented)

```bash
dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~Printer"
cd web && bunx vitest run src/features/printers && bunx tsc -b
```
