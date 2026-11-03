---
title: Outcomes and recovery
description: Confirm produced units, skip objects, and recover shortfalls without losing attempt history.
---

A completed printer command is not always a verified count of good output. Inspect the physical plate before confirming quantities or clearing the bed.

## Exact quantities and live skipping

For a fixed target of units or sets, OpenFarm can select a partial final plate if the file has reliable object identifiers. Every recognized printable object instance counts as one unit. Modifiers, supports, and shared purge structures are not extra units.

The job's object controls show whether live skipping is available. OpenFarm needs supported object identifiers that match the file used for the active print. A reliable object count alone is not enough. For example, raw Bambu G-code can contain object counts but lack the identifier table needed to skip objects. Prefer a labeled sliced 3MF for that workflow.

Object exclusions use native Bambu or U1 commands. Verify firmware behavior on your hardware before depending on partial plates. Purge and shared structures may still print.

## Confirm physical output

Output can remain unverified if object-skip confirmation is missing or the print ended during a connection interruption. Inspect the actual plate before you use **Confirm produced units**. Until you confirm the output, OpenFarm can block replacement or write-off actions for that completed attempt.

Failed and cancelled attempts conservatively record zero produced units. Their material estimates can still count consumed filament.

## Recover the shortfall

- **Requeue missing units** creates a replacement for the unresolved shortfall, retaining the original production snapshot.
- **Write off missing units** accepts the remaining loss without calling it produced stock.

These operations preserve the original attempt and prevent resolving the same loss twice. Existing legacy whole-plate jobs may offer **Requeue plate**.

## When the start acknowledgement is uncertain

If OpenFarm cannot confirm a start request that uses object controls, it does not automatically send the request again. Check the printer first. The print might have started despite the network failure. If the printer is idle and the bed is clear, use **Confirm print did not start**. This cancels the attempt so you can requeue it.

## Retained execution files

For sent jobs, downloads use the saved file prepared for that job. OpenFarm does not slice it again with current settings. For Bambu printers, the start request specifies the initial object exclusions. If you print the downloaded file outside OpenFarm, apply those exclusions again. U1 downloads already include their initial exclusion commands.

Filament use remains an estimate that includes purge and extrusion before observed skips. It is not simply proportional to good-unit counts.
