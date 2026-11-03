---
title: Outcomes and recovery
description: Confirm produced units, skip objects, and recover shortfalls without losing attempt history.
---

A completed printer command is not always a verified count of good output. Inspect the physical plate before confirming quantities or clearing the bed.

## Exact quantities and live skipping

OpenFarm can select a partial final plate for finite unit/set targets when native object identities are reliable. Every recognized printable object instance counts as one unit. Modifiers, supports, and shared purge structures are not extra units.

The job's object controls show whether live skipping is available. It requires a supported file identity matching the active printer artifact. Reliable quantity alone does not prove skip support: bare Bambu G-code may count objects but lack the identity table required for exclusions. Prefer a labeled sliced 3MF for that workflow.

Object exclusions use native Bambu or U1 commands. Verify firmware behavior on your hardware before depending on partial plates. Purge and shared structures may still print.

## Confirm physical output

If exclusion confirmation is missing or a print ended while the connection was interrupted, output can remain unverified. Use **Confirm produced units** after inspecting the actual plate. Until confirmation, completed-attempt replacement/write-off actions can be blocked.

Failed and cancelled attempts conservatively record zero produced units. Their material estimates can still count consumed filament.

## Recover the shortfall

- **Requeue missing units** creates a replacement for the unresolved shortfall, retaining the original production snapshot.
- **Write off missing units** accepts the remaining loss without calling it produced stock.

These operations preserve the original attempt and prevent resolving the same loss twice. Existing legacy whole-plate jobs may offer **Requeue plate**.

## When the start acknowledgement is uncertain

An object-aware start with an uncertain acknowledgement is not automatically resent. Check the printer first: it may have started despite the network failure. If it is idle and the bed is clear, use **Confirm print did not start** to cancel that attempt before requeueing.

## Retained execution files

Downloads for sent jobs use the retained execution file, not a new slice against current settings. Bambu's initial exclusions are start-request options: manually printing its downloaded file outside OpenFarm requires applying those exclusions again. U1 execution downloads include their initial exclusion commands.

Filament use remains an estimate that includes purge and extrusion before observed skips. It is not simply proportional to good-unit counts.
