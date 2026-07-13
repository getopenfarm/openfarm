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

Real-hardware validation against a physical Bambu printer remains a required manual step before
trusting `BambuLanConnector`/`BambuMqttMessageParser`/`BambuCameraRelay` in production — see
`specs/002-printer-integration/quickstart.md`'s "Physical hardware validation (manual)" section.
