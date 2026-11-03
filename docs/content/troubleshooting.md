---
title: Troubleshooting
description: Resolve common setup, connection, matching, and output-recovery problems.
---

Start with the affected printer/job page and the specific error. Avoid changing multiple unrelated settings before checking the result.

## Sign-in does not persist

If `ORIGIN` is HTTPS, use that hostname for sign-in. Direct HTTP LAN access cannot send Secure refresh cookies. Route `/api` and `/hubs` through the same hostname and confirm the configured origin has no path/query.

An existing account's password is not changed by editing `Seed__AdminPassword`. Ask another administrator to reset it. See [Accounts](administration/accounts.md).

## Database startup fails

For native development, check the effective `ConnectionStrings__Default` includes a password and matches the actual role/port. `dev.sh` does not load Compose `.env` files. PostgreSQL authentication failures occur before migrations; recreating tables does not fix credentials.

For Docker, initialization variables do not change passwords in an existing volume. Check the original credentials and retain its data. If a native PostgreSQL service already uses port 5432, resolve the port conflict before starting the supplied host-network stack.

## No printers are discovered

Discovery happens on the **server's** LAN, not your browser's. Check Bambu UDP 2021/1990 or U1 mDNS UDP 5353 and the host/container network mode. Bridged containers may not receive multicast. Try manual IP entry if unicast connectivity works.

## Printer is offline or a camera is stale

Check power, IP/DHCP reservation, credentials, model support, and the relevant [Bambu](printers/bambu.md) or [U1](printers/snapmaker.md) ports. Status, file transfer, and camera use distinct connections. A U1 serial mismatch can indicate that DHCP reassigned the IP.

X1-family camera views are unavailable because the current app has no RTSP relay; opening the camera port does not enable that feature.

If all browser live updates stop together, check WebSockets for `/hubs/farm` and sign in again if the browser session expired.

## Job has no matching printer

Open **Job diagnosis**. Work through [Printer matching](production/matching.md): physical bed clearance, assignment, confirmed nozzle, tracked loaded spool/color, profiles, plate, and estimated stock plus its 10% margin.

A printer-reported material alone is not a registered spool. An offline remembered nozzle is not current confirmation. Pinning does not bypass either check.

## Profiles are missing or incompatible

Check **Settings → Slicing defaults**, bundled catalogs, the part's exact slicer pin, and the model/nozzle assignment for each filament. Native development needs `./dev.sh slicers` and `./dev.sh ingest`. A U1 needs OrcaSlicer presets, not a Bambu Studio pin.

Do not assume a similarly named profile in another engine is interchangeable. Missing native twins and cross-engine user presets block dispatch.

## Upload has no estimate or is incomplete

STL/geometry-only 3MF can be saved without estimates. Missing time/grams remain unknown. Review required material/nozzle fields, finish the background review, and reprocess explicitly when correcting a source file or quantity. See [Parts](library/parts.md).

## Partial plate or output confirmation is blocked

Reliable native object identities are required. Bare Bambu G-code may have counts without a usable skip table. Inspect the physical plate before **Confirm produced units**. An uncertain start must be checked at the printer before **Confirm print did not start**. See [Recovery](production/outcomes.md).

## Material use or cost is missing

Check **Action required** entries, spool assignment, remaining weight, and file estimates. Missing deduction is not zero consumption. Use a measured correction/manual usage where appropriate, avoiding duplicate deductions. Blank prices produce partial costs.

## Get diagnostic logs

For the supplied deployment:

```bash
docker compose logs --tail=200 openfarm
docker compose logs --tail=100 postgres
```

Record the error, affected job/printer, and time. Remove credentials and sensitive configuration before sharing. Use [Support](support.md) to report a reproducible issue.
