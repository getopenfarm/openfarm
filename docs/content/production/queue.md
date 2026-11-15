---
title: Print queue
description: Send whole plates to production and manage waiting and active work.
---

The queue holds physical plate attempts. You can print directly from a part or assembly without creating a production run.

## Send work

Open a part and choose **Print**. Set the number of whole plates with **−** and **+**, check the units, time, and filament in the footer, and choose **Queue *n* plates**. For an assembly's direct Print flow, set whole-plate counts per component; zero skips a component.

The Print dialog also sets:

- **Plates** or **Continuous**: **Continuous** turns the print into [continuous backfill](backfill.md).
- **Printer**: **Automatic** uses any matching printer. A chosen printer pins the jobs to it, and the jobs wait for that printer. The list shows the live state of each printer.
- **Position**: **Bottom** adds the jobs after all waiting jobs. **Top** adds them before all waiting jobs.
- **Filament** (parts only): select a color, any color of one filament, or any color of the material for each requirement. The material stays the part's material. The jobs show the choice as a job override. Choose the reset button to use the part's filament again.

For an exact unit target or complete assembly sets, use [Runs](runs.md). For repeat production during idle capacity, use [Continuous backfill](backfill.md).

:::caution Ready printers may start
A queued job can start automatically on a compatible ready printer. Before you send work to the queue, check the physical bed and material path.
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
