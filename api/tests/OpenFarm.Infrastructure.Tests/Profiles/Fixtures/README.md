# Profile bundle fixtures

Per `specs/003-slicer-profile-import/research.md` §1's follow-up task, `ProfileBundleParser` is tested
fixture-replay/test-after against real preset content rather than an assumed schema. This sandbox has no
slicer install to run "Export Preset Bundle" from, so these fixtures reuse **real, MIT-licensed vendor
preset JSON fetched from the public `OrcaSlicer/OrcaSlicer` GitHub repo**
(`resources/profiles/BBL/machine/*.json`, `resources/profiles/BBL/filament/*.json`) — authentic field
names, nested single/dual-extruder array shapes, and `inherits` chains, restructured into the
`machine/`/`filament/` subfolder shape a real "Export Preset Bundle" produces (confirmed by multiple
independent sources, see research.md §1).

Two presets (`My Custom PLA.json` / `My Custom PLA - Red.json`) are hand-authored rather than fetched:
color-specific user presets are never part of the vendor's own shipped repo (colors are an end-user
choice), so no real public source exists for one. Their field values still mirror the real numbers
confirmed from the vendor chain (220°C nozzle, 55°C bed, 21 mm³/s max flow, 100% fan) so the fixture
stays realistic. This pair is also the one case in these fixtures where a base's own declared parent is
absent (a genuine root, `from: "user"`, no `inherits` key) — the common real shape for a from-scratch user
preset, as opposed to the vendor chains below which all ultimately root in a template
(`fdm_filament_pla`/`fdm_machine_common`) no real user ever exports.

The Bambu Studio fixtures (`.bbscfg`/`.bbsflmt`) are assembled by hand from the same real OrcaSlicer
vendor JSON, since Bambu Studio's own export tooling isn't reachable from this environment either — this
is a reasonable stand-in per spec.md's own Assumptions that both slicers "share the same underlying
bundle structure," OrcaSlicer being a Bambu Studio fork.

**Follow-up recommended before shipping to real users**: replace these with genuine captured exports from
a real OrcaSlicer and Bambu Studio install, the same way `specs/002-printer-integration`'s hardware spike
captured real MQTT fixtures — this note mirrors research.md §1's own caveat.

## Fixture inventory

| File | Purpose |
|---|---|
| `orcaslicer-multi-preset-bundle.zip` | Multi-preset bundle: one machine preset with an unresolved base, two filament presets each with an unresolved base, plus the complete `My Custom PLA` → `My Custom PLA - Red` root/override pair. |
| `base-and-override-pair.zip` | Just the `My Custom PLA` root + `My Custom PLA - Red` override, isolated for base/override-resolution-specific tests. |
| `bambu-p1s-04-nozzle.orca_printer` | Single-preset machine export bundling the printer preset with its one directly compatible filament preset (spec.md Acceptance Scenario 5). |
| `aliz-petg-basic.orca_filament` | Single-preset filament-style export; also carries `Bambu PLA Basic @base` for prior-import base-resolution tests (import the multi-preset bundle first, then this file). |
| `bambu-studio-printer-config.bbscfg` | Bambu Studio printer config bundle equivalent of the OrcaSlicer multi-preset bundle. |
| `bambu-studio-filament.bbsflmt` | Single Bambu Studio filament preset using the buggy `compatible_prints` key spelling (OrcaSlicer issue #12193) instead of `compatible_printers`, to prove the parser tolerates both. |
| `not-a-zip.zip` | Plain text file with a `.zip` extension — corrupted/unrecognized archive (FR-007). |
| `empty-bundle.zip` | Valid zip with no machine/filament presets inside (FR-008). |

Regenerate all of the above with `python3 /tmp/fixture-build/build_fixtures.py` (script not checked in —
recreate it from this file's description if needed, or just re-fetch the named vendor JSON paths under
`https://raw.githubusercontent.com/OrcaSlicer/OrcaSlicer/main/resources/profiles/BBL/...`).
