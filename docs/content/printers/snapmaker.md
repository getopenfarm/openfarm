---
title: Connect Snapmaker U1
description: Discover or register a U1 through its LAN Moonraker interface.
---

OpenFarm connects to the U1's existing Moonraker API over HTTP and WebSocket on port **7125**. No extra agent is installed on the printer.

## Add the printer

1. Put the OpenFarm server on a network that can reach the U1.
2. Open **Printers → Add printer**.
3. Select the discovered U1. For manual entry, enter its IP address with brand **Snapmaker**, model **U1**.
4. Enter a name.
5. If the printer requires an API key, enter it.
6. Add the printer.

OpenFarm reads the serial number from the printer and verifies its identity.

Discovery uses mDNS on UDP **5353**. If discovery finds no printer, you can still connect manually. DHCP can assign a saved IP address to a different U1. If its serial number does not match, OpenFarm keeps the printer offline. Correct the saved IP address.

## Configure slicing and toolheads

Use **OrcaSlicer** for U1 production; Bambu Studio does not provide U1 presets. Choose the compatible machine and process profiles and the supported high-temperature build plate.

The U1 appears with four toolhead slots **T1–T4**. Load tracked spools into their actual slots. Each required filament must be on a head with the nozzle size the part needs. Heads can have different nozzle sizes; a preset at one diameter does not make every head compatible.

OpenFarm uploads G-code and starts the print through Moonraker's local-print method. This requires **firmware 1.3 or later**. A successful connection check does not confirm that every print operation works on that firmware version.

## Confirm physical behavior

Review the [outcome and object-skipping guide](../production/outcomes.md) before you use partial plates or live exclusions. OpenFarm can count compatible standby stock toward the filament available for a print. Confirm that the U1's physical auto-replenish settings can use that stock.
