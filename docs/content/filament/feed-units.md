---
title: Feed units and reloaders
description: Match tracked inventory to AMS slots, external feeds, U1 toolheads, and reloader positions.
---

Feed units describe the printer's physical material paths. Each has slots where tracked spools can be loaded. Open the printer's feed-unit card to inspect and manage them.

## Native feed paths

- Bambu printers expose AMS positions and external spool paths according to their hardware.
- The U1 exposes one four-slot toolhead unit, **T1–T4**, with each slot feeding its corresponding head.
- The spool record and the printer's reported material are separate. Check both when troubleshooting.

## Add a reloader

Use **Add reloader** to describe an external reloader's spool positions. Choose its physical feeding slot and whether it replaces an existing external spool port. The add flow defaults to two slots and an available external port.

If replacing a port with a registered spool, transfer it to the selected reloader slot or return it to storage. Occupied destinations are refused. Only one active reloader may replace the same port.

## Select the feeding slot

The feeding slot is the spool position actually supplying the printer. Change it when the physical path changes. A selection can exist even when the filament has not been registered, but that does not satisfy production's tracked-spool requirement.

Only the selected reloader slot supplies material for job matching. Compatible standby spools can count toward the stock available to continue the print. Port detection reports the replaced printer port. It does not report a separate sensor for each standby slot.

If recorded usage finishes a spool, OpenFarm can select another loaded spool in the same reloader as **estimated** continuation. Check the physical reloader. The record does not confirm that the hardware switched spools successfully.

## Remove a reloader

Removing a reloader returns its tracked spools to storage and keeps their weights and history. OpenFarm archives the reloader, and the original external port becomes available again. Adding it again creates a new unit.
