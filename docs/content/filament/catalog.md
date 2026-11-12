---
title: Filament catalog
description: Define material identities, color variants, and printer-specific slicing assignments.
---

The filament catalog describes what material you stock. Physical rolls are separate [spool records](spools.md).

## Add a material

Open **Filament → Add filament**. Set the vendor, name, material type, and any known price per kilogram. Add the color variants you actually use. Optional low-stock and days-left thresholds help identify replenishment needs.

Leave prices blank if they are unknown. Enter zero only when the known cost is zero. A color can override the material's default price.

## Assign slicing profiles

Assign a filament profile for each **printer brand, model, and nozzle size** you use. Printers with the same hardware share an assignment row. A color can override the default assignment. If you clear the color override, the color uses the filament's default assignment again.

Assignments must match the material, hardware, nozzle, and selected slicer catalog. Inventory can be saved without them, but production requires compatible assignments, including for pre-sliced files.

The farm's default slicer determines which catalog appears in profile selection lists. Existing parts keep their saved slicer and exact version, called the slicer pin. If a job needs slicing, the selected profiles must have compatible matches in that slicer's catalog. Already-sliced jobs still need compatible assignments, but OpenFarm does not apply their settings to the toolpaths. See [When profiles apply](../library/profile-behavior.md) and [Profiles and slicers](../library/profiles.md).

## Choose production filament on a part

Select **Production filament** for the part's material requirement. The dialog shows the material first, then the catalog filaments of that material. Each choice is named `<scope> · <color>`:

| Choice | Example | A loaded spool matches when it is |
|---|---|---|
| File color (default) | Any PETG · #000000 | any brand of PETG in the file's color |
| Any color of the material | Any PETG · Any color | any brand and any color of PETG |
| Any color of one filament | Aliz PETG Basic · Any color | any color of that filament |
| One color | Aliz PETG Basic · Black | that color of that filament |

Use an **Any color** choice for parts you print in several colors. The material and color detected in the file remain visible as source metadata. OpenFarm rejects a different known material type. Filament shortage warnings do not count jobs set to an **Any color** choice. Review [matching diagnostics](../production/matching.md) for profile constraints or missing assignments.

## Review stock and use

Open a filament to switch between **Colors**, **Spools**, **Usage**, and **History**. Its color rows summarize stock and consumption. Stock numbers describe registered inventory, so keep spool loading, weights, and corrections current.
