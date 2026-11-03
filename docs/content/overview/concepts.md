---
title: Key concepts
description: Understand the records that connect a product definition to a physical print.
---

The normal workflow is: define a part, load a compatible spool, request output, and let a ready printer take the resulting job.

## Library and material

| Record | What it represents |
| --- | --- |
| Part | A saved source file or selected plate, its print requirements, and production filament choices. |
| Assembly | A product definition containing parts and sub-assemblies with quantities. Cost items can be added separately. |
| Filament and color | A catalog material identity and its color variants, prices, and profile assignments. |
| Spool | A physical roll, with remaining weight, purchase details, and usage history. |
| Feed unit and slot | The printer's material path: AMS, external spool, toolheads, or a configured reloader. A tracked spool is loaded into a slot. |
| Profile | Slicer settings for a machine, process, or filament. Profiles are separate from physical inventory. |

## Demand, jobs, and runs

**Production demand** describes what you want made. It can exist without a run. A **print job** is one physical plate attempt, assigned automatically or pinned to a printer. A **run** groups finite demand to track an order or batch.

Parts can be requested in whole plates or unit targets; assemblies in a run are requested in complete sets. Exact unit targets can require a partial final plate, which needs reliable object identities. [Runs](../production/runs.md) explains this planning flow.

**Continuous backfill** makes whole plates when compatible capacity is idle, after finite work. It has its own pause and concurrency controls.

## Readiness has several layers

A saved part can be incomplete. A complete part can still have no matching printer. A matching printer can be busy or waiting for bed clearance.

Production checks connectivity, availability, confirmed nozzle sizes, file compatibility, tracked loaded spools, profiles, and stock where estimates are known. Use [Job diagnosis](../production/matching.md) to see which layer is blocking work.

## Recorded work and current configuration

Finite jobs capture production inputs when queued; running jobs keep their captured inputs. Backfill follows current part inputs until dispatch. Printer-specific settings remain configuration dependencies and are checked before sending.

Editing a library record does not rewrite a completed print's material history. Requeue uses retained production inputs; [Outcomes and recovery](../production/outcomes.md) covers the limits.
