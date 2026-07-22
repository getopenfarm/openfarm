# Vendor profile index fixtures (System Profile ingestion)

Per `specs/009-process-profiles/research.md` §0/§0a, System Profile ingestion (`VendorProfileIndexReader`,
`SystemProfileIngestionService`) is tested fixture-replay/test-after against real, MIT-licensed vendor
profile JSON, the same precedent `003-slicer-profile-import`'s own `Fixtures/README.md` already established
for `ProfileBundleParser` — not a synthetic stand-in for the vendor index shape.

## `OrcaSlicer/`

Fetched verbatim from `https://raw.githubusercontent.com/OrcaSlicer/OrcaSlicer/v2.4.2/resources/profiles/BBL/...`
— the real, released v2.4.2 source tree (the release .zip asset itself isn't reachable from this
environment, but this is the exact path that release is built from; see research.md §0's own note). `BBL.json`
is trimmed down to just the entries whose files are included below (a real vendor's `BBL.json` lists ~2,500
presets; only a representative slice is needed to exercise ingestion) — every entry kept still points at a
real, unmodified preset file:

- A full `instantiation:false → true` inheritance chain for one machine model (Bambu Lab X1 Carbon):
  `machine/fdm_machine_common.json` (root) → `machine/fdm_bbl_3dp_001_common.json` (abstract) →
  `machine/Bambu Lab X1 Carbon 0.4 nozzle.json` (leaf, `setting_id: GM001`), plus the `machine_model_list`
  entry `machine/Bambu Lab X1 Carbon.json` itself (`type: machine_model`, not a preset).
- The matching process chain: `process/fdm_process_common.json` → `process/fdm_process_single_0.20.json` →
  `process/0.20mm Standard @BBL X1C.json` — this leaf carries a real `renamed_from` field
  (`"0.20mm Bambu Support W @BBL X1C"`), exercising the research.md §1 rename case (upsert-by-`setting_id`
  survives a name change).
- The matching filament chain: `filament/fdm_filament_common.json` → `filament/fdm_filament_pla.json` →
  `filament/Bambu PLA Basic @base.json` (vendor-generic, `instantiation:false`) →
  `filament/Bambu PLA Basic @BBL X1C.json` (leaf, `setting_id: GFSA00`).

## `BambuStudio/`

Fetched verbatim from `https://raw.githubusercontent.com/bambulab/BambuStudio/master/resources/profiles/BBL/...`
(research.md §0a — cloned directly from upstream Bambu Studio, independent of the OrcaSlicer fork, to
verify a real format divergence). Same X1 Carbon machine/process/filament chain as `OrcaSlicer/` above
(byte-identical where they overlap, confirming the two engines agree on shared history), plus the two
`include`-mechanism cases OrcaSlicer's tree never uses:

- **Machine**: `machine/Bambu Lab X1 Carbon 0.4 nozzle.json` carries a real `include` array naming five
  gcode-template fragments, all present here as their own files
  (`machine/Bambu Lab X1 Carbon 0.4 nozzle template {change_filament,layer_change,machine_end,
  machine_start,time_lapse}_gcode.json`) — each fragment has `instantiation:"false"` and **no `type` field
  at all**, confirming research.md §0a's finding that Bambu Studio's `machine_list` mixes real
  `type:"machine"` presets with untyped gcode-template fragments; ingestion must filter by declared `type`,
  not list membership alone, or these five would be mis-ingested as machine presets.
- **Filament**: `filament/Bambu PLA Dynamic @BBL X1C 0.2 nozzle.json` (a different, real leaf from the same
  vendor index) carries a real `include: ["fdm_filament_template_direct_dual"]`, resolved here via
  `filament/fdm_filament_template_direct_dual.json` — this fragment *does* carry
  `type:"filament"`/`instantiation:"false"`, confirming research.md §0a's finding that filament-side include
  targets look like ordinary abstract rows (unlike the untyped machine-side fragments above), just reached
  through a second field the pre-existing `BaseProfileId` chain-walk never reads. Its own base chain
  (`filament/Bambu PLA Dynamic @base.json` → `filament/fdm_filament_pla.json`, shared with the PLA Basic
  chain above) is included too so the whole chain resolves within this fixture set.

`BBL.json` is trimmed the same way as `OrcaSlicer/`'s.

## `UnsupportedVendor/`

`Creality.json` — a trimmed but real slice of
`https://raw.githubusercontent.com/OrcaSlicer/OrcaSlicer/v2.4.2/resources/profiles/Creality.json`: its
`machine_model_list` (`Creality CR-10 Max`, `Creality Ender-3 V2`) has zero overlap with any
`PrinterModelCatalog.VendorMachineModelName` value (the catalog only maps Bambu Lab models today). No
preset files are included — a vendor failing the model-catalog overlap check is skipped before any of its
individual presets are fetched, so nothing beyond the index itself is needed to exercise that scoping path
(spec.md Acceptance Scenario 3, SC-009).
