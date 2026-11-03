---
title: Connect Bambu Lab
description: Add a supported Bambu printer using its local IP, serial number, and LAN access code.
---

Bambu printers connect directly over the LAN. OpenFarm does not ask for a Bambu Cloud account.

## Prepare the printer

1. Confirm the model is in the [supported catalog](supported-printers.md).
2. Enable LAN-only access and Developer Mode where required by the printer's firmware.
3. Find its IP address, serial number, and LAN access code in the printer settings.
4. Ensure the OpenFarm server can reach it. Enabling LAN-only mode changes the printer's cloud/app workflow.

## Add it to the fleet

Open **Printers → Add printer**. Select the discovered printer, or choose manual entry and set the brand, model, IP, and serial. Enter a name and access code, then add the printer. OpenFarm verifies the connection before registration.

Credentials stay encrypted on the server. Enter or replace them from the printer's add/edit page.

## Configure production

Wait for live telemetry to confirm the nozzle. Edit the printer to choose a compatible **machine profile**, **process profile**, and **build plate**. Register and [load tracked spools](../filament/spools.md) into the correct AMS or external slots, with compatible filament profile assignments.

Pre-sliced files must match the model they were sliced for. Being able to connect does not mean an arbitrary sliced file is compatible.

## Network checks

- MQTT status/control: TCP **8883**.
- FTPS file transfer: TCP **990**, plus the passive data connections negotiated by the printer.
- Camera: TCP **322** for the cataloged X1 models; **6000** for P1/A1 models.
- Discovery: UDP **2021/1990** on the server's LAN.

A reachable status connection alone does not prove file transfer and camera connectivity. If discovery is empty in a bridged container, add by IP or use the supplied Linux host-network deployment. See [Troubleshooting](../troubleshooting.md).

X1-family cameras use RTSP, which the current application does **not** relay. Those models can connect for status/production while their camera view remains unavailable. P1/A1 snapshot cameras are supported.
