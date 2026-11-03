---
title: Stats and material reports
description: Understand production outcomes, printer utilization, and estimated material usage.
---

Open **Stats** to review the farm over a selected date range and compare it with the preceding equivalent period.

## Read the figures

| Figure | Interpretation |
| --- | --- |
| Prints and success | Ended prints that actually started; completed prints divided by completed, failed, and cancelled attempts. |
| Printer time | Recorded printing, awaiting-clear, paused, idle, and offline stretches. Unobserved time appears as no data. |
| Utilization | Printing hours divided by observed hours. Offline time is observed; unknown time is excluded. |
| Queue wait | Time from job creation to start, summarized for jobs that started in the range. |
| Filament and waste | Recorded/estimated material on completed work versus failed/cancelled work. |
| Units made | The current Stats aggregate uses completed jobs' recorded plate quantities; use job/run outcome details to verify exact accepted output. |

## Dates and coverage

The browser's local days determine the range; the API takes an IANA time zone and supports up to 366 days. Prints are bucketed by when they ended, not merely when they were queued.

Time comes from printer activity, while output comes from print records. A print still open does not contribute a completed duration. Server downtime can leave unknown stretches; OpenFarm cannot reconstruct every print that started and ended while it was offline.

## Material costs

Material use is estimated from files and observed progress. Realized material cost uses the price captured when deduction was recorded, rather than silently repricing old prints with today's catalog values. Missing prices or deductions can make figures partial.

Use the filament's **Usage / History**, spool history, and **Action required** print entries to investigate discrepancies. These reports do not measure filament on a scale or record sales revenue.
