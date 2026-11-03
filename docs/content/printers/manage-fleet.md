---
title: Manage the fleet
description: Read printer status, configure profiles, and clear printers for the next job.
---

Use **Printers** for the fleet list and open a printer for its camera, current job, hardware, feed units, maintenance, profiles, and recent activity.

## Connectivity and state

Reachability is separate from operational status. A printer can be unreachable while OpenFarm still remembers that its bed needs clearing.

- **Idle**: no active print is reported.
- **Printing / Paused**: an active print is running or paused.
- **Awaiting clear**: the printer finished or stopped a print and needs operator clearance.
- **Offline**: the server cannot currently reach the printer.

Check the printer-reported state and alerts as well as OpenFarm's status. Camera images are snapshots refreshed while visible, not a recording archive.

## Mark ready

Remove the finished/failed output, inspect the bed and material path, and then choose **Mark ready**. This returns an awaiting-clear printer to availability. Marking it ready may allow queued work to start automatically.

## Edit configuration

Use **Edit** to change the name, connection settings, profiles, and compatible build plate. Changing connection information triggers verification. Select a machine profile only after current telemetry confirms the relevant nozzle size.

Machine/process profiles generate new toolpaths only for files that need slicing. A machine profile also participates in already-sliced job validation. See [When profiles apply](../library/profile-behavior.md) before expecting a profile change to alter a G-code or sliced 3MF file.

A last-known nozzle value can remain visible while offline; production needs confirmation from the current connection. See [Printer matching](../production/matching.md) when the displayed size seems correct but a job is blocked.

## Remove a printer

Removal requires the corresponding permission and confirmation. It is not a way to pause scheduling. Resolve active work first; removing a printer ends its open tracked print as failed and preserves historical print records.
