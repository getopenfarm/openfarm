---
title: Manage the fleet
description: Read printer status, configure profiles, and clear printers for the next job.
---

Use **Printers** for the fleet list and open a printer for its camera, current job, hardware, feed units, maintenance, profiles, and recent activity.

## Connectivity and state

Connection status and print status are separate. If a printer disconnects, OpenFarm still keeps its Not ready state.

- **Ready**: no active print is reported, and the printer takes jobs.
- **Printing / Paused**: an active print is running or paused.
- **Not ready**: the printer takes no job until you mark it ready. It becomes Not ready when a print ends (the plate is not confirmed clear) or when you mark it not ready.
- **Offline**: the server cannot currently reach the printer.

Check the printer-reported state and alerts as well as OpenFarm's status. Camera images are snapshots refreshed while visible, not a recording archive.

## Mark ready

1. Remove the finished or failed print.
2. Inspect the bed and material path.
3. Choose **Mark ready**.

The printer becomes available for queued work. A compatible job can then start automatically.

## Mark not ready

To keep a **Ready** printer from taking jobs, for example for a nozzle swap, a bad spool or a repair, open it and choose **Mark not ready**. It stays Not ready, also after a reconnect or a restart, until you choose **Mark ready**. Queued work waits. Use the same permission as Mark ready.

A printer that is printing cannot be marked not ready: it becomes Not ready by itself when its print ends.

## Edit configuration

Use **Edit** to change the name, connection settings, and build plate. Changing connection information triggers verification.

The build plate is a check, not a slicing setting. A job goes only to a printer whose plate matches the file's plate. A ready printer with no plate selected shows **Needs setup** in the fleet list, on the dashboard, and on its page. It takes no jobs until you select a plate. See [When profiles apply](../library/profile-behavior.md).

A last-known nozzle value can remain visible while offline; production needs confirmation from the current connection. See [Printer matching](../production/matching.md) when the displayed size seems correct but a job is blocked.

## Remove a printer

Removal requires the corresponding permission and confirmation. Do not remove a printer to pause its queue. Resolve active work first. If you remove a printer with an open tracked print, OpenFarm records that print as failed. Historical print records remain available.
