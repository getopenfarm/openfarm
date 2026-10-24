# Filament profile assignments

Filament inventory and profile configuration are separate. Inventory can be saved without profiles. Production requires an effective compatible profile for each tracked spool used by the job, including pre-sliced jobs.

## Hardware identity

The profile-assignment card groups the fleet by registered brand, model, and **reported nozzle diameter**. Duplicate printers share one row; a printer whose extruders have different nozzles is in one group per size. Machine profiles never supply a nozzle diameter. Last reported sizes remain available for offline configuration, but production and new machine-profile selections require confirmation from the current printer connection.

Bambu telemetry accepts numeric and string diameters, including uniform nested nozzle reports. Partial reports preserve the last observation. Each extruder is confirmed at its own reported nozzle; an explicit unknown report removes that extruder's confirmation. Reconnection, connection edits, and reachability loss remove confirmation until a fresh valid nozzle report arrives (`docs/PRINTER_HARDWARE_MODEL.md`, Nozzles).

## Assignments

Each filament can have a default assignment for each hardware group. A colour can override that assignment; clearing an override restores inheritance. Assignment writes validate effective inherited material, declared nozzle compatibility, compatible printer names, selectable/current status, and that the profile resolves in the farm default slicer's own catalog (see Slicer resolution). Compatibility expressions that cannot be evaluated are rejected with an explanation.

The edit card retains assignments for hardware no longer in the fleet and displays invalid saved assignments. Machine-profile pickers are filtered server-side using the printer's registered model and confirmed telemetry nozzles (a preset at any of them). Invalid existing machine links remain visible so unrelated printer edits remain possible.

## Production

Production resolves tracked spools on the destination printer's active feed slots. It applies the colour override or filament default for that printer's hardware group. Part narrowing acts as a constraint on that physical assignment. Unused loaded spools do not block a usable matching spool. No catalog, storage, or other-printer fallback is used.

Queue eligibility and file generation use the same readiness checks, resolved once per dispatch. Blocked rows expose an actionable configuration reason. Pinned jobs can generate files against their configured target. Before sending a sliced file, dispatch resolves again and compares the fingerprint with the one the file was generated from; a mismatch sends nothing.

Library estimation continues to use explicit estimation configuration, independently of fleet and inventory.

**Multi-colour production slicing.** Every job line resolves its own filament profile against the part's pin, with the
same checks as a single-colour slice (compatibility with the printer, material, plate temperature); every problem is
reported, each with the page that fixes it. The file is then sliced with one `--load-filaments` file per filament slot
of the project, in slot order: a slot's material and colour name its line — the key the part's requirements were
grouped by — and the slot's colour is written into its file, so the output's filaments match the lines again at
dispatch (AMS trays, or the Snapmaker U1's tool→head map). A blank slot is unused and gets the first line's filament;
a slot no line claims, or a file without project slots that has several lines, blocks the job instead of guessing.
Measured with Orca 2.3.1: a single loaded file sliced slot 1 with it and every other slot with the preset embedded in
the project, unresolved against the pin — which per-slot files replace. Pre-sliced files run as they are and only go
to the model they were sliced for. SuperSlicer slices one filament.

## Contracts and schema

- `GET /api/v1/filaments/profile-assignment-fleet` returns hardware groups and printers without nozzle observations.
- `GET /api/v1/filaments/{id}/profile-assignments` returns assignments and invalid-assignment issues.
- `PUT /api/v1/filaments/{id}` atomically replaces the filament's fields and its `profileAssignments` (defaults and colour overrides).
- `GET /api/v1/slicing/readiness?engine=&version=` reports which printers and filament assignments would block production for one slicer pin (see Slicer resolution).
- Filament creation accepts `profileAssignments`; the former scalar `filamentProfileId` is removed.
- Machine profile listing accepts `printerId`; filament profile listing accepts `brand`, `modelCode`, and `nozzleDiameterMm`.

The persisted nozzle state and the assignment rows are part of the initial schema (`InitialCreate`). Generated frontend clients come from the OpenAPI contract. `export-openapi` exports contracts without running database initialization or printer workers.

## Slicer resolution (native-only, fail-closed)

A profile choice (a printer's machine and process, a filament's assignment, the estimation defaults) is stored as one row of one engine's catalog. A part is pinned to one slicer engine and exact version. Production resolves each choice to **that slicer's own copy** and to nothing else. `ProfileResolver` decides, per selected row and pin:

- System row of the pin's engine and version: used as it is (`Exact`).
- System row of another version of the same engine: mapped by the vendor's `setting_id`, so a renamed profile is still found (`MappedVersion`). Rows without an id match by vendor and name.
- System row of the other engine: its exact twin in the pin's catalog, with the same `setting_id`, vendor and name (`MappedEngine`). OrcaSlicer and Bambu Studio share these ids for Bambu Lab (`BBL`) profiles only.
- User row of the pin's engine: used as it is; its System ancestors are mapped as above (`UserNative`).
- User row of the other engine: blocked (`UnsupportedOrigin`).

Everything else is blocked, never substituted: `NoPin` (the part has no engine and version), `SlicerUnavailable` (not bundled), `EngineWithoutProfiles`, `Missing` (no twin in the pin's catalog), `Ambiguous` (several twins), `NotOffered` (no longer in the vendor index), `CatalogNotLoaded` (the pin's catalog was never ingested) and `InvalidRow`. After mapping, `Incompatible` covers the machine, process, filament, nozzle and material checks, which run on the **resolved** rows. A blocked job is not sliced and not sent; no other engine, version or profile takes its place.

Where it shows: the queue row (reason and link to the page that fixes it), job detail ("Last attempt failed", and "Sliced with …" with the resolved profiles), the part, printer, filament and Slicing defaults pages, the part upload/replace/reprocess dialog, and the new-run page. They all read `GET /api/v1/slicing/readiness`, which runs the same checks as dispatch, so a warning cannot disagree with what production does.

Pickers list only the farm default engine's own catalog: System rows of its version and User rows of its format. The imports page still lists every format. Filament assignments are validated against the farm default; parts pinned to another slicer are checked by the readiness report and at dispatch.

Dispatch resolves once. The configuration stamp contains the pin and the resolved profiles (name, id, `setting_id`, version, outcome), every line's filament included. The resolved profiles are stored on the job and the last block reason is kept; a blocked job is retried with a growing delay (1, 2, 4, then 5 minutes).

Pins change only by an explicit operator action. Replace file and Reprocess keep the current pin unless another slicer is chosen. A farm default change or a slicer upgrade never touches existing parts.

Inherited Bambu template includes are resolved inside the pin's own catalog; a missing, ambiguous, cyclical or excessively deep include is rejected rather than ignored. A successful slice proves the slicer can load the settings. It does not certify physical print quality.

System catalogs are per slicer version. Identities and uniqueness indexes include `SourceFormat` and `SourceVersion` (the bundled slicer's version, i.e. its leaf directory name) as well as vendor and external ID (or abstract-base name). An ingestion run refreshes only its own (engine, version) catalog, in one transaction: other engines and other versions are never overwritten or marked stale. A System row always has a `SourceVersion` (a database CHECK enforces it); User rows have none. Re-ingestion reconnects (or clears) each preset's inheritance within its own catalog, and unchanged data writes nothing.

Pickers and name lookups read one catalog version per engine: the farm default's version, else the newest bundled version that was ingested, else the newest ingested one. A stored profile choice is resolved to the pinned slicer's catalog as described above; if that catalog was never ingested, production is blocked (`CatalogNotLoaded`). "Same profile" checks (a part's narrowed profile against a spool's assignment) compare these identities, not row ids.

OrcaSlicer ships generic filament bases in its `OrcaFilamentLibrary` index. The bases a vendor filament inherits from (with their own ancestors) are stored as hidden, non-selectable rows so those filaments resolve and slice; the rest of the library is not stored. These indexes are part of the initial schema (`InitialCreate`).
