---
title: Filament catalog
description: Define material identities, color variants, and printer-specific slicing assignments.
---

The filament catalog describes what material you stock. Physical rolls are separate [spool records](spools.md).

## Add a material

Open **Filament → Add filament**. Set the vendor, name, material type, and any known price per kilogram. Add the color variants you actually use. Optional low-stock and days-left thresholds help identify replenishment needs.

Blank prices mean unknown; do not enter zero unless the cost really is zero. A color can override the material's default price.

## Assign slicing profiles

Configure a filament's profile assignments for each **printer brand/model + nozzle size** you use. Identical hardware shares an assignment row. A color can override that row; clearing the override restores inheritance from the filament default.

Assignments must match the material, hardware, nozzle, and selected slicer catalog. Inventory can be saved without them, but production requires compatible assignments, including for pre-sliced files.

The farm's default slicer controls picker catalogs. When production needs slicing, existing parts retain their exact slicer pin and the selected profiles must resolve in that pinned catalog. Already-sliced jobs validate assignments without applying their settings to the toolpaths. See [When profiles apply](../library/profile-behavior.md) and [Profiles and slicers](../library/profiles.md).

## Choose production filament on a part

Select the catalog **Production filament** on the part's requirement. That chosen color variant becomes the effective identity the job matches against tracked loaded spools. The file's detected material/color remains visible as source metadata.

You can choose another color of the same material; selecting a different known material type is rejected. A loaded spool of the exact selected variant satisfies the identity check. Review [matching diagnostics](../production/matching.md) for narrower profile constraints or missing assignments.

## Review stock and use

Open a filament to switch between **Colors**, **Spools**, **Usage**, and **History**. Its color rows summarize stock and consumption. Stock numbers describe registered inventory, so keep spool loading, weights, and corrections current.
