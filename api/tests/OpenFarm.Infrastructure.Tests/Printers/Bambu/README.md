# Bambu connectivity tests

Run without a printer or Docker:

```bash
dotnet test api/tests/OpenFarm.Infrastructure.Tests/OpenFarm.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Printers"
```

The built-in **System / Simulation** printer remains the tool for UI demos and simulated job
lifecycles. These tests exercise the real Bambu connector instead:

- Unit tests check MQTT options, certificate trust, JSON parsing, frame encoding, and upload failures.
- `BambuMqttHarness` is a small MQTT 3.1.1 wire peer supporting the connector's CONNECT, SUBSCRIBE,
  QoS-0/1 PUBLISH, PUBACK, PING, and DISCONNECT packets. It verifies credentials and report/request topics,
  scripts version/status replies, replays fixtures, and can drop connections. It is not a general
  broker or firmware emulator.
- `LoopbackTlsServer` serves MQTT and camera sessions using ephemeral loopback ports and a generated
  certificate. Only explicit test options trust that certificate. Production constructors retain
  the bundled Bambu trust, LAN ports, client factory, uploader, and timeout defaults. MQTT tries
  strict certificate validation first and uses a session-scoped compatibility fallback only for
  recognized chain failures. Tests separately cover this fallback; custom test trust
  does not replace the production trust store. Camera certificate validation remains strict.
- Camera tests fragment frames across TLS writes, send invalid or truncated data, and verify
  reconnection and cancellation. Their JPEG bytes are synthetic framing fixtures, not captured
  P1S camera images.
- The background-service protocol test uses the real MQTT connector with in-memory repositories
  to check silence/recovery, status preservation, and merging partial reports. Existing HTTP/DB
  integration tests separately cover application behavior through the built-in simulator.

- `BambuFtpsHarness` exercises real FluentFTP uploads over encrypted passive data connections.
  It covers PASV `0.0.0.0`, STOR before data TLS completion, exact SIZE checks without directory
  listings, missing final responses, retry limits, storage errors, cancellation, and per-printer
  upload serialization. It does not require or prove TLS-session resumption.

Sessions and listeners are owned and disposed by each test, with bounded waits and no physical
printer dependency. No new server package is needed. Printer-specific TLS-session resumption,
simultaneous-client tolerance, camera behavior, and firmware command acceptance require the [P1S hardware checklist](../../../../../specs/002-printer-integration/quickstart.md#physical-hardware-validation-manual-not-ci--constitution-principle-ii).
Record firmware/versioned, sanitized P1S captures when hardware is available; the existing replay
corpus is upstream A1/P1P/X1C data and does not establish P1S compatibility.


## P1S acceptance after the connectivity refactor

Record firmware, prepared artifact size, transfer phase logs, and start-confirmation telemetry.
Use the artifact from the failing send and complete three consecutive sends with confirmed starts.
Interrupt one upload, then retry and confirm the exact-size check recovers the same remote file
without a duplicate start. The sender uses root `openfarm-*.gcode.3mf` files and
`ftp://filename` project URLs. Existing `/cache` downloads remain supported.

Telemetry silence no longer triggers MQTT nudges or forced reconnects. The existing 90-second
reachability policy still applies; successful upload activity does not manufacture printer reports.
A broker PUBACK confirms receipt only, while printer telemetry confirms that printing started.
Hardware acceptance remains pending until these checks are performed on the affected printer.
