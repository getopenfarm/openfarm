---
title: Maintenance
description: Track recurring service by elapsed days, print hours, or print count.
---

Create a maintenance task once and assign it to printers. Each assigned printer has its own service cycle.

## Define and enroll

1. Open **Maintenance → Add task**.
2. Give the task a name and at least one positive interval: days, print hours, or print count.
3. Choose a warning threshold and the printers to enroll.
4. Save the task. Existing templates can also be added from a printer's maintenance card using **Add tasks**.

Seeded tasks are editable templates. Choose intervals based on the printer and operating conditions. Editing a shared task changes the definition for every enrolled printer.

## Read due state

If a task has several intervals, it becomes due when the first interval is reached. The default warning threshold is 80%. **Due soon** warns before an interval is reached. **Overdue** means at least one interval has been reached.

Hours use recorded printer activity and print durations. A `≥` indicator means actual hours can be higher because OpenFarm could not track some time. Failed prints, cancelled prints, and observed printer-started prints can also contribute to wear.

Overdue maintenance currently **warns only**; it does not block automatic dispatch. Pause or otherwise manage work before physically servicing a printer.

## Record service

After you complete the service, choose **Mark done**. Add a note if necessary. A service date cannot be in the future or before the current cycle began. OpenFarm records the signed-in user as the person who did the service.

Use **Set last done** to record service completed before OpenFarm began tracking it. Include known usage since that service. OpenFarm keeps the history if you later remove the task or printer.

The page has views for tasks needing attention, all tasks, task definitions, and history. Recording completed service requires different permissions from managing or removing task definitions.
