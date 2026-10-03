# Fixture provenance

These are **not** captured from physical hardware by OpenFarm (no printer was reachable from the
development sandbox this phase was built in — see `specs/002-printer-integration/tasks.md` T004's
note). They are copied verbatim from `pybambu`'s own test corpus, vendored at
`third_party/printers/bambulab/references/ha-bambulab/custom_components/bambu_lab/pybambu/tests/`
(the reverse-engineered reference client this integration is built from) — genuine `report`/`get_version`
MQTT payloads recorded by that project's maintainers/contributors from real printers, not hand-invented
JSON.

| File | Source | Model | `gcode_state` | Notes |
|---|---|---|---|---|
| `idle-a1.json` | `pybambu/tests/MOCK-A1.json` | A1 | `IDLE` | Unmodified. |
| `printing-a1.json` | `pybambu/tests/MOCK-A1.json` | A1 | `RUNNING` | One field patched (`gcode_state`: `IDLE` → `RUNNING`) — the upstream corpus has no in-scope-model `RUNNING` sample; every other field is the genuine A1 fixture. |
| `awaiting-clear-p1p-no-ams.json` | `pybambu/tests/MOCK-P1PNOAMS.json` | P1P | `FINISH` | Unmodified. No AMS attached. |
| `awaiting-clear-x1c-multi-ams.json` | `pybambu/tests/MOCK-X1CMULTIAMS.json` | X1C | `FINISH` | Unmodified. Multiple AMS units with populated trays — used for AMS-parsing tests (Phase 4). |

| `sample-camera-frame.jpg` | Hand-constructed | — | — | **Synthetic**, not from pybambu's corpus (it has no binary camera-frame samples, only MQTT JSON). Bytes match research.md §4's `CAMERA_IMAGE` framing exactly (`FF D8 FF E0` start / `FF D9` end magic bytes around a minimal JFIF-shaped body) — protocol-accurate, not physically captured. Used by `BambuCameraFrameCodecTests`. |

Real-hardware validation against a physical Bambu printer remains a required manual step before
trusting `BambuLanConnector`/`BambuMqttMessageParser`/`BambuCameraRelay` in production — see
`specs/002-printer-integration/quickstart.md`'s "Physical hardware validation (manual)" section.

## Protocol harness coverage

The Bambu loopback harness now drives the real MQTT connector and camera relay over TLS on ephemeral
localhost ports. It uses generated certificates trusted only by explicit, instance-local test
options; production trust still uses the bundled Bambu certificates. MQTT fixture replay uses the
corpus above, while camera transport tests send synthetic framed bytes. This adds transport regression
coverage, not P1S firmware evidence. FTPS orchestration uses an injected uploader; the actual printer
FTPS server and command acceptance remain on the manual hardware checklist. See
[`../Bambu/README.md`](../Bambu/README.md) for test commands and harness limits.
