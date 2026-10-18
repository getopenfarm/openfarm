# Filament profile assignments

Filament inventory and profile configuration are separate. Inventory can be saved without profiles. Production requires an effective compatible profile for each tracked spool used by the job, including pre-sliced jobs.

## Hardware identity

The profile-assignment card groups the fleet by registered brand, model, and **reported nozzle diameter**. Duplicate printers share one row. Machine profiles never supply a nozzle diameter. Last reported sizes remain available for offline configuration, but production and new machine-profile selections require confirmation from the current printer connection.

Bambu telemetry accepts numeric and string diameters, including uniform nested nozzle reports. Partial reports preserve the last observation. Explicit unknown or mixed nozzle reports remove confirmation. Reconnection, connection edits, and reachability loss remove confirmation until a fresh valid nozzle report arrives.

## Assignments

Each filament can have a default assignment for each hardware group. A colour can override that assignment; clearing an override restores inheritance. Assignment writes validate effective inherited material, declared nozzle compatibility, compatible printer names, selectable/current status, and directional engine support. Compatibility expressions that cannot be evaluated are rejected with an explanation.

The edit card retains assignments for hardware no longer in the fleet and displays invalid saved assignments. Machine-profile pickers are filtered server-side using the printer's registered model and confirmed telemetry nozzle. Invalid existing machine links remain visible so unrelated printer edits remain possible.

## Production

Production resolves tracked spools on the destination printer's active feed slots. It applies the colour override or filament default for that printer's hardware group. Part narrowing acts as a constraint on that physical assignment. Unused loaded spools do not block a usable matching spool. No catalog, storage, or other-printer fallback is used.

Queue eligibility and file generation use the same readiness checks. Blocked rows expose an actionable configuration reason. Pinned jobs can generate files against their configured target. Before sending a sliced file, dispatch checks fresh persisted configuration and compares its fingerprint with the settings used to generate the file.

Library estimation continues to use explicit estimation configuration, independently of fleet and inventory. Production slicing supports one distinct filament profile per slice; pre-sliced files can validate multiple required spool assignments. This change does not add printer models or multi-tool slicing support.

## Contracts and schema

- `GET /api/v1/filaments/profile-assignment-fleet` returns hardware groups and printers without nozzle observations.
- `GET /api/v1/filaments/{id}/profile-assignments` returns assignments and invalid-assignment issues.
- `PUT /api/v1/filaments/{id}/profile-assignments` atomically replaces defaults and colour overrides.
- Filament creation accepts `profileAssignments`; the former scalar `filamentProfileId` is removed.
- Machine profile listing accepts `printerId`; filament profile listing accepts `brand`, `modelCode`, and `nozzleDiameterMm`.

`AddTelemetryFilamentAssignments` adds persisted nozzle state and assignment rows. It drops the old scalar column without backfilling development data. Generated frontend clients come from the OpenAPI contract. `export-openapi` exports contracts without running database initialization or printer workers.

## Profile origin and engine compatibility

`SourceFormat` records where a preset originated; it is not an engine exclusivity flag. Fleet pickers use the farm's effective default slicer, prefer its native catalog before pagination, and also offer Bambu Studio presets when OrcaSlicer is selected. Bambu Studio does not offer Orca-origin presets. Picker options retain upstream names and show source, origin/vendor, and the hardware group they match. A shared X1C-labelled profile can correctly match a P1S when its declared compatibility includes that machine and telemetry nozzle.

Production uses the part's resolved engine/version (including its existing override), not the picker default. Machine, process and filament sources are checked together. Orca can consume Bambu-origin profiles only after the resolved settings successfully slice a small validation model with that exact engine version. Inventory assignments can be saved before printer process/plate configuration is complete; the engine validation happens when slicing readiness is evaluated, before dispatch. Failure appears as a blocking configuration reason. Pre-sliced files do not need this additional slicing check.

Evidence is kept in a bounded process-local cache: engine/version, all resolved JSON, source metadata, material and nozzle are hashed together. Success lasts up to 24 hours, failure up to one minute; profile/template edits or engine/nozzle changes use a new key, and cancellation is not cached. Host restart discards the evidence. Native presets do not incur an additional validation slice. Library estimation accepts the same directional formats and its actual estimation slice reports engine errors.

Inherited Bambu template includes are resolved in the source catalog before engine validation, including imported user presets. Missing, ambiguous, cyclical or excessively deep includes are rejected rather than silently ignored. A successful validation proves the selected engine can load and slice the settings; it does not certify physical print quality or that every parameter has identical semantics across versions.

Verified with the locally installed OrcaSlicer 2.3.1 using the Bambu Studio 2.7.1 `Bambu PLA Basic @BBL P1S 0.4 nozzle` filament settings and native Orca P1S machine/process settings. The real engine validation slice succeeded.

System catalog identities and uniqueness indexes include `SourceFormat` as well as vendor and external ID (or abstract-base name). Ingesting one engine no longer overwrites the other catalog or marks its rows stale. Re-ingestion reconnects each catalog’s inheritance within its own source. The `SeparateProfileSourceCatalogs` schema migration updates these indexes; no legacy catalog backfill is included.
