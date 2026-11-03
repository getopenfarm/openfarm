---
title: Feed units and reloaders
description: Match tracked inventory to AMS slots, external feeds, U1 toolheads, and reloader positions.
---

Feed units describe the printer's physical material paths. Each has slots where tracked spools can be loaded. Open the printer's feed-unit card to inspect and manage them.

## Native feed paths

- Bambu printers expose AMS positions and external spool paths according to their hardware.
- The U1 exposes one four-slot toolhead unit, **T1–T4**, with each slot feeding its corresponding head.
- A registered spool and printer telemetry are separate observations. Confirm both when troubleshooting.

## Add a reloader

Use **Add reloader** to describe an external reloader's spool positions. Choose its physical feeding slot and whether it replaces an existing external spool port. The add flow defaults to two slots and an available external port.

If replacing a port with a registered spool, transfer it to the selected reloader slot or return it to storage. Occupied destinations are refused. Only one active reloader may replace the same port.

## Select the feeding slot

The feeding slot is the spool position actually supplying the printer. Change it when the physical path changes. A selection can exist even when the filament has not been registered, but that does not satisfy production's tracked-spool requirement.

Standby reloader positions are not independent active material sources. Readiness uses the selected slot; compatible continuation stock can contribute to the enough-filament check. Port detection describes the replaced printer port, not separate sensors on every standby slot.

When recorded depletion finishes a spool, OpenFarm may select a loaded sibling as **estimated** continuation. Check the physical reloader; bookkeeping does not prove that hardware switched successfully.

## Remove a reloader

Removal returns its tracked spools to storage, preserves their weights/history, and archives the reloader. The original external port becomes available again. Re-adding creates a new unit.
