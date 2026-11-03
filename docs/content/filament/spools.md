---
title: Spools and usage
description: Register physical rolls, load them into printers, and maintain their remaining weights.
---

One spool record represents one physical roll. Its lifecycle is **In storage → Using → Finished**, with usage history retained along the way.

## Add spools

Use **Add spools** from the filament workflow. Enter one line per filament/color and roll specification, then set its quantity. Shared purchase details such as supplier, order, and date are entered once for the batch.

Set the spool weight, known remaining filament for a partly used roll, and price per spool when known. Full identical rolls can appear grouped on the shelf; expand the group to inspect individual labels.

## Load and unload

1. Physically load the spool into the intended printer feed path.
2. Choose **Load spool** and select the matching printer, feed unit, and slot.
3. Check that the slot shows the intended inventory record and material.

**Unload** returns the record to storage without discarding its remaining weight. Registration does not physically move filament. A printer's reported material alone does not establish tracked inventory.

## Automatic usage

OpenFarm binds job filament to the loaded spool and estimates consumption from the dispatched file. Completed work deducts its estimated usage; stopped work uses available layer/progress information. Object exclusions affect the estimate without simply scaling grams by the final good-unit count.

These are estimates, not scale measurements. Prints started outside OpenFarm can also be tracked when the server can recover their file and spool mapping.

## Corrections and missing deductions

Open a spool for its weight and history. Use **Record usage** for adjustments such as purging, test extrusion, or drift. Correct the remaining amount when a measured weight is more reliable.

If deduction could not identify a spool or estimate, the print appears under **Action required**. Assign a spool or resolve the missing-use issue from the print workflow where available; use a manual adjustment when needed. Avoid deducting the same use twice.

Use **Finish** when the roll is exhausted. [Feed units and reloaders](feed-units.md) explains how OpenFarm selects continuation spools.
