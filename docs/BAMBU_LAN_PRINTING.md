# Bambu LAN print files and paths

OpenFarm uploads a readable, timestamped project to `/cache/<filename>.gcode.3mf` over
implicit FTPS, then publishes `print.project_file`. The MQTT URL must refer to
that same cache file, not to a basename at the SD-card root.

| Registered model | MQTT project URL |
| --- | --- |
| P1P, P1S, A1, A1MINI | `file:///sdcard/cache/<filename>.gcode.3mf` |
| X1, X1C, X1E | `ftp:///cache/<filename>.gcode.3mf` |

The P1/A1 convention follows the implementation in
[bambu-printer-manager](https://raw.githubusercontent.com/synman/bambu-printer-manager/devel/src/bpm/bambuprinter.py).
[OpenBambuAPI](https://github.com/Doridian/OpenBambuAPI/blob/main/mqtt.md#printproject_file)
also documents a cache-qualified FTP URL and the internal plate path.

`param` is `Metadata/plate_1.gcode`, a ZIP entry inside the uploaded project.
Renaming plain G-code to `.3mf` does not create that entry. OpenFarm preserves
existing sliced archives, including their settings and thumbnails. For bare
G-code it packages the original bytes at that entry and adds the per-plate MD5
sidecar, following the archive approach in the
[bambulabs_api print example](https://bambutools.github.io/bambulabs_api/examples.html#start-a-print).
The MQTT MD5 is calculated from the final uploaded archive. Empty files,
invalid archives, and archives missing the requested plate are rejected before
upload or print-start publication. Production OrcaSlicer/Bambu Studio output
retains its original sliced archive and reports its output format to the job
file resolver.

Production filenames follow `<part>_<material>_<duration>_<UTC timestamp>.gcode.3mf`,
for example `Bracket_PLA_1h20m_20261007-203005123.gcode.3mf`. Unknown material
or duration is omitted; re-sliced jobs use their new slice's duration, while
older stored revisions omit the current revision's estimate. Multiple material
types are included once each. The readable stem is limited to 100 ASCII letters,
digits, underscores and hyphens; original model extensions and unsafe characters
are removed. A UTC millisecond timestamp replaces the random UUID, with concurrent
uploads receiving distinct timestamps within the API process.

Uploads skip the unnecessary remote existence check because each destination
has a distinct timestamp. Destination sanitization remains enabled. This avoids an
unrelated existing printer filename containing repeated dots causing FluentFTP
to reject an `NLST` result before transfer.

## Diagnosing a MicroSD message after upload

Successful FTPS transfer confirms transfer completion, not that the print-start
URL or project contents are valid. Before assuming a physical card defect,
check that the start URL includes `/cache/` and that the archive contains the
requested plate. OpenFarm previously omitted the cache prefix and sent bare
G-code as if it were a project archive; both were corrected on October 7, 2026.

Local MQTT/FTPS tests verify exact uploaded bytes, archive contents, checksums,
and path agreement. They do not establish firmware acceptance on physical
hardware. After loading the updated API, a successful real-printer start is
still needed to confirm resolution of a particular MicroSD error.

## Filament feed mapping (`use_ams`, `ams_mapping`, `ams_mapping2`)

Without a mapping a Bambu prints from the external spool. At dispatch OpenFarm tells the printer which feed each
filament of the file uses, from the spools it matched for the job (`PrintFeedMappingBuilder`).

- `ams_mapping` is an integer array indexed by the project's filament (the G-code's `T<n>`, slice_info filament
  `id - 1`), as long as the project's filament list. Each entry is the absolute tray id: `unit * 4 + slot` for a
  4-slot AMS or AMS Lite, the unit id (128+) for a single-slot unit, `-1` for an unused filament or the external spool.
- `ams_mapping2` is the same list as `{ "ams_id", "slot_id" }`; `{255, 255}` is unmapped and the external feed is
  `{255, 0}`. Both arrays follow Bambu Studio and [bambu-printer-manager](https://github.com/synman/bambu-printer-manager).
- `use_ams` is true when any filament comes from an AMS tray.
- The external spool is spelled out (`[-1]` plus `ams_mapping2`) when the model's `SendsExplicitExternalMapping` is
  true (default). A community report says an omitted mapping can raise HMS `07FF_8012` on current firmware; turn the
  flag off per model in `PrinterModelCatalog` if a printer rejects it.

### One feed path at a time

An AMS and the external spool share one filament inlet, and an external spool that is not already loaded into the
extruder is never fed automatically. On a printer with an AMS, production therefore uses either the AMS or the
external spool, never both (`ProductionFeedPath.ActiveSlotIds`). Which one comes from the printer's own
`print.ams.tray_now`, stored as `Printer.ActiveTrayId`: `254` means the external spool is the active feed, so only it
is usable; anything else (an AMS tray, `255` nothing loaded, not reported yet) means only the AMS is usable. A
printer without an AMS always uses the external spool. A reloader replaces the external port and loads through the
extruder, so it is usable on either path. A job whose colours would need both the AMS and a reloader is held.

`tray_now` meanings come from community sources (Home Assistant `ha-bambulab`, OpenBambuAPI), not Bambu documentation.
Unverified on hardware: `254` while idle with external filament hand-loaded, `255` on A1 / AMS Lite printers, the
`ams_id` used for the external feed in `ams_mapping2`, and printer acceptance of both arrays.

## RFID spools

A Bambu spool's two RFID tags carry the same spool id (`tray_uuid`); OpenFarm stores it as the spool's `RfidId`. An all-zero
id (an untagged spool, or a printer without a reader, such as the A1's external holder) means no tag. Only the parser knows
the Bambu field names: the rest of the app sees `AmsTray.RfidId` and `RfidWeightGrams` (`tray_weight`).

- **Register or link.** `POST /api/v1/spools` takes `rfidId` (quantity 1). `PUT /api/v1/spools/{id}/rfid` ties a spool already
  in inventory to its tag, so a spool registered before RFID assist is not registered twice. One active spool per tag: a
  finished spool keeps its tag for history and frees it, so a refill or re-used tag can be registered again, and restoring
  the old spool is then refused (409).
- **Auto-load.** After each telemetry flush, `RfidSpoolLinker` loads a registered spool into the AMS slot (or unreplaced
  external holder) whose tray reads its tag. The registered spool that sat there goes back to storage, and the tagged spool
  leaves any other slot it was in, clearing a reloader's feeding slot when it was that one. It runs under the printer's
  feed-path lock and retries at the next flush when another change wins the race.
- **Only when the tag is new to the slot.** `FeedSlot.LinkedRfidId` remembers the tag a slot last loaded by, across restarts
  and reconnects. The same tag still being read does nothing, so an operator who unloads a spool that stays in the AMS is not
  overruled; an empty reading forgets it, so re-inserting the spool loads it again. A tray holding a spool but reporting no tag
  is skipped, not read as empty. A tag read in two slots at once loads neither. A finished spool's tag loads nothing.
- Reloader slots are never linked: their feeding slot can be an estimate.
- Spools are never created from a tag: a created spool would have no price, could get a guessed color, and would duplicate an
  untagged spool already in inventory.
