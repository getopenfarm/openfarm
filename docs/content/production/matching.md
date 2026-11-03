---
title: Printer matching
description: Read compatibility checks and fix the reason a queued job cannot start.
---

Open **Job diagnosis** on the job page, or select the blocking status in the queue row. The dialog checks current requirements for each printer. It shows the results and links to the settings you need to change.

## Work through the checks

| Check | What to verify |
| --- | --- |
| Availability | Printer is reachable, not busy, and has been cleared when required. |
| Assignment | A pinned job can use only its selected printer. Queue position and preference are separate from compatibility. |
| Nozzle | Reports from the current connection confirm the required diameter on every head and feed path the job uses. |
| Target model | Pre-sliced files match their declared target model. |
| Material | Required filament is physically loaded and registered as an active tracked spool. |
| Identity | The loaded spool matches the selected production color variant or the material and color requirements saved for the job. |
| Profiles | All jobs need a compatible machine profile and filament-profile assignments for loaded spools. If a job needs slicing, it also needs a process profile. All three presets must have compatible matches in the job's pinned slicer catalog. |
| Build plate | A required slicing plate is selected and compatible. |
| Stock | Known estimated use plus the fixed 10% margin fits available compatible stock. |

## Enough filament

For a 120 g estimate, the initial check requires 132 g, including the 10% margin. Compatible standby spools in the same AMS or configured reloader can contribute stock for continuation. An external spool by itself supplies only its own stock. Standby reloader slots do not count as separate active material choices.

Unknown need does not block the stock check. After slicing, OpenFarm checks again using the generated file's exact grams. If it no longer fits, nothing is sent. Load more stock or correct a measured spool weight, then reevaluate.

## Profile or nozzle mismatch

Use [When profiles apply](../library/profile-behavior.md) to distinguish settings used to slice a new file from profiles checked for an already-sliced file. Pre-sliced jobs do not apply your current process and filament settings to existing toolpaths.

A saved nozzle size from an offline printer is not current confirmation. Reconnect the printer and wait for a fresh nozzle report. Select a compatible machine profile, then check the filament assignments for that hardware and nozzle size.

Changing the farm default does not change an existing part's saved slicer and version. If slicing is needed, a profile missing from that slicer's catalog can block the job. A user preset from another slicer can also block it. A similar name in another catalog is not enough.

## Matching passed but sending stopped

**Last attempt failed** reports an error after the job passed compatibility checks, such as slicing or file transfer. Inspect the reason and server logs. Repeated dispatch failures use a growing retry delay. Opening or refreshing diagnosis is read-only and does not itself dispatch the job.
