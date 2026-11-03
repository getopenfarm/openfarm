---
title: Supported printers
description: The printer models currently registered in OpenFarm's hardware catalog.
---

OpenFarm uses its hardware catalog to select the printer connection software. Only models in this catalog are supported. A similar model is not automatically compatible.

## Current catalog

| Brand | Models | Connection |
| --- | --- | --- |
| Bambu Lab | X1, X1 Carbon (`X1C`), X1E, P1P, P1S, A1, A1 mini (`A1MINI`) | Direct LAN, access code required. |
| Snapmaker | U1 | Moonraker over LAN; API key when required by the printer. |
| System | Simulation | Software simulator for development, not physical hardware. |

This list follows `PrinterModelCatalog` in the codebase. Newer Bambu models are not implicitly supported.

Camera support is separate: P1/A1 snapshot cameras and the U1 HTTP camera are supported. X1-family RTSP relay is not implemented in the current application.

## Before adding a printer

- Make the printer reachable from the OpenFarm server, even if you use the web UI from another machine.
- Reserve its LAN address where possible.
- Prepare [Bambu LAN access](bambu.md) or [Snapmaker Moonraker access](snapmaker.md).
- Match the physical nozzle and build plate to production configuration.

If discovery does not find a printer, use manual entry on **Add printer**. Discovery needs local broadcast or multicast traffic. A direct connection by IP address can still work when discovery is unavailable.
