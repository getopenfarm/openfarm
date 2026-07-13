# Phase 0 Research: Phase 1 — Fleet Management + Live Bambu Integration

All protocol-level findings below are drawn from directly reading the primary reference source at
`third_party/printers/bambulab/references/ha-bambulab/custom_components/bambu_lab/pybambu/` (the
`pybambu` reverse-engineered Bambu Lab LAN client vendored inside the `ha-bambulab` Home Assistant
integration), populated locally via `git submodule update --init --recursive` as the first step of
this planning pass. Citations point at real file paths and, where useful, function/class names and
approximate line numbers in that source. The curated summaries at
`third_party/printers/bambulab/docs/ha-bambulab/**/*.md` are used only for cross-checking and framing
— every claim below was independently verified against the primary source, not copied from the docs.

## 1. MQTT topic/message schema, TLS handshake, full-vs-delta behavior

**Decision**: Use MQTT topics `device/{serial}/report` (subscribe) and `device/{serial}/request`
(publish), MQTT 3.1.1, TLS on port 8883, MQTT username `bblp` / password = the printer's LAN access
code, keepalive 5s, QoS 0 (fire-and-forget, no delivery confirmation). On every successful connect,
publish `GET_VERSION` then `PUSH_ALL` to force a complete state snapshot rather than waiting on the
printer's own push cadence.

**Rationale**: Directly confirmed in `pybambu/bambu_client.py`:
- `subscribe()` (line 636-639): `self.client.subscribe(f"device/{self._serial}/report")`.
- `publish()` (line 641-650): publishes to `f"device/{self._serial}/request"`, QoS 0 (no `qos=`
  argument passed to `publish`), success/failure inferred only from `MQTTMessageInfo.rc`.
- `__init__` (line 386): `self._port = 8883` (unconditional, both local and cloud modes).
- `connect()` (line 457-487) / `setup_tls()` (line 449-455): local mode calls
  `self.client.username_pw_set("bblp", password=self._access_code)` (line 478) — `"bblp"` is a
  hardcoded literal, not derived from config.
- `MqttThread.run()` (line 229-292): `self._client.client.connect(host, self._client._port,
  keepalive=5)` — 5-second MQTT keepalive.
- `subscribe_and_request_info()` (line 489-492), called from `_on_connect()`: `subscribe()` then
  `publish(GET_VERSION)` then `publish(PUSH_ALL)`.
- `commands.py` lines 24-32: `GET_VERSION = {"info": {"sequence_id": "0", "command":
  "get_version"}}`, `PUSH_ALL = {"pushing": {"sequence_id": "0", "command": "pushall"}}`,
  `START_PUSH = {"pushing": {"sequence_id": "0", "command": "start"}}`.

**TLS certificate handling**: Bambu printers present a device-specific certificate, not one from a
public CA. `create_local_ssl_context()` (`bambu_client.py` lines 827-845) builds one combined trust
store by loading **every** `*.cert` file bundled in `pybambu/certs/` (`bambu.cert`,
`bambu_h2c_251122.cert`, `bambu_p2s_250626.cert`, `bambu_x2c_260425.cert`) via
`context.load_verify_locations()` — there is no per-model cert selection logic; whichever bundled CA
matches the specific printer's cert is what validates the handshake. It also clears
`ssl.VERIFY_X509_STRICT` (tolerates certs missing a Key Usage extension) and sets
`check_hostname = False` (printers are addressed by LAN IP, not a name matching the cert). A fully
insecure fallback (`create_insecure_ssl_context()`, lines 847-852: `verify_mode = ssl.CERT_NONE`) is
available but opt-in only, for printers whose cert isn't recognized by the bundled trust store.
**Implication for `BambuLanConnector`**: MQTTnet needs an equivalent custom `TlsParameters`
certificate-validation callback that accepts Bambu's per-device certs; a pinned/bundled CA list is
the safer default, with an explicit "skip verification" opt-out surfaced to the operator only as a
documented fallback, not the default.

**Full vs. delta**: The printer pushes incremental telemetry by default; `PUSH_ALL`/`pushall`
explicitly requests one complete state object (see `connection.md` §9, verified against
`bambu_client.py`'s `refresh()` at line 652-659 and the `subscribe_and_request_info()` call on every
reconnect). `on_message` (line 585-634) dispatches by top-level JSON key (`print`, `info`, `system`)
and calls `self._device.print_update(data=...)`, which per the curated doc's own citation performs
merge-into-existing-state semantics (fields not present in a given delta message are left unchanged).
**Implication**: `BambuMqttMessageParser`/the domain telemetry model must merge incoming deltas into
existing per-printer state rather than replacing it wholesale, and must publish `pushall` immediately
after every fresh connect (matching `subscribe_and_request_info()`) so the first telemetry snapshot is
always complete.

**Alternatives considered**: None — this is observed protocol behavior, not a design choice.

## 2. State-machine transition table

**Decision**: Map `gcode_state` (the top-level print-status string on the `print` MQTT payload) to
OpenFarm's `PrinterStatus` as follows:

| `gcode_state` (pybambu) | OpenFarm `PrinterStatus` |
|---|---|
| `idle` | `Idle` |
| `init`, `prepare`, `slicing`, `running`, `pause` | `Printing` |
| `finish`, `failed` | `AwaitingClear` |
| `offline` | *(not mapped — reachability is tracked separately, see §6; this value overlaps with connection state in the wire protocol itself, which is exactly why OpenFarm's explicit separation of `PrinterStatus` from reachability is a deliberate improvement, not a mirroring of the source)* |
| `unknown` | initial/no-data state, not a reachable `PrinterStatus` value |

This confirms the spec's requirement that finish/fail/cancel all land in `AwaitingClear` (edge case in
spec.md, FR-003/FR-004).

**Rationale**: `pybambu/const.py` line 173-184 defines the complete enum:
```python
GCODE_STATE_OPTIONS = ["failed", "finish", "idle", "init", "offline", "pause", "prepare", "running", "slicing", "unknown"]
```
`models.py`'s `PrintJob.print_update()` (around line 1012-1018) reads `data.get("gcode_state", ...)`
and falls back to `"unknown"` if the reported value isn't in `GCODE_STATE_OPTIONS`. There is also a
separate, much finer-grained numeric `stg_cur` field (`CURRENT_STAGE_IDS` dict, `const.py` lines
85-169, ~78 named sub-stages like `heatbed_preheating`, `paused_filament_runout`,
`inspecting_first_layer`) — this is sub-stage detail *within* an active print (useful as an optional
"current activity" telemetry string on the detail page) and is **not** part of the `Idle`/`Printing`/
`AwaitingClear` state machine itself; do not conflate it with `gcode_state`.

**Legal manual transitions** (`TransitionTo`, strict): `Idle → Printing` is not manually triggerable
this phase (FR-011: no print dispatch); the only manual transition this phase actually needs is
`AwaitingClear → Idle` ("Mark ready", per spec User Story 4). Any other manual transition attempt
(e.g. `Idle → AwaitingClear` directly) is illegal and rejected per FR-003/edge cases.

**Lenient reconciliation** (`ObserveStatus`): every `gcode_state` change reported by the connector
reconciles regardless of the current manual-transition rules (FR-004), logged via
`IActivityLogWriter.WriteAsync` with `ActorType.System` per `plan.md`'s Security & Multi-Tenancy
decision.

**Alternatives considered**: Mapping `pause` to `AwaitingClear` instead of `Printing` was considered
and rejected — a paused print is still an active job requiring the operator's attention as "printing,"
not one that's finished and awaiting a physical clear action; conflating the two would misrepresent
FR-003's Idle/Printing/AwaitingClear semantics.

## 3. AMS slot schema

**Decision**: Model AMS state as a list of AMS units, each with up to 4 trays (slots), stored as a
JSONB column on `Printers` (not historical — reflects only the latest reported state, per spec's Key
Entities). Per-tray fields: `empty` (bool), `type` (material, e.g. `"PLA"`/`"ABS"`), `color` (hex
`RRGGBBAA`), `subBrands`, `remain` (percentage, `-1` if unknown/not calibrated), `trayUuid`, `active`
(bool — currently feeding the nozzle).

**Rationale**: `pybambu/models.py`:
- `AMSTray` (line 2865-2951+): fields `empty`, `state`, `idx`, `name`, `type`, `sub_brands`, `color`
  (`"00000000"` default, `RRGGBBAA` per the inline comment), `nozzle_temp_min/max`, `_remain`
  (`-1` default, exposed via a `remain` property), `k` (filament k-factor), `tag_uid`, `tray_uuid`,
  `tray_weight`, `_active`/`active` property, `dry_temp`, `dry_time`, `bed_temp`. An `unknown`
  property (line 2911-2914) is true when a spool is loaded but vendor/material/color are unassigned.
- `AMSInstance` (line 2565-2598): each AMS unit has `model`, `index`, and a `tray` list — 4 slots
  (`self.tray = [None] * 4`) for a standard AMS unit, or a single-entry list when `index >= 128`
  (external/single spool holder, not a real 4-slot AMS unit).
- `AMSList` (line 2602+): `data: dict[int, AMSInstance]` — a printer can have multiple AMS units
  (multi-AMS setups), keyed by unit index; `active_ams_index`/`active_tray_index` properties resolve
  which tray is currently feeding the active nozzle (relevant for the dual-nozzle H2-family models,
  out of this phase's X1/P1/A1 scope but a reason the schema is per-nozzle-aware upstream).

**Implication**: OpenFarm's data model only needs single-nozzle AMS handling for X1/P1/A1-series this
phase (per spec Assumptions), but the JSONB shape should still be "list of AMS units, each with a
tray-index list" rather than a flat 4-slot assumption, since some printers in this same family range
(and any future brand) may have zero or multiple AMS units attached (or none at all — a printer with
no AMS reports an empty/absent `ams` section).

**Alternatives considered**: A normalized `AmsSlot` child table was considered and rejected —
telemetry (including AMS contents) is explicitly non-historical per spec's Key Entities, so a JSONB
column matches the actual access pattern (always read/replace the whole current snapshot, never
queried slot-by-slot historically) with far less schema overhead than a child table.

## 4. Camera protocol (the genuinely open question)

**Decision**: Bambu Lab printers use **two mutually exclusive** camera transports, selected per
model, both of which OpenFarm's backend must support to honor the spec's stated X1/P1/A1-series scope:

| Feature flag | Models (this phase's scope) | Transport |
|---|---|---|
| `CAMERA_RTSP` | X1-family (`X1`, `X1C`, `X1E`) | RTSPS (RTSP-over-TLS) on port 322, URL supplied by the printer itself over MQTT |
| `CAMERA_IMAGE` | P1-family (`P1P`, `P1S`), A1-family (`A1`, `A1MINI`) | Proprietary binary JPEG-over-TLS push on TCP port 6000 |

**Recommendation**: Implement `CAMERA_IMAGE` (binary JPEG push) first — it is self-contained, requires
no external media framework, and covers the P1/A1 printers most likely to be the first hardware
connected. Build `BambuCameraRelay` as a plain `TcpClient` + `SslStream` reader in
`OpenFarm.Infrastructure`, mirroring `ChamberImageThread`'s exact framing (see below), and relay
decoded JPEG frames to the browser over the existing `/hubs/farm` SignalR channel as a
`CameraFrameReceived(printerId, base64Jpeg)` event — this reuses the real-time infrastructure already
being built for status/telemetry rather than standing up a second HTTP MJPEG-streaming endpoint, and
gives the frontend one consistent "camera frame arrives via SignalR" contract regardless of which
transport produced it server-side.

For `CAMERA_RTSP` (X1-family), recommend a small server-side FFmpeg subprocess per active X1 camera
view that samples the `rtsps://` stream and re-encodes it to periodic JPEG frames fed into the same
`CameraFrameReceived` SignalR event — this avoids needing a second, RTSP-aware frontend video
component and keeps the browser-facing contract identical for both printer families. This is a real
new dependency (an `ffmpeg` binary/process) scoped to X1-class cameras only; if it proves too heavy for
this phase's demo bar, the P1/A1 `CAMERA_IMAGE` path alone satisfies SC-005 for at least one printer
family, and X1 RTSP support can be treated as a fast-follow within the same phase without any schema
change (the `CameraFrameReceived` contract doesn't change, only which server component produces the
frames). **No RTSP-decoding NuGet library is needed** — FFmpeg-as-subprocess (or, if avoiding a new
process dependency is preferred, deferring live X1 camera support to a fast-follow within this phase)
are the two realistic choices; a native .NET RTSP client library was considered and rejected as
unnecessary complexity when `pybambu` itself doesn't implement one either (it hands the URL to
FFmpeg/GStreamer via Home Assistant's own camera platform, not a custom RTSP parser).

**Rationale — binary `CAMERA_IMAGE` protocol** (`ChamberImageThread`, `bambu_client.py` lines 76-216):
- Connects to `{printer_ip}:6000` via plain TCP wrapped in TLS (`local_tls_context`, same trust store
  as MQTT), non-blocking after the handshake.
- Writes an 80-byte auth blob before reading anything: 4× little-endian `uint32` (`0x40`, `0x3000`,
  `0`, `0`) + `"bblp"` NUL-padded to 32 bytes + the LAN access code NUL-padded to 32 bytes (lines
  99-110). No ACK is read for auth — success/failure is inferred from subsequent read behavior.
- Each frame: a 16-byte header delivered as its own discrete read (bytes `[0:3]` little-endian =
  payload size — note the source only reads 3 of the 4 size bytes, capping representable size at
  `0xFFFFFF`, line 190), followed by the JPEG payload itself, validated against magic bytes
  `FF D8 FF E0` (start, `jpeg_start`, line 114) and `FF D9` (end, `jpeg_end`, line 115). A zero-length
  read means "connection rejected — wrong access code or IP" (line 192-195, raises `RuntimeError`).
  Up to 12 reconnect attempts (`MAX_CONNECT_ATTEMPTS`, line 96) with backoff on failure.

**Rationale — RTSP protocol**: `pybambu` implements **no RTSP client at all**. The printer reports its
own `rtsps://` URL inside the regular MQTT telemetry payload's `print.ipcam.rtsp_url` field (e.g.
`"rtsps://192.168.1.64/streaming/live/1"`, per `docs/ha-bambulab/camera-and-ftp/camera-protocol.md`
§3.1, cross-checked against `models.py`'s `Camera.print_update()` reading
`data.get("ipcam", {}).get("rtsp_url", ...)` around line 459). The Home Assistant integration layer
(`camera.py`, outside `pybambu` proper) rewrites this URL to inject the `bblp:{access_code}` userinfo
and defaults the port to `322` when absent from the printer-reported URL, then hands the fully-resolved
URL to HA's own `ffmpeg`/GStreamer-backed stream platform — it does not parse RTSP itself. A value of
`"disable"` in `rtsp_url` means the printer's live view is currently off (not an error state).
`Features.CAMERA_RTSP` vs. `Features.CAMERA_IMAGE` are mutually exclusive, computed in
`models.py`'s `PrinterInfo.supports_feature()` (line 157-177) against disjoint model sets from
`const.py`'s `Printers` enum.

**Alternatives considered**: A unified WebRTC gateway (e.g. mediasoup/Pion) was considered and
rejected as significant infrastructure for a P2-priority feature whose spec success criterion (SC-005)
only requires *a* live feed within 3s with graceful degradation, not a specific streaming technology;
MJPEG-over-SignalR/HTTP is materially simpler to build and debug and matches what a browser can render
natively via periodic `<img>` updates with zero additional frontend dependencies.

## 5. MQTT library

**Decision**: MQTTnet, confirmed (not re-litigated) — `docs/FEATURE_PLAN.md` already names it as the
intended choice, citing `Bambu.NET`'s MQTTnet-based `BambuMQTTClient` as the closest existing .NET
prior art for this exact protocol. MQTTnet supports MQTT 3.1.1/5, TLS with custom certificate
validation callbacks (needed per §1's finding), and async I/O throughout — no blocking-thread-per-
connection design, satisfying SC-006's ~20-concurrent-printer constraint.

**Alternatives considered**: None explored beyond confirming MQTTnet — no reason to deviate from
established .NET prior art for a well-understood protocol.

## 6. ~30s unreachable timeout + reconciliation mechanics

**Decision**: `PrinterConnectionBackgroundService` (per-printer) records `LastMessageUtc` on every
inbound MQTT message (any successfully parsed `device/{serial}/report` payload). A periodic sweep
(every 5s) marks a printer unreachable if `now - LastMessageUtc > 30s` (per FR-005's ~30s threshold —
a deliberate OpenFarm product choice, distinct from `pybambu`'s own 60-second `WatchdogThread` timer,
see below) and flips it back to reachable on the next successfully parsed message. On MQTT reconnect,
if the freshly reported `gcode_state` differs from the printer's last-known `PrinterStatus`, call the
lenient `ObserveStatus` reconciliation path (§2) and log the discrepancy via `IActivityLogWriter`
directly (never silently overwrite without a log entry, per FR-010/spec edge cases).

**Rationale for the sweep-based design over a single fixed timer**: `pybambu` itself uses exactly this
pattern — a background `WatchdogThread` (`bambu_client.py` lines 37-73) tracks
`self._last_received_data`, checked in a loop against a fixed `WATCHDOG_TIMER = 60` (seconds), firing
once (latched via `_watchdog_fired` to avoid repeat firing) and resetting when fresh data arrives
again. OpenFarm's chosen threshold (~30s, per FR-005) is intentionally tighter than `pybambu`'s 60s —
this is a product requirement, not a protocol constraint, and the two are independent design choices
that happen to use the same "last message timestamp + periodic check" mechanism. Also note MQTT's own
keepalive is a much tighter 5 seconds (`MqttThread.run()`, `client.connect(..., keepalive=5)`) — that
governs the MQTT transport layer's own liveness (paho/MQTTnet's internal PINGREQ/PINGRESP), separate
from and faster than the 30s application-level "printer considered unreachable" threshold OpenFarm
exposes to the operator.

**Alternatives considered**: A single `Task.Delay`-based timer reset on every message (rather than a
periodic sweep) was considered; rejected because a sweep interval decoupled from message arrival is
simpler to reason about across many concurrent printer connections and avoids per-message timer
churn under SC-006's ~20-printer concurrency target.

## 7. Verify-before-add/edit semantics (FR-001a, FR-002)

**Decision**: "Verify" performs a short-lived MQTT connect + subscribe + `GET_VERSION`+`PUSH_ALL`
request against the entered IP/access-code, waiting up to 5 seconds for both a successful `get_version`
response and a first full `print` status message, then immediately disconnects without persisting
anything. Run both client-side (to progressively enable/disable the add/edit form's submit action) via
`VerifyPrinterConnectionEndpoint`, and server-side again as the final guard inside
`RegisterPrinterCommand`/`EditPrinterConnectionCommand` before persisting — never trust only the
client's earlier verification result, since the printer's reachability can change between the two
calls.

**Rationale**: `pybambu/bambu_client.py`'s `try_connection()` (line 706-809) implements exactly this
pattern already, confirming it as sound, well-trodden design: connects with a dedicated MQTT client,
subscribes and requests `get_version`/`pushall`, and waits on a `queue.Queue` result populated only
once **both** `received_info` (a `get_version` response) and `received_push` (a `print`/`push_status`
message with `msg == 0`, i.e. a full dump) are true — with `result.get(timeout=10)` (10-second overall
timeout) and disconnect always run in a `finally` block regardless of outcome. OpenFarm's design
mirrors this two-signal completeness check (don't declare "verified" on a partial/malformed first
message) but uses a tighter 5s timeout, since verify-before-save is a synchronous UI-blocking gate
(SC-001 requires end-to-end registration in under 2 minutes) rather than a background HA config-flow
step where a longer wait is more tolerable.

**Alternatives considered**: A lighter TCP-connect-only check (no MQTT subscribe/handshake) was
considered and rejected — it would not actually validate the LAN access code, which is exactly the
failure mode FR-001a/edge cases call out ("incorrect LAN access code" must be caught before the
printer is added), so a real MQTT auth handshake is required, not just network reachability.
