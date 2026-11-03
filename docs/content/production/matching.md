---
title: Printer matching
description: Read compatibility checks and fix the reason a queued job cannot start.
---

Use **Job diagnosis** on the job page or the queue row's blocking-status link. The dialog evaluates current requirements and shows each printer's checks with links to the relevant configuration.

## Work through the checks

| Check | What to verify |
| --- | --- |
| Availability | Printer is reachable, not busy, and has been cleared when required. |
| Assignment | A pinned job can use only its selected printer. Queue position and preference are separate from compatibility. |
| Nozzle | Current connection telemetry confirms the required diameter on every head/feed path the job uses. |
| File/model | Pre-sliced files match their declared target model. |
| Material | Required filament is physically loaded and registered as an active tracked spool. |
| Identity | The loaded spool matches the part/job's effective production color variant or applicable material/color constraints. |
| Profiles | Machine, process where slicing is needed, and every required filament profile resolve compatibly in the pinned slicer catalog. |
| Build plate | A required slicing plate is selected and compatible. |
| Stock | Known estimated use plus the fixed 10% margin fits available compatible stock. |

## Enough filament

With a 120 g requirement, the initial check needs 132 g. Compatible continuation stock can contribute through the same AMS or configured reloader path. An external spool by itself covers only its own stock. Standby reloader slots do not count as separate active material choices.

Unknown need does not block the stock check. After slicing, OpenFarm checks again using the generated file's exact grams. If it no longer fits, nothing is sent. Load more stock or correct a measured spool weight, then reevaluate.

## Profile or nozzle mismatch

Do not rely on an offline last-known nozzle or a profile name that merely looks right. Reconnect telemetry, choose the compatible machine profile, and check filament assignments for that hardware/nozzle group.

A changed farm default does not migrate an existing part's engine/version pin. Missing native profiles or cross-engine user presets can block production even when another catalog contains a similar name.

## Matching passed but sending stopped

**Last attempt failed** reports an error after the job passed compatibility checks, such as slicing or file transfer. Inspect the reason and server logs. Repeated dispatch failures use a growing retry delay. Opening or refreshing diagnosis is read-only and does not itself dispatch the job.
