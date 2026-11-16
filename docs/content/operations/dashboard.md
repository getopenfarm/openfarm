---
title: Dashboard and audit log
description: Monitor the live farm and use the audit log to understand changes.
---

The **Dashboard** shows camera tiles, current prints, and printer warnings. Use it to find machines that need attention. Open the printer or job page to use its controls.

## Live status and cameras

Each browser tab uses one live connection for printer and job updates. OpenFarm refreshes camera images while they are visible. The images are periodic snapshots, not video recordings.

If a camera image stops updating, check the camera connection separately from MQTT or Moonraker status. If all live updates stop, check the browser connection and the reverse proxy's WebSocket support for `/hubs/farm`.

## Finished work

Inspect and remove the printed output before you mark the printer ready. A finished tile means you must inspect and clear the bed. It does not confirm that every unit is acceptable. See [Outcomes and recovery](../production/outcomes.md) for output confirmation and replacements.

## Audit log

Users with audit access can open **Audit log** under Administration. The table shows when a change happened, who made it, the affected record, and what changed. Filter by person, channel, token or app, record type, action, or date range.

Select **Everything** to see all recorded changes. Select **Tokens and apps** to focus on changes made through those channels. **Type** groups the record type and action filters. **Date** opens the **From** and **To** controls.

Open an entry to inspect its recorded field values before and after the change. The entry also shows other changes from the same request. Actor and record names are saved with new entries. Historical entries can have missing names or field details.

Printer status reports and alert changes remain in **Recent activity** on each printer page. Operator actions, such as **Mark ready**, also appear in the audit log. Automatic changes to jobs, production, and inventory identify **System** as the actor.

The audit log is not the source for utilization hours. [Stats](stats.md) uses dedicated printer-activity history and print records, including gaps when the server could not observe a machine.
