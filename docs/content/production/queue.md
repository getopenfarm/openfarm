---
title: Print queue
description: Send whole plates to production and manage waiting and active work.
---

The queue holds physical plate attempts. You can print directly from a part or assembly without creating a production run.

## Send work

Open a part and choose **Print**. Select the number of whole plates, review the plan, and choose **Send to queue**. For an assembly's direct Print flow, set whole-plate counts per component; zero skips a component.

For an exact unit target or complete assembly sets, use [Runs](runs.md). For repeat production during idle capacity, use [Continuous backfill](backfill.md).

:::caution Ready printers may start
A queued job can start automatically on a compatible idle printer. Before you send work to the queue, check the physical bed and material path.
:::

## Follow a job

Open **Queue** and use its status tabs and part-name search. A job page shows the printer, production inputs, matched profiles, outcome, file, and available actions. Each printer shows progress while OpenFarm prepares and uploads its file. Physical printing starts separately.

Queue ordering affects selection among eligible jobs. Compatibility is a separate decision: a job near the top may remain blocked while a later compatible job runs.

## Change waiting work

The row action menu shows the operations available for that job:

- **Move to top** changes waiting priority.
- **Pin to printer** restricts the job to one printer. **Unpin** restores automatic assignment.
- **Remove job** cancels unsent work after confirmation.

Pinning does not bypass matching. A pinned job waits if its printer is busy, offline, missing material, or incompatible. Open the blocking status or **Job diagnosis** for details.

## Active work and external prints

Pause, resume, and cancel are offered only for eligible jobs and accounts with explicit remote-control permission. Confirm the physical outcome after a stop.

**Printer-started prints** are tracked separately from OpenFarm-dispatched jobs when observable. They can have incomplete file or spool information and do not become queued demand or run progress automatically.
