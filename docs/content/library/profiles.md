---
title: Profiles and slicers
description: Use bundled catalogs and imported presets without losing the exact slicer required by a part.
---

Profiles configure slicing; they do not represent physical printers or spools. **Profiles** separates machine, process, and filament catalogs.

## Bundled system catalogs

The production image bundles the slicers declared in the repository manifest. Startup ingests their system profiles. Each engine/version has its own catalog.

Choose the farm's default engine and exact version under **Settings → Slicing defaults**. That selection controls catalog pickers and new processing choices. Existing parts remain pinned to their saved engine/version.

## Import user presets

Open **Profiles → Import profiles**. The UI accepts `.zip`, `.orca_printer`, `.orca_filament`, `.bbscfg`, and `.bbsflmt` bundles, with a 20 MiB request body limit.

Upload the bundle, review the discovered machine/filament presets, and select what to import. Nothing is saved until confirmation. Do not assume every profile embedded in an arbitrary project is a selectable import.

## Configure where profiles are used

- The **printer** selects machine and process profiles and its build plate.
- The **filament** assigns a profile for each model/nozzle group, optionally overridden per color.
- The **part** retains the engine and exact version used for its processing/production pin.

Unsliced multi-color projects need compatible profiles for every required material slot. Pre-sliced files keep their toolpaths and must match the model they declare.

## When profiles do not resolve

OpenFarm resolves saved choices into the part's pinned slicer's own catalog. Compatible system twins can map across supported catalogs; a user preset from another engine is blocked. Missing, ambiguous, unloaded, or incompatible presets stop dispatch rather than triggering a substitute.

Use [Job diagnosis](../production/matching.md) for the resolved requirement and configuration link. If bundled profiles are missing after a native setup, run the profile-ingestion step in [Local development](../development/local-setup.md). Do not change a pin merely to hide an unexplained compatibility failure.
