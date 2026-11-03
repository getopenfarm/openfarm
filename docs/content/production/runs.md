---
title: Production runs
description: Track an order or batch with fixed part quantities and complete assembly sets.
---

A run groups an order or batch with fixed quantities. Use it to track progress and confirm completion. Direct queue jobs do not require a run.

## Create a run

1. Open **Runs → Add run**.
2. Use **Add part or assembly** under **Items to make**.
3. For a part, choose whole **Plates** or a **Unit target**. For an assembly, enter complete **Sets**.
4. Choose automatic assignment or a specific printer for each line.
5. Review required units, planned plates, and estimates.
6. Enter the run details.
7. Submit using **Add run**.

The run's jobs appear in Queue.

OpenFarm checks the plan again when you submit it. If library inputs changed during planning, refresh the plan and review the updated quantities before submitting again.

## Understand the final plate

Suppose five assembly sets need two brackets each and the part contains six brackets per plate. The total is ten brackets: one job targets six and another targets four.

Partial plates require reliable identifiers for complete printable units and a file that supports the required object selection. A quantity estimate alone is insufficient. If OpenFarm cannot verify safe selection, it blocks the operation. See [Outcomes and recovery](outcomes.md).

Parts requested in **Plates** remain whole-plate jobs. Continuous backfill also schedules whole plates.

## Monitor progress

Open the run to see its lines, plate states, produced output, and unresolved shortfalls. Failed or cancelled attempts remain separate from their replacements in the history. Inspect output before confirming unknown quantities.

Use **Requeue missing units** to replace an unresolved shortfall. Use **Write off missing units** to accept the loss. Available close and completion actions depend on the run's progress and unresolved work. Cancelling a run does not erase prints that already happened.

Jobs with fixed quantities keep the source inputs saved when queued. OpenFarm still checks current printer settings and loaded spools before sending each job. Creating a plan does not reserve a printer configuration or its stock.
