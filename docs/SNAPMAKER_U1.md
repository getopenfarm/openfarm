# Snapmaker U1 over Moonraker

The U1 runs Snapmaker's forks of Klipper and Moonraker
([u1-klipper](https://github.com/Snapmaker/u1-klipper), [u1-moonraker](https://github.com/Snapmaker/u1-moonraker)).
OpenFarm talks to its Moonraker on the LAN, port 7125: plain HTTP for one-off requests and one websocket per
live connection (JSON-RPC 2.0). Nothing is installed on the printer. Printago, SimplyPrint and the Home Assistant
U1 integrations use the same API.

`SnapmakerU1Connector` implements the same `IPrinterConnector` as the Bambu connector and translates Moonraker into
the vocabulary the rest of OpenFarm already speaks. No Application or Domain code knows it is talking to Moonraker.

## Registering

| Field | U1 |
| --- | --- |
| IP address | The printer's LAN address (DHCP reservation recommended). |
| Serial number | **Not asked.** Read from `GET /machine/system_info` → `product_info.serial_number` when the printer is added (`IPrinterConnector.ReadSerialNumberAsync`). |
| API key | Optional. Moonraker trusts LAN clients by default; set it only when the printer's `[authorization]` requires one. Sent as `X-Api-Key` over HTTP and as `api_key` in `server.connection.identify` on the websocket. |

Verify succeeds when `product_info.machine_type` is `Snapmaker U1` and, for a registered printer, the serial
matches. Every reconnect re-checks the serial: Moonraker messages carry no serial, so a DHCP swap would otherwise
book another U1's telemetry on this printer. A mismatch logs a warning and keeps the printer offline until its IP
is edited.

## Discovery

The U1's Moonraker publishes the mDNS service `_snapmaker._tcp.local.` with `sn`, `machine_type`, `device_name` and
`ip` in its TXT record (u1-moonraker `components/zeroconf.py`; Snapmaker Orca's *Nearby* list browses the same
service). `SnapmakerMdnsDiscoveryService` lists every TXT whose `machine_type` the catalog knows as a Snapmaker model,
at the packet's source address. *Add printer* shows it next to the Bambu printers, prefilled with brand, model, IP and
SN. The SN goes with the add, so the connection check also confirms that the IP still belongs to the discovered U1.

An mDNS responder announces on boot or change and says goodbye on shutdown. Otherwise it only answers queries; it
does not repeat itself like Bambu SSDP. So the service:

- listens on UDP 5353, shared with mDNSResponder/avahi, for announcements, goodbyes and replies to anyone's query;
- queries only while someone reads the list: at once, then after 1, 2, 4 … 30 s; it stops 30 s after the last read,
  so there is no mDNS traffic while nobody adds a printer;
- hides a U1 that stays silent for 60 s of its queries, and drops one at its goodbye;
- falls back to one-shot queries from an ephemeral port when it cannot share 5353. The U1's python-zeroconf answers
  those by unicast (RFC 6762 §6.7). Browsing still works; announcements and goodbyes are missed.

The host must be on the printers' subnet: a bridged Docker container hears nothing (see `deploy/README.md`). Only the
default network interface is used.

## Status

The websocket subscribes (`printer.objects.subscribe`) to `print_stats`, `virtual_sdcard`, `display_status`,
`heater_bed`, `extruder`–`extruder3`, `toolhead`, `webhooks` and `print_task_config`, and queries
`server.exception.query`. Deltas (`notify_status_update`) are merged into one status document.

| Moonraker | OpenFarm |
| --- | --- |
| `print_stats.state` standby / printing / paused / complete / cancelled, error | `IDLE` / `RUNNING` / `PAUSE` / `FINISH` / `FAILED` |
| `print_stats.state` printing → standby with no terminal state | `FINISH` if the last progress was ≥ 99 %, else `FAILED` |
| `webhooks.state` shutdown/error (or Klippy disconnecting) during a print | `FAILED` |
| `display_status.progress` (M73), else `virtual_sdcard.progress` | Progress % |
| `print_duration` ÷ progress | Remaining minutes (0 when not printing) |
| `print_stats.info.current_layer` / `total_layer` | Layers |
| The head whose `state` is `ACTIVATE` | Active tray (255 when none) |
| Active head's temperature, else the hottest head | Nozzle temperature |
| `nozzle_diameter` on all four heads | Nozzle diameter — only when all four agree (mixed heads are unknown, so nothing dispatches) |
| `print_task_config` arrays (`filament_exist`, `filament_type`, `filament_sub_type`, `filament_color_rgba`) | One four-tray unit, tray = head (T1–T4) |
| Exceptions (`id`, `index`, `code`, `level`, `message`) | Alerts, source `Exception`, code `id:index:code` |

**States are sent once per change.** A state is reported on a connection's first snapshot and then only when it
changes — never re-sent on a telemetry tick or after a websocket reconnect. Re-sending `FINISH` would undo an
operator's Mark ready, because the U1 keeps reporting `complete` until the next print. State changes go out at
once and in order; telemetry is coalesced to at most one event a second, with one at least every 10 s.

**Toolheads.** The four heads appear as one Multi-Material Unit labelled *Toolheads*, slots *T1*–*T4*. A tray's
id is its head index, so a job's tray mapping is exactly the firmware's tool→head map. Its reload policy matches
the U1's *auto replenish* (another head with the same filament takes over); if auto replenish is off on the
printer, same-filament heads still count as backup stock in the enough-filament check.

**Not mapped yet:** per-head temperatures, RFID spool linking (`filament_detect.info[N].CARD_UID`), the
`machine_state_manager` stage (bed leveling, flow calibration, …).

## Dispatch

Production slices with upstream OrcaSlicer, which ships the `Snapmaker U1 (0.4 nozzle)` machine and its process and
filament presets; the catalog's `Snapmaker U1` model pulls the Snapmaker vendor in at the next profile ingestion. The
farm's slicer must be OrcaSlicer: Bambu Studio ships no Snapmaker presets, so a part pinned to it is blocked for a U1
and readiness says so.

`StartJobAsync`:

1. Unwraps `Metadata/plate_1.gcode` when the file is a sliced project (`.gcode.3mf`); plain G-code is sent as it is.
   The U1 prints plain G-code, and its own 3MF handling expects files Snapmaker's slicer made.
2. Uploads it with `POST /server/files/upload` (`root=gcodes`, `path=openfarm`, `checksum=<sha256>`), named like
   Bambu uploads but ending in `.gcode`.
3. Starts it over a short-lived websocket with `server.files.start_local_print {path, options: {map_table}}`
   (firmware 1.3+; the method is not offered over HTTP). `map_table` is `[[tool, head], …]` from the job's tray
   mapping — a tray id is a head index here — and the firmware resets its map to identity before applying it.
   With no mapping the identity map stays.
4. Fails the dispatch, which is reverted and retried later, when the answer's `state` isn't `success` (for example
   "Printer is busy, cannot start print"), when the method is missing (firmware older than 1.3), or after 60 s.

Pause, resume and stop are `POST /printer/print/pause|resume|cancel`. A print started from the touchscreen or
Snapmaker Orca is read back from `/server/files/gcodes/<print_stats.filename>` for filament accounting.

**Multi-colour.** A pre-sliced multi-colour file dispatches with a full `map_table`. A plain multi-colour 3MF is
sliced server-side with one filament per project slot (`docs/FILAMENT_PROFILE_ASSIGNMENTS.md`) — checked with the real
Orca 2.3.1 CLI on the U1 preset: each slot prints on its own tool.

**Pre-sliced files go only to their own model.** A file that needs no slicing must name the printer it was sliced
for (G-code `printer_model`, else the 3MF's `printer_model_id`) and that must be this printer's exact catalog model
— a Bambu file never reaches a U1, a U1 file never reaches a Bambu, and a P1S file doesn't go to an X1C. A file
naming no model is held. Files that still need slicing (STL, STEP, plain 3MF) go anywhere: they are sliced for the
printer that takes them. The simulator accepts every model.

## Camera

Stock firmware has no stream: the printer keeps a JPEG at `/server/files/camera/monitor.jpg` fresh only while a
client keeps the camera awake with the websocket call `camera.start_monitor {domain: "lan", interval: 0}`; it
idles a few seconds after the last one. `SnapmakerU1CameraRelay` wakes it every 8 s, polls the snapshot once a
second, and passes a frame on only when the bytes changed — about 1 fps, like Bambu's P1/A1 camera. A missing or
half-written snapshot is skipped. The JPEG check accepts any segment after the start marker (the U1's frames need
not be JFIF), so the last frame persists across restarts like a Bambu one. Paxx12's Extended Firmware adds a WebRTC
stream; OpenFarm doesn't use it.

## Hardware checklist

Run against a real U1 (firmware ≥ 1.3) before trusting the connector:

1. Add by IP with no API key; the SN fills in by itself. With `[authorization]` on, add fails without the key and
   succeeds with it.
2. The detail page shows status, bed and nozzle temperatures, and T1–T4 with the loaded material and colour; an
   empty head shows empty.
3. A finished print → *Awaiting clear*; Mark ready → stays *Idle* while the printer still reports `complete`.
4. Unplug the network → offline within 30 s; plug it back → online without a status flip.
5. Trigger an exception (e.g. filament runout) → one alert, no repeated alert activity entries.
6. Capture sanitized `printer.objects.subscribe` answers (idle, printing, paused, complete) and an exception
   list, and replace the synthetic fixtures in `api/tests/OpenFarm.Infrastructure.Tests/Printers/Fixtures/`.
7. Dispatch a single-colour part with its spool on T3: the U1 uploads `openfarm/<name>.gcode` and prints it on
   head 3; pause, resume and stop from the job page; a Bambu pre-sliced file shows held for the U1.
8. Start a print from the touchscreen: it is booked as an external print and its filament deducted at the end.
9. The camera card updates about once a second; after an API restart the last frame still shows.
10. Discovery: opening *Add printer* lists the U1 within one poll (~3 s) with its device name and SN. Reboot it with
   the page open: it comes back from its announcement. A clean power-off removes it. Select → Add with a blank API key
   registers it, and it then shows as *Added*. `dig @224.0.0.251 -p 5353 _snapmaker._tcp.local PTR` answers (the
   one-shot fallback works); save that reply as a fixture and confirm its TXT `sn` equals `product_info.serial_number`.
11. Open questions to settle with the captures: does PRINT_END reset `print_stats` to standby right after
   `complete`; what `extruder*.state` and `toolhead.extruder` report while every head is parked; what the
   exception `level` values mean (OpenFarm assumes ≥ 3 error, 2 warning, 1 information).
