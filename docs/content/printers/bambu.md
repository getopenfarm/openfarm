---
title: Connect Bambu Lab
description: Add a supported Bambu printer using its local IP, serial number, and LAN access code.
---

Bambu printers connect directly over the LAN. OpenFarm does not ask for a Bambu Cloud account.

## Prepare the printer

1. Confirm the model is in the [supported catalog](supported-printers.md).
2. Enable LAN-only access and Developer Mode where required by the printer's firmware.
3. Find its IP address, serial number, and LAN access code in the printer settings.
4. Make sure the OpenFarm server can reach the printer.

Enabling LAN-only mode changes how you use the printer with Bambu Cloud and its apps.

## Add it to the fleet

1. Open **Printers → Add printer**.
2. Select the discovered printer. For manual entry, set the brand, model, IP address, and serial number.
3. Enter a name and the LAN access code.
4. Add the printer.

OpenFarm verifies the connection before saving the printer.

Credentials stay encrypted on the server. Enter or replace them from the printer's add/edit page.

## Configure production

Wait for the connected printer to report its nozzle size. Edit the printer and select a compatible **machine profile**, **process profile**, and **build plate**. Register the physical spools and assign compatible filament profiles. Then [load the tracked spools](../filament/spools.md) into the correct AMS or external slots.

Pre-sliced files must match the model they were sliced for. Being able to connect does not mean an arbitrary sliced file is compatible.

## Network checks

- MQTT status/control: TCP **8883**.
- FTPS file transfer: TCP **990**, plus the passive data connections negotiated by the printer.
- Camera: TCP **322** for the cataloged X1 models; **6000** for P1/A1 models.
- Discovery: UDP **2021/1990** on the server's LAN.

A working status connection does not confirm that file transfer and camera connections also work. If a bridged container cannot discover printers, add the printer by IP address. You can also use the supplied Linux host-network configuration. See [Troubleshooting](../troubleshooting.md).

X1-family cameras use RTSP, which the current application does **not** relay. Those models can connect for status/production while their camera view remains unavailable. P1/A1 snapshot cameras are supported.
