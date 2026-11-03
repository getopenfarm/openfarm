---
title: Maintenance
description: Track recurring service by elapsed days, print hours, or print count.
---

Maintenance combines reusable task definitions with a separate service cycle for each enrolled printer.

## Define and enroll

1. Open **Maintenance → Add task**.
2. Give the task a name and at least one positive interval: days, print hours, or print count.
3. Choose a warning threshold and the printers to enroll.
4. Save the task. Existing templates can also be added from a printer's maintenance card using **Add tasks**.

Seeded tasks are editable templates. Choose intervals based on the printer and operating conditions. Editing a shared task changes the definition for every enrolled printer.

## Read due state

When a task has several triggers, whichever reaches its interval first controls the due state. The default warning threshold is 80%. **Due soon** warns before the interval; **Overdue** means at least one interval has been reached.

Hours use recorded activity/print durations. A `≥` indicator means the observed hours are a lower bound because some time was untracked. Failed/cancelled prints and observable printer-started prints can contribute to wear.

Overdue maintenance currently **warns only**; it does not block automatic dispatch. Pause or otherwise manage work before physically servicing a printer.

## Record service

After performing service, choose **Mark done** and add an optional note. A backdated service date cannot be in the future or before the current cycle began. The signed-in user is recorded as the performer.

Use **Set last done** to establish a baseline for service performed before tracking, including known carried usage. History is preserved if a task or printer is later removed.

The page includes attention/all views, task definitions, and history. Completing service and managing/removing task definitions require distinct permissions.
