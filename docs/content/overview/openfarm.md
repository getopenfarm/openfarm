---
title: What is OpenFarm?
description: A self-hosted workspace for printers, filament, parts, and production.
---

OpenFarm manages a 3D print farm from one web application. You run it on your own server, keep the database and files, and connect directly to supported printers on your LAN.

## What you can do

- Monitor the fleet, printer status, cameras, and current work.
- Track filament colors, physical spools, loaded slots, usage, and estimated material costs.
- Store parts, import slicer profiles, and build nested assemblies.
- Queue plates directly, plan finite runs, and fill spare capacity with continuous backfill.
- Diagnose why a printer cannot take a job, then follow the link to its configuration.
- Track maintenance, production statistics, cost estimates, and product pricing.
- Give team members specific permissions and issue scoped API tokens.

## One installation, one farm

Each installation serves one farm. Run **one application instance** with PostgreSQL. That instance manages printer connections and sends jobs to printers. You can use the browser remotely, but the server must have network access to the printers.

See [Installation](../installation/docker.md) for deployment and [Supported printers](../printers/supported-printers.md) for the actual model catalog.

## Find your workflow

The application groups daily work under **Operate** and reusable records under **Library**. Settings and account administration are separate.

Start with the [Quick start](../quick-start.md), then read [Key concepts](concepts.md) when you need to understand how parts, spools, jobs, and runs relate.
