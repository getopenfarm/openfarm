# File-based Part Library estimation

Approved 2026-10-05. Supersedes fleet-representative estimation and inventory-derived estimation filament choices in earlier specs. Production continues to use the assigned printer and its profiles.

- Sliced3MF/GCode imports extract their own metadata. Sliced3MF combines per-plate slice XML, plate JSON, and GCode, with project fallback only where applicable. Used filaments determine sliced requirements. Conflicting nozzle/material makes requirements incomplete; conflicting time/weight makes only that estimate unknown.
- Unsliced Orca/Bambu project3MF uses its embedded effective configuration, including custom profiles, object overrides, plate placement, and multimaterial assignments. No profile-name substitution or external settings override. Partial/malformed/unsupported project settings leave estimates unavailable instead of using defaults.
- STL/geometry-only3MF uses optional organization estimation machine/process/filament/build-plate selections. No registered printer is needed and fleet/inventory changes do not select estimator settings. Filament is estimation-only; the existing any-material production requirement remains.
- Existing slicer engine/version selection remains. Defaults validate selectable profiles, source engine, inheritance/include resolution, machine/process/material/plate compatibility, known uniform nozzle, and positive plate temperature.
- GET/PUT /api/v1/settings/slicing-defaults adds estimationProfiles. Omitted preserves; null clears; provided requires all four choices. Existing permissions/activity logging remain.
- Nullable organization profile IDs/plate type, job resolved-configuration JSON, and job/Part public estimation-detail/warning JSON are additive migration fields. Legacy rows are not recalculated or assigned profiles. Nozzle/material estimates never borrow fleet configuration.
- Processing snapshots resolved estimator settings at processing start. Existing review/save, explicit reprocess, revisions, late-result protection, and unavailable/partial estimates remain. Responses expose estimate source, machine/profile names, plate, and actual estimator engine/version.
- Settings→Slicing Defaults configures estimation profiles. Review/detail show estimate basis and explain possible production estimate differences. Settings changes affect new processing or explicit Reprocess only.

## Real-tool verification

Bundled OrcaSlicer2.3.1/macOS successfully sliced the saved Benchy project without --load-settings or --load-filaments: 2131seconds/11.09g, retaining0.4mm/PLA. An unsupported older calibration project is rejected safely. Added an optional real-tool regression using the saved project.
BambuStudio2.7.1/macOS rejected the Orca sample and crashed on its own bundled calibration project. Embedded mode for that exact combination is unavailable; no silent alternate profile/engine is selected. Other compatibility failures surface as unavailable estimates. No slicer bundles/pins changed.
H0003 was inspected in the preceding research turn, but its Downloads folder is no longer present. Synthetic fixtures retain its metadata shape and values: P1S,0.6mm,PETG,customScrubblerprofile,179m (10790seconds),176.84g.
