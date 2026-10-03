# Fleet management and Bambu connectivity validation

There are three complementary validation paths:

- **UI and lifecycle development:** register a **System / Simulation** printer through the normal
  Add Printer form. The built-in simulator verifies any access code except the test sentinel
  `wrong`, supplies telemetry and camera frames, and runs simulated print jobs. Follow the
  [virtual printer quickstart](../004-virtual-printer/quickstart.md) for detailed scenarios.
- **Automated transport regression:** run the Infrastructure printer tests below. The Bambu
  loopback harness exercises the real connector over local TLS sockets without a printer or Docker.
  It is test-only and is not a standalone printer that the development app can register.
- **P1S compatibility:** follow the manual hardware checklist. Registering a **Bambu / P1S** printer
  uses its real LAN connection, even in development.

## Local app setup

Run `./dev.sh up` with the local Postgres configured, or use the Docker stack:

```bash
cp .env.example infra/.env
cd infra && docker compose up
```

Log in as the seeded operator. For hardware-free testing, choose **System / Simulation**, provide a
unique serial number, a valid IP address, and an access code other than `wrong`. Confirm registration,
live idle telemetry, the camera image, job progression, and mark-ready behavior. The simulator does
not free-run prints: dispatch a job through the normal production workflow or use the existing
simulated lifecycle integration tests. No fake-connector control endpoint exists.

## Physical hardware validation (manual, not CI — constitution Principle II)

Use this checklist when a P1S is available and after changes to connectivity or printer firmware.
The loopback protocol harness verifies OpenFarm behavior, not firmware compatibility.

1. Put the printer in the LAN/developer mode required by its installed firmware. Record the P1S
   firmware version, OpenFarm revision, date, and whether AMS is attached. Use its actual IP,
   serial number, and LAN access code when registering it as **Bambu / P1S**. Verify a wrong code
   is rejected and the correct code succeeds. Connector selection is per printer: **System /
   Simulation** uses the built-in simulator; **Bambu / P1S** uses the real connector. There is no
   global `Printers__UseFakeConnector` switch.
2. Confirm idle telemetry, temperatures, and AMS/external-spool detection where applicable. Start a
   print at the printer and confirm the fleet becomes `Printing`; confirm live progress and camera
   frames on the detail page. Record observed camera startup latency.
3. Dispatch a small, known-good pre-sliced single-plate 3MF through OpenFarm. Confirm the actual
   implicit-TLS FTPS upload succeeds and the printer starts the intended plate. This validates the
   `/cache` upload path, `ftp:///` command URL, checksum, and `Metadata/plate_1.gcode` selection.
   Automated tests substitute the uploader and do not validate the printer's FTPS server.
4. While the long-lived telemetry connection is active, request pause, resume, and stop through
   OpenFarm. Confirm each action on the physical printer and in subsequently reported telemetry;
   a successful command publish alone does not confirm an action. Check that issuing commands does
   not disrupt telemetry. Also let a print finish, confirm `AwaitingClear`, then mark ready and
   confirm the printer remains `Idle`.
5. Disconnect the printer from the network during a print. Confirm it becomes unreachable within
   roughly 30–40 seconds while preserving its last-known print status. Restore the network and
   confirm telemetry and camera recover without restarting OpenFarm.
6. Save sanitized P1S report/version captures for idle, printing, pause, finish, and AMS configurations
   tested. Remove identifying fields and secrets; keep field shapes intact. Add fixture provenance
   (model, firmware, capture date, sanitization) to `Printers/Fixtures/README.md` and replay tests.

Record **pass / fail / not tested** for each step, observed failures, and logs without access codes.
Do not label P1S connectivity validated until these checks have actually been performed.

**Historical evidence:** the earlier A1 connection/verification check found TLS trust-store and MQTT
protocol-version bugs, now covered by regression tests. That check is not a complete P1S validation;
no new physical-printer results are claimed by the protocol-harness implementation.

## Automated verification

Transport and background-service tests need no hardware or Docker:

```bash
dotnet test api/tests/OpenFarm.Infrastructure.Tests/OpenFarm.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Printers"
```

The [harness notes](../../api/tests/OpenFarm.Infrastructure.Tests/Printers/Bambu/README.md) describe
the supported wire behavior and limits. HTTP/database integration tests still require Docker
(Testcontainers PostgreSQL):

```bash
dotnet test api/tests/OpenFarm.Api.IntegrationTests/OpenFarm.Api.IntegrationTests.csproj --filter "FullyQualifiedName~Printers"
cd web && bunx vitest run src/features/printers && bunx tsc -b
```
