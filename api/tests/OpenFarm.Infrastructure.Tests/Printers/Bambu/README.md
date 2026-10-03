# Bambu connectivity tests

Run without a printer or Docker:

```bash
dotnet test api/tests/OpenFarm.Infrastructure.Tests/OpenFarm.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Printers"
```

The built-in **System / Simulation** printer remains the tool for UI demos and simulated job
lifecycles. These tests exercise the real Bambu connector instead:

- Unit tests check MQTT options, certificate trust, JSON parsing, frame encoding, and upload failures.
- `BambuMqttHarness` is a small MQTT 3.1.1 wire peer supporting the connector's CONNECT, SUBSCRIBE,
  QoS-0 PUBLISH, PING, and DISCONNECT packets. It verifies credentials and report/request topics,
  scripts version/status replies, replays fixtures, and can drop connections. It is not a general
  broker or firmware emulator.
- `LoopbackTlsServer` serves MQTT and camera sessions using ephemeral loopback ports and a generated
  certificate. Only explicit test options trust that certificate. Production constructors retain
  the bundled Bambu trust, LAN ports, client factory, uploader, and timeout defaults. No simulator
  certificate or configurable trust bypass is installed in the app.
- Camera tests fragment frames across TLS writes, send invalid or truncated data, and verify
  reconnection and cancellation. Their JPEG bytes are synthetic framing fixtures, not captured
  P1S camera images.
- The background-service protocol test uses the real MQTT connector with in-memory repositories
  to check silence/recovery, status preservation, and merging partial reports. Existing HTTP/DB
  integration tests separately cover application behavior through the built-in simulator.

Sessions and listeners are owned and disposed by each test, with bounded waits and no physical
printer dependency. No new server package is needed. FTPS upload orchestration substitutes an
uploader; actual FTPS transfer, simultaneous-client tolerance, camera behavior, and firmware command
acceptance require the [P1S hardware checklist](../../../../../specs/002-printer-integration/quickstart.md#physical-hardware-validation-manual-not-ci--constitution-principle-ii).
Record firmware/versioned, sanitized P1S captures when hardware is available; the existing replay
corpus is upstream A1/P1P/X1C data and does not establish P1S compatibility.
