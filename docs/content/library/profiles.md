---
title: Profiles and slicers
description: Use bundled catalogs and imported presets without losing the exact slicer required by a part.
---

Profiles are saved slicer settings, also called presets. They are separate from physical printer and spool records. The **Profiles** page has separate catalogs for machine, process, and filament profiles.

See [When profiles apply](profile-behavior.md) for the rules for STL, geometry-only 3MF, project 3MF, sliced 3MF, and G-code.

## Bundled system catalogs

The production image includes the slicers listed in the repository manifest. On startup, OpenFarm imports their system profiles. Each slicer engine and version has its own catalog.

Choose the farm's default slicer engine and exact version under **Settings → Slicing defaults**. This determines the catalog shown in selection lists and the default for new file processing. Existing parts keep their saved engine and version, called the slicer pin.

## Import user presets

Open **Profiles → Import profiles**. The UI accepts `.zip`, `.orca_printer`, `.orca_filament`, `.bbscfg`, and `.bbsflmt` bundles, with a 20 MiB request body limit.

1. Upload the bundle.
2. Review the machine and filament presets found in it.
3. Select the presets to import.
4. Confirm the import.

OpenFarm saves nothing before confirmation. A preset embedded in a project is not necessarily available as an import.

## Configure where profiles are used

- The **printer** selects machine and process profiles and its build plate.
- The **filament** assigns a profile for each printer model and nozzle size. A color can override that assignment.
- The **part** keeps its saved slicer engine and exact version for file processing and production slicing.

Unsliced multi-color projects need compatible profiles for every required material slot. Pre-sliced files keep their toolpaths and must match the model they declare.

## When profiles do not resolve

If a job needs slicing, OpenFarm finds the selected presets in the part's pinned slicer catalog. A matching system preset can come from another supported catalog. A user preset from another slicer engine is blocked.

Missing profiles, multiple matches, unloaded catalogs, and incompatible presets stop the job. OpenFarm does not substitute another preset. Already-sliced files skip this catalog matching. They still require a compatible machine profile and filament-profile assignments for loaded spools.

Open [Job diagnosis](../production/matching.md) to see the requirement and a link to the relevant settings. If profiles are missing after a local setup, run the profile-import step in [Local development](../development/local-setup.md). Find the cause of a compatibility failure before changing the slicer pin.
