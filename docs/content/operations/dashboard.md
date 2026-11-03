---
title: Dashboard and activity
description: Monitor the live farm and use the activity log to understand changes.
---

The **Dashboard** provides a live fleet overview with camera tiles, current print information, and printer warnings. Use it to see which machine needs attention, then open that printer or job for its operational controls.

## Live status and cameras

Printer and job changes arrive over a shared live connection in each browser tab. Camera images are pulled while visible; they are periodic snapshots rather than a full-motion recording service.

If a camera is stale, check reachability and camera connectivity separately from MQTT/Moonraker status. If all live updates stop, check the browser connection and reverse proxy's WebSocket handling for `/hubs/farm`.

## Finished work

Inspect and remove output before marking the printer ready. A finished tile is an invitation to complete the physical clearance workflow, not proof that every unit is acceptable. [Outcomes and recovery](../production/outcomes.md) covers unverified output and replacements.

## Activity log

Users with audit access can open **Activity log** under Administration to review recorded changes and their actors. It helps trace configuration updates, printer events, job actions, and skipped material deductions.

The activity log is not the source for utilization hours. [Stats](stats.md) uses dedicated printer-activity history and print records, including gaps when the server could not observe a machine.
