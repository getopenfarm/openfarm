---
title: Production runs
description: Track a finite order or batch using part quantities and complete assembly sets.
---

A run groups finite production demand for progress and sign-off. Direct queue jobs do not require one.

## Create a run

1. Open **Runs → Add run**.
2. Use **Add part or assembly** under **Items to make**.
3. For a part, choose whole **Plates** or a **Unit target**. For an assembly, enter complete **Sets**.
4. Choose automatic assignment or a specific printer for each line.
5. Review required units, planned plates, and estimates. Fill the run details and submit using **Add run**. Its jobs appear in Queue.

The plan is checked again when submitted. If library inputs changed while planning, refresh/review the new plan rather than retrying an outdated quantity blindly.

## Understand the final plate

Suppose five assembly sets need two brackets each and the part contains six brackets per plate. The total is ten brackets: one job targets six and another targets four.

Partial plates require reliable complete-unit/object identities and a supported execution artifact. A quantity estimate alone is insufficient. If OpenFarm cannot verify safe selection, it blocks the operation; use the guidance in [Outcomes and recovery](outcomes.md).

Parts requested in **Plates** remain whole-plate jobs. Continuous backfill also schedules whole plates.

## Monitor progress

Open the run to see its lines, plate states, produced output, and unresolved shortfalls. A failed/cancelled attempt and its replacement remain separate history. Inspect output before confirming unknown quantities.

Use **Requeue missing units** for an unresolved shortfall or **Write off missing units** to accept that loss. Closing/completing a run follows its current progress and unresolved-work rules; cancellation does not erase prints that already happened.

Finite jobs retain their queued source inputs. Printer settings and loaded spools are still validated at dispatch, so creating a plan does not reserve a permanently valid production environment.
