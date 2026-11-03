---
title: Stats and material reports
description: Understand production outcomes, printer utilization, and estimated material usage.
---

Open **Stats** to review the farm over a selected date range and compare it with the preceding equivalent period.

## Read the figures

| Figure | Interpretation |
| --- | --- |
| Prints and success | Includes ended prints that actually started. Success rate is completed prints divided by all completed, failed, and cancelled attempts. |
| Printer time | Recorded periods of printing, awaiting-clear, paused, idle, and offline status. Unobserved time appears as no data. |
| Utilization | Printing hours divided by observed hours. Offline time is observed; unknown time is excluded. |
| Queue wait | Time from job creation to start, summarized for jobs that started in the range. |
| Filament and waste | Compares recorded or estimated material use for completed work with material use for failed and cancelled work. |
| Units made | Stats uses the recorded plate quantities of completed jobs. Use job and run outcome details to verify the exact accepted output. |

## Dates and coverage

The date range uses local days in the browser's time zone. The API accepts an IANA time zone and supports up to 366 days. OpenFarm groups prints by their end date, not their queue date.

Time comes from printer activity. Output comes from print records. A print that has not ended does not contribute a completed duration. Server downtime can leave periods with no data. OpenFarm cannot reconstruct every print that started and ended while the server was offline.

## Material costs

OpenFarm estimates material use from files and observed print progress. Recorded material cost uses the price saved when OpenFarm deducted the usage. Later catalog price changes do not change that recorded cost. Missing prices or deductions can leave the report incomplete.

Use the filament's **Usage / History**, spool history, and **Action required** print entries to investigate discrepancies. These reports do not measure filament on a scale or record sales revenue.
