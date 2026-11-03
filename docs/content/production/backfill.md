---
title: Continuous backfill
description: Keep spare compatible printer capacity productive after finite work.
---

Backfill is persistent repeat demand. It makes full plates only when compatible capacity is available after finite jobs.

## Start backfill

Open a part or assembly's **Print** flow and enable **Continuous backfill**. Submit the entry, then manage it from **Queue**. It starts with a concurrency limit of one printer at a time.

The entry remains visible even when it has no current jobs. Configure its concurrency cap or optional printer pin, pause/resume it, refresh its recipe, or remove it.

## Assembly balancing

Assembly backfill captures the exploded units-per-set recipe. OpenFarm compares completed and reserved/in-flight units against each component's ratio. It favors the least-covered eligible component below the next complete-set milestone.

This prevents an unavailable component from causing unlimited surplus of every other component. Failed/cancelled jobs do not count as completed output. The scheduler still requires each selected part to match a ready printer.

## Pause, refresh, and remove

- **Pause** blocks new dispatches and retries. It does not physically cancel an already running print.
- **Refresh** adopts the assembly's current BOM, retires old requirements, cancels unsent jobs, and begins a new balancing period. Running prints keep their original recipe attribution.
- **Remove** cancels unsent work and retains running prints and history.

Backfill follows current part file inputs until dispatch. A part-file revision by itself does not require an assembly recipe refresh. Use a finite [run](runs.md) for an order with a fixed quantity and explicit completion.
