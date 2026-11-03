---
title: Manage the fleet
description: Read printer status, configure profiles, and clear printers for the next job.
---

Use **Printers** for the fleet list and open a printer for its camera, current job, hardware, feed units, maintenance, profiles, and recent activity.

## Connectivity and state

Connection status and print status are separate. If a printer disconnects, OpenFarm still keeps its bed-clearance state.

- **Idle**: no active print is reported.
- **Printing / Paused**: an active print is running or paused.
- **Awaiting clear**: the printer finished or stopped a print and needs operator clearance.
- **Offline**: the server cannot currently reach the printer.

Check the printer-reported state and alerts as well as OpenFarm's status. Camera images are snapshots refreshed while visible, not a recording archive.

## Mark ready

1. Remove the finished or failed print.
2. Inspect the bed and material path.
3. Choose **Mark ready**.

The printer becomes available for queued work. A compatible job can then start automatically.

## Edit configuration

Use **Edit** to change the name, connection settings, profiles, and compatible build plate. Changing connection information triggers verification. Select a machine profile only after current telemetry confirms the relevant nozzle size.

Machine and process profiles generate toolpaths only for files that need slicing. Already-sliced jobs still need a compatible machine profile. See [When profiles apply](../library/profile-behavior.md) before expecting a profile change to alter G-code or sliced 3MF toolpaths.

A last-known nozzle value can remain visible while offline; production needs confirmation from the current connection. See [Printer matching](../production/matching.md) when the displayed size seems correct but a job is blocked.

## Remove a printer

Removal requires the corresponding permission and confirmation. Do not remove a printer to pause its queue. Resolve active work first. If you remove a printer with an open tracked print, OpenFarm records that print as failed. Historical print records remain available.
