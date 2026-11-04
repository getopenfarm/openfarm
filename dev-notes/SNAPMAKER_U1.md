# Snapmaker U1 over Moonraker

OpenFarm uses the U1's existing LAN Moonraker service on port **7125**. It installs nothing on the printer. Protocol tests establish request and recovery behavior, not acceptance on every firmware.

## Connection and ownership

HTTP uses optional `X-Api-Key`. WebSocket authentication uses an encoded `token` query parameter, followed by `server.connection.identify` identifying OpenFarm. Credentials must not appear in logs. Registration verifies `/printer/info` and checks `product_info.machine_type` and `serial_number` through `/machine/system_info`. Reconnect checks the saved serial before accepting telemetry.

`U1SessionRegistry` lets commands borrow the initialized monitoring socket. Commands wait for readiness during reconnect; they do not dispose the borrowed socket. A command without monitoring uses a temporary owned socket. Dispatch is serialized per printer; another printer has an independent gate. Notification queues belong to each socket generation and are never reused by its replacement.

RPC requests have a 30-second deadline. Pause, resume and cancel allow 120 seconds and accept either the successful RPC response or the matching observed state. Transport acknowledgement does not change persisted print state. Reconnect uses exponential delays from 5 seconds to 300 seconds. Quiet telemetry does not force reconnects; WebSocket keepalive detects lost transport.

The session owns polling and camera wake work, with its cancellation lifetime. `SnapmakerU1Connector.Session.cs` handles monitoring, while `U1FilamentState` and `U1ExceptionTracker` own their state. `MoonrakerStatusMapper` translates readings without accessing the network.

## Discovery

Discovery retains `_snapmaker._tcp.local.` mDNS on UDP 5353, using `sn`, `machine_type`, `device_name` and `ip` TXT records. Queries start while Add printer is browsing and back off to 30 seconds. The browser stops querying 30 seconds after the last list read. Shared-port listening supports announcements and goodbyes; ephemeral-port unicast queries are the fallback when port sharing fails.

Linux Docker deployments need host networking for mDNS discovery. Manual entry remains available. A DHCP address that now belongs to another serial is rejected.

## Monitoring

The connector requests `printer.objects.list` and subscribes only to available objects. Desired objects include print state, progress, bed and per-head temperatures/nozzles, active head, object exclusions, webhooks, RFID and feed sensors. Missing optional objects do not block connection.

| Source | OpenFarm behavior |
| --- | --- |
| `print_stats.state` standby / printing / paused / complete / cancelled or error | IDLE / RUNNING / PAUSE / FINISH / FAILED |
| Printing or paused directly to standby | FAILED; progress alone does not establish completion |
| Klippy shutdown/disconnection during printing | FAILED |
| `notify_history_changed` finished for the current filename | Apply the completed/cancelled/error terminal mapping; ignore unrelated filenames |
| Positive `display_status.progress` | Preferred progress |
| Printable-byte bounds from file metadata | Progress fallback before virtual SD progress |
| Metadata `estimated_time`, else elapsed/progress | Remaining-time estimate |
| Per-head state `ACTIVATE`, else selected extruder on firmware without head states | Active feed slot |

State tracking survives reconnects. A repeated FINISH cannot undo Mark ready. Printer observations have their actual timestamp; override refreshes and cached publications do not invent fresh status or nozzle observations. Camera wakes, transfer progress and command acknowledgements do not count as printer telemetry. The shared reachability policy remains 90 seconds.

Metadata is fetched when the current filename changes and cleared when it clears. Failed optional operations do not discard the last valid status.

## Filament and exceptions

Subscribe to `filament_detect`, `filament_feed left` and `filament_feed right`. Merge RFID entries by channel and field. Feed sensors determine physical presence; RFID supplies descriptions, color and `CARD_UID`. Normalize material aliases and colors for the shared telemetry model. Map channels 0–3 to the existing Toolheads unit, slots T1–T4, with matching extruder indices. Do not invent remaining percentage or tag weight.

Fetch `/server/files/config/snapmaker/print_task.json` immediately and every 3 seconds, using `If-Modified-Since` where available. Matching file-list changes trigger refresh. A 304 keeps overrides, a 404 clears them, and transient failures keep prior overrides. A delayed re-emission at 5.5 seconds handles late readings. Temporary description loss while loaded keeps the previous description; confirmed unload clears that hold.

Query active exceptions and process both `notify_exception_status` and `notify_exception_notification`. Codes use `U1-<id:0000>-<index:0000>-<code:0000>`. Level 2 or higher is Error, lower levels are Warning, and missing level defaults to 3. Reject non-finite identifiers. Retain ten one-shot notifications, deduplicate codes and clear one-shots on transitions to printing or standby. Historical alert codes are not rewritten.

## Dispatch

1. Validate the tool-to-head mapping against heads 0–3. Preserve empty tool positions.
2. Extract the selected `Metadata/plate_<n>.gcode` from a sliced project, or use bare G-code unchanged. Preserve exact-model and nozzle eligibility checks.
3. Send `SET_PRINT_EXTRUDER_MAP CONFIG_EXTRUDER=<tool> MAP_EXTRUDER=<head>` for mapped tools, then `SET_PRINT_USED_EXTRUDERS EXTRUDERS=<heads>`. An empty mapping sends no macros. Routing failure prevents start.
4. Derive a stable filename from the prepared G-code SHA-256 and job name. Check `server.files.metadata` for an existing remote file. Upload missing files through multipart `/server/files/upload` using the file field.
5. Call `printer.print.start` with `filename`. Do not additionally rewrite T commands.

Uploads have an inactivity deadline that renews after successful chunk writes and bounds the final response wait. Successful slow transfers are not limited to a five-minute total duration. Upload success does not imply a successful start.

An explicit RPC rejection is a definite failure. A lost reply after requesting start is uncertain: the shared `PrinterStartUnconfirmedException` blocks automatic resends, including after application restart. Printer telemetry settles the outcome. Pre-start failures retain the existing dispatch retry policy.

Pause, resume and cancel use `printer.print.pause`, `printer.print.resume` and `printer.print.cancel`. Live object skipping retains validated `EXCLUDE_OBJECT NAME=...` commands.

## Camera

The monitoring session sends `camera.start_monitor({domain:"lan",interval:0})` immediately and every 2 seconds, including without an open viewer. The camera relay fetches `/server/files/camera/monitor.jpg` on port **80** once per second. It validates complete JPEGs and emits changed frames only. Camera failures do not terminate printer monitoring.

## Verification

The loopback Moonraker harness verifies protocol behavior, not firmware. Run the printer regression suite and production dispatch tests. Record physical U1 acceptance separately: firmware, optional API key, a non-identity multi-tool mapping, pause/resume/cancel, RFID/non-RFID spools, exceptions, camera continuity, and network loss during upload/start. No physical U1 acceptance is implied by automated results.
