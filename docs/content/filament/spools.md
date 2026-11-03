---
title: Spools and usage
description: Register physical rolls, load them into printers, and maintain their remaining weights.
---

One spool record represents one physical roll. Its states are **In storage → Using → Finished**. OpenFarm keeps its usage history through these changes.

## Add spools

Use **Add spools** from the filament workflow. Enter one line per filament/color and roll specification, then set its quantity. Shared purchase details such as supplier, order, and date are entered once for the batch.

Set the spool weight, known remaining filament for a partly used roll, and price per spool when known. Full identical rolls can appear grouped on the shelf; expand the group to inspect individual labels.

## Load and unload

1. Physically load the spool into the intended printer feed path.
2. Choose **Load spool** and select the matching printer, feed unit, and slot.
3. Check that the slot shows the intended inventory record and material.

**Unload** returns the record to storage without discarding its remaining weight. Registration does not physically move filament. A printer's reported material alone does not establish tracked inventory.

## Automatic usage

OpenFarm links each job's filament to a loaded spool. It estimates consumption from the file sent to the printer. For completed prints, OpenFarm deducts the estimated usage. For stopped prints, it uses available layer or progress information. Object exclusions affect the estimate. Filament use is not simply proportional to the number of good units produced.

These are estimates, not scale measurements. Prints started outside OpenFarm can also be tracked when the server can recover their file and spool mapping.

## Corrections and missing deductions

Open a spool for its weight and history. Use **Record usage** for adjustments such as purging, test extrusion, or drift. Correct the remaining amount when a measured weight is more reliable.

If OpenFarm cannot identify a spool or estimate, the print appears under **Action required**. Open the print and assign a spool or resolve the missing usage information where the controls are available. Make a manual adjustment if necessary. Do not deduct the same usage twice.

Use **Finish** when the roll is exhausted. [Feed units and reloaders](feed-units.md) explains how OpenFarm selects continuation spools.
