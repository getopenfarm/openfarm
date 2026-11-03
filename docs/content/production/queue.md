---
title: Print queue
description: Send whole plates to production and manage waiting and active work.
---

The queue holds physical plate attempts. You can print directly from a part or assembly without creating a production run.

## Send work

Open a part and choose **Print**. Select the number of whole plates, review the plan, and choose **Send to queue**. For an assembly's direct Print flow, set whole-plate counts per component; zero skips a component.

For an exact unit target or complete assembly sets, use [Runs](runs.md). For repeat production during idle capacity, use [Continuous backfill](backfill.md).

:::caution Ready printers may start
Queueing is an operational action. A compatible idle printer can take work automatically. Check the physical bed and material path before making work available.
:::

## Follow a job

Open **Queue** and use its status tabs and part-name search. A job page shows the printer, production inputs, resolved profiles, outcome, and available file/actions. Per-printer send progress helps distinguish file preparation/upload from physical printing.

Queue ordering affects selection among eligible jobs. Compatibility is a separate decision: a job near the top may remain blocked while a later compatible job runs.

## Change waiting work

The row action menu exposes eligible operations:

- **Move to top** changes waiting priority.
- **Pin to printer** constrains the destination; **Unpin** restores automatic assignment.
- **Remove job** cancels unsent work after confirmation.

Pinning does not bypass matching. A pinned job waits if its printer is busy, offline, missing material, or incompatible. Open the blocking status or **Job diagnosis** for details.

## Active work and external prints

Pause, resume, and cancel are offered only for eligible jobs and accounts with explicit remote-control permission. Confirm the physical outcome after a stop.

**Printer-started prints** are tracked separately from OpenFarm-dispatched jobs when observable. They can have incomplete file or spool information and do not become queued demand or run progress automatically.
