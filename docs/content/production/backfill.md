---
title: Continuous backfill
description: Use idle compatible printers for repeat production after jobs with fixed quantities.
---

Backfill repeatedly prints whole plates until you pause or remove the entry. It uses compatible printers that are available after jobs with fixed quantities.

## Start backfill

Open a part or assembly's **Print** flow and enable **Continuous backfill**. Submit the entry, then manage it from **Queue**. By default, the entry uses one printer at a time.

The entry remains visible even when it has no current jobs. You can limit how many printers it uses at once or pin it to one printer. You can also pause, resume, refresh, or remove it.

## Assembly balancing

For assembly backfill, OpenFarm saves the number of each printed part needed for one assembly set. It compares these quantities with completed units and units already allocated to queued or running work. It gives priority to the eligible part furthest behind the proportions needed for the next complete set.

If one part cannot print, this balancing limits excess production of the other parts. Failed and cancelled jobs do not count as completed output. Each selected part must still match a ready printer.

## Pause, refresh, and remove

- **Pause** blocks new dispatches and retries. It does not physically cancel an already running print.
- **Refresh** uses the assembly's current bill of materials (BOM). It replaces old requirements, cancels unsent jobs, and starts a new balancing period. Running prints remain linked to the original assembly recipe.
- **Remove** cancels unsent work and retains running prints and history.

Backfill uses the current part file until OpenFarm prepares and sends a job. Changing a part file alone does not require an assembly recipe refresh. For an order with a fixed quantity and a defined end, use a [run](runs.md).
