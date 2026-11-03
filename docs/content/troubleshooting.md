---
title: Troubleshooting
description: Resolve common setup, connection, matching, and output-recovery problems.
---

Open the affected printer or job page and read the error. Change one relevant setting at a time, then check the result.

## Sign-in does not persist

If `ORIGIN` uses HTTPS, sign in through that hostname. Direct HTTP LAN access cannot send Secure refresh cookies. Route `/api` and `/hubs` through the same hostname. Confirm that the configured origin has no path other than `/` and no query.

An existing account's password is not changed by editing `Seed__AdminPassword`. Ask another administrator to reset it. See [Accounts](administration/accounts.md).

## Database startup fails

For local development, check that `ConnectionStrings__Default` includes a password and matches the database user and port. `dev.sh` does not load Compose `.env` files. PostgreSQL checks authentication before it runs migrations. Recreating tables does not fix incorrect credentials.

For Docker, initialization variables do not change passwords in an existing volume. Check the original credentials and keep the volume's data. If a PostgreSQL service already uses port 5432, resolve the conflict before starting the supplied host-network configuration.

## No printers are discovered

Discovery happens on the **server's** LAN. The browser's network does not control it. Check Bambu UDP 2021/1990 or U1 mDNS UDP 5353, as applicable. Also check the host and container network settings. Bridged containers might not receive multicast traffic. If a direct connection works, enter the printer's IP address manually.

## Printer is offline or a camera is stale

Check power, the IP address, DHCP reservation, credentials, and model support. Then check the relevant [Bambu](printers/bambu.md) or [U1](printers/snapmaker.md) ports. Status, file transfer, and camera use separate connections. A U1 serial mismatch can indicate that DHCP reassigned the IP address.

X1-family camera views are unavailable because the current app has no RTSP relay; opening the camera port does not enable that feature.

If all browser live updates stop together, check WebSockets for `/hubs/farm` and sign in again if the browser session expired.

## Job has no matching printer

Open **Job diagnosis** and work through [Printer matching](production/matching.md). Check bed clearance, printer assignment, confirmed nozzle, loaded tracked spool and color, profiles, and plate. Known estimated use must fit the available stock with the 10% margin.

A printer-reported material alone is not a registered spool. An offline remembered nozzle is not current confirmation. Pinning does not bypass either check.

## Profiles are missing or incompatible

Check **Settings → Slicing defaults** and the installed profile catalogs. Confirm the part's saved slicer and version, then check each filament's assignment for the printer model and nozzle size. For local development, run `./dev.sh slicers` and `./dev.sh ingest`. U1 production needs an OrcaSlicer selection and compatible U1 presets. Bambu Studio does not provide those presets.

If slicing is needed, each selected profile must have a compatible match in the pinned slicer's catalog. A similar name in another catalog is not enough. A user preset from another slicer also blocks slicing.

## Upload has no estimate or is incomplete

You can save STL and geometry-only 3MF files without estimates. Missing time or filament weight remains unknown. Review the required material and nozzle fields, then finish the file review. If you correct the source file or quantity, reprocess the part. See [Parts](library/parts.md).

## Partial plate or output confirmation is blocked

The file must contain reliable object identifiers. Raw Bambu G-code can have object counts without the table needed to skip objects. Before using **Confirm produced units**, inspect the physical plate. If the start result is uncertain, check the printer before using **Confirm print did not start**. See [Recovery](production/outcomes.md).

## Material use or cost is missing

Check **Action required** entries, the spool assignment, remaining weight, and file estimates. A missing deduction does not mean no filament was used. If necessary, correct the weight from a measurement or record usage manually. Do not deduct the same usage twice. Blank prices leave costs partial.

## Get diagnostic logs

For the supplied deployment:

```bash
docker compose logs --tail=200 openfarm
docker compose logs --tail=100 postgres
```

Record the error, affected job or printer, and time. Remove credentials and sensitive configuration before sharing. Use [Support](support.md) to report a reproducible issue.
