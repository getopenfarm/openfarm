# Maintenance

Maintenance tasks per printer, due by print hours, days or prints. **Due state is computed on read** from printer activity
and `Prints` (`MaintenanceUsageReadModel` + `MaintenanceDue.Evaluate`); nothing is stored per tick and no background
service runs. Warn only: an overdue task never blocks dispatch (yet).

## Definition vs enrollment

- `MaintenanceTask` is the shared definition: unique name, `EveryPrintHours` / `EveryDays` / `EveryPrints` (at least one,
  each > 0; domain + DB CHECK), `WarnAtPercent` (1–99, default 80). Editing it changes every enrolled printer.
- `MaintenanceEnrollment` signs one printer up to one task (unique pair): its own `CycleStartedAtUtc` plus `CarryHours` /
  `CarryPrints` ("usage before tracking", since the last service done outside OpenFarm).
- "Set last done" (`Rebaseline`) moves the cycle start and sets the carries; the UI shows "Baseline <date>" until a log exists.

## Default tasks

- The `MaintenanceTracker` migration seeds 10 tasks after Printago's list (names in `DefaultMaintenance`, fixed ids
  `6d1f0c3a-0000-4000-8000-0000000000NN`, `ON CONFLICT ("Name") DO NOTHING`), all warn at 80 %:

  | Task | Every |
  |---|---|
  | XY-Axis Cleaning & Lubrication (High Use) | 30 d |
  | Air Filter Replacement | 90 d |
  | Carbon Rod Cleaning | 30 d |
  | Z-Axis Lead Screw Lubrication | 90 d |
  | Camera Lens Cleaning | 30 d |
  | XY-Axis Cleaning & Lubrication | 30 d |
  | Full Calibration Run | 100 h |
  | Rail / Axis Lubrication | 100 h |
  | Extruder Gear Cleaning — Standard | 20 prints |
  | Extruder Gear Cleaning — CF Filament | 8 prints |

- They are ordinary tasks: editable and deletable. A deleted default is gone for good; nothing re-creates it.
- "Add tasks" on a printer (`POST /api/v1/printers/{id}/maintenance/enrollments`, `{ taskIds }`) enrolls it in existing
  tasks from now, skipping tasks it is already in. Unknown task or printer: 404. It never creates tasks. Tasks are plain
  templates: no per-model suggestions; the operator picks them (nothing pre-checked) and "New task" covers custom ones.

## Due rules

- Per trigger: `fraction = (used + carry) / interval`. Days = elapsed time / 24 h (no time zones, no DST).
- **Whichever comes first**: the driving trigger is the highest fraction. `Overdue` at ≥ 1, `DueSoon` at ≥ warn %, else `Ok`.
- `Remaining` is signed: negative = overdue by that much.

## Usage counted (cycle start → until)

- **Hours** = max(Printing spans clipped to the window + the open Printing stretch, Σ ended print durations clipped).
  Prints whose end `JobTelemetrySync` inferred write no span, so the print sum covers them.
- Hours are a **lower bound**: time in `Unknown` (printer unseen) is returned as `UntrackedHours` and the UI prefixes "≥".
- **Prints** = `Prints` started in the window with status Completed, Failed or Cancelled. Failed prints still wear the
  machine; external prints (not dispatched by OpenFarm) count too.
- The open stretch grows without a hub event, so the web refetches every 60 s while any printer is printing.

## Completion and backdating

- Mark done takes many enrollments, **all or none** (422 lists the failing ids).
- `performedAt` may be backdated, never in the future nor before the cycle start. Usage in the log is measured up to
  `performedAt`, not now. Dates from clients are normalized to UTC.
- The web sends a backdated day as local start of day, or the cycle start itself when it is that same day.
- `MaintenanceLog` is append-only and survives deletes: task/printer/enrollment FKs are `SetNull`, with name snapshots.
  "Performed by" is the logged-in user; the free-text note is optional.

## Permissions

| Permission | Allows | Seeded to |
|---|---|---|
| `maintenance:view` | Lists, due state, log, bell items | Operators, Viewers |
| `maintenance:complete` | Mark done | Operators |
| `maintenance:manage` | Create/edit tasks, enroll (incl. Add tasks), set last done | Operators |
| `maintenance:delete` | Delete a task (log stays) | Administrators only |

The bell reads maintenance through its own query, so it does not need the notifications permissions.

## Later slices

Out-of-service flag and blocking dispatch when overdue (`Printer.CanTakeWork`), auto-enroll by model, filament kg trigger,
30-day forecast, snooze/skip, parts and cost from the cost catalog, auto-reset on nozzle swap, external alerts (Phase 17),
HMS codes mapped to tasks.
