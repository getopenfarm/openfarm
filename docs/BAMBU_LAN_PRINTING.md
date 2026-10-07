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
