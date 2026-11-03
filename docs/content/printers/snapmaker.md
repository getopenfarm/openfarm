---
title: Connect Snapmaker U1
description: Discover or register a U1 through its LAN Moonraker interface.
---

OpenFarm connects to the U1's existing Moonraker API over HTTP and WebSocket on port **7125**. No extra agent is installed on the printer.

## Add the printer

1. Put the OpenFarm server on a network that can reach the U1.
2. Open **Printers → Add printer** and select the discovered U1. If necessary, enter its IP manually with brand **Snapmaker**, model **U1**.
3. Enter a name and an API key only if the printer requires one.
4. Add the printer. OpenFarm reads its serial number from the printer and verifies the machine identity.

Discovery uses mDNS on UDP **5353**. Empty discovery does not prevent manual connection. If a DHCP change points a registered IP to another U1, the serial check keeps it offline; correct its IP instead of treating it as the original machine.

## Configure slicing and toolheads

Use **OrcaSlicer** for U1 production; Bambu Studio does not provide U1 presets. Choose the compatible machine and process profiles and the supported high-temperature build plate.

The U1 appears with four toolhead slots **T1–T4**. Load tracked spools into their actual slots. Each required filament must be on a head with the nozzle size the part needs. Heads can have different nozzle sizes; a preset at one diameter does not make every head compatible.

Dispatch uploads G-code and starts the print using Moonraker's local-print method, which requires **firmware 1.3 or later**. A successful connection check alone does not verify all dispatch behavior on a firmware version.

## Confirm physical behavior

Review the [outcome and object-skipping guide](../production/outcomes.md) before relying on partial plates or live exclusions. OpenFarm's tracked continuation stock assumes appropriate printer-side refill behavior; confirm the U1's auto-replenish settings on the physical printer.
