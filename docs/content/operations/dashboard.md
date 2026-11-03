---
title: Dashboard and activity
description: Monitor the live farm and use the activity log to understand changes.
---

The **Dashboard** shows camera tiles, current prints, and printer warnings. Use it to find machines that need attention. Open the printer or job page to use its controls.

## Live status and cameras

Each browser tab uses one live connection for printer and job updates. OpenFarm refreshes camera images while they are visible. The images are periodic snapshots, not video recordings.

If a camera image stops updating, check the camera connection separately from MQTT or Moonraker status. If all live updates stop, check the browser connection and the reverse proxy's WebSocket support for `/hubs/farm`.

## Finished work

Inspect and remove the printed output before you mark the printer ready. A finished tile means you must inspect and clear the bed. It does not confirm that every unit is acceptable. See [Outcomes and recovery](../production/outcomes.md) for output confirmation and replacements.

## Activity log

Users with audit access can open **Activity log** under Administration. The log shows recorded changes and who made them. Use it to investigate configuration updates, printer events, job actions, and skipped material deductions.

The activity log is not the source for utilization hours. [Stats](stats.md) uses dedicated printer-activity history and print records, including gaps when the server could not observe a machine.
