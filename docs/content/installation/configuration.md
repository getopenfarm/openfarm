---
title: Configuration
description: Configure the public address, saved keys, database, and number of simultaneous slicing operations.
---

The production image includes default settings. To change them, enter overrides in `.env` next to your Compose file. ASP.NET setting names use double underscores, such as `Seed__AdminPassword`.

## Common settings

| Setting | Purpose |
| --- | --- |
| `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` | Initialize the bundled PostgreSQL database on its first start. |
| `Database__Host` | Database hostname or address. The supplied host-network configuration uses `127.0.0.1`. |
| `Database__Port` | PostgreSQL port, normally `5432`. |
| `ConnectionStrings__Default` | Optional full connection string; takes precedence over the `Database__*` settings. |
| `Seed__AdminUsername`, `Seed__AdminEmail`, `Seed__AdminPassword` | First administrator, created only if needed. |
| `ORIGIN` | Public HTTP/HTTPS origin used for authentication and trusted browser origins. |
| `Jwt__SigningKey` | Optional token signing key. If omitted, OpenFarm generates one and saves it under `/data/keys`. |
| `DataProtection__CertificatePath`, `DataProtection__CertificatePassword` | Optional PKCS#12 certificate protecting stored printer-credential encryption keys at rest. |
| `PUID`, `PGID`, `TZ` | Data ownership and log time zone. |
| `Slicing__MaxConcurrentSlicingOperations` | Maximum number of slicing operations that can run at the same time. Default: `2`. |

Use [deploy/.env.example](https://github.com/getopenfarm/openfarm/blob/main/deploy/.env.example) for the current supported examples. Farm currency and prices are configured in the application under [Settings](../administration/settings.md).

## HTTPS and reverse proxies

For an HTTPS hostname or Cloudflare Tunnel:

```dotenv
ORIGIN=https://farm.example.com
```

Route the **entire hostname**, including `/api` and `/hubs`, to `http://<server-ip>:6868`. The proxy must allow WebSocket connections for live updates. No separate frontend build or API hostname is needed.

`ORIGIN` must contain a scheme and hostname, with an optional port. Do not include a path other than `/`, a query, a fragment, or credentials. An invalid value stops startup.

Apply changed environment settings by recreating the application:

```bash
docker compose up -d --force-recreate openfarm
```

With an HTTPS origin, refresh cookies use the Secure attribute, even if the proxy connects to OpenFarm over HTTP. Sign in through that HTTPS hostname. Those cookies do not work for direct HTTP sign-in over the LAN. Omit `ORIGIN` for trusted local HTTP hosting.

## Keep keys persistent

Preserve `/data/keys` with the database and part files. Losing Data Protection keys makes saved printer credentials unreadable. Replacing the signing key invalidates existing login tokens. See [Upgrades and backups](upgrades-backups.md) before moving an installation.
