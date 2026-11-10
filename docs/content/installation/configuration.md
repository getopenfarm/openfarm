---
title: Configuration
description: Configure the public address, AI assistant sign-in, saved keys, database, and number of simultaneous slicing operations.
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
| `ORIGIN` | Public HTTP/HTTPS origin used for authentication, trusted browser origins and the MCP server URL. |
| `OAUTH_ENABLED` | `true` lets AI assistants sign in with OAuth. Default: off. Needs an HTTPS `ORIGIN`. See [AI assistant sign-in](#ai-assistant-sign-in). |
| `OAuth__RegisterPermitLimit`, `OAuth__PermitLimit` | Sign-in requests allowed per minute from one address: app registrations (default `10`) and other sign-in requests (default `60`). |
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

## AI assistant sign-in

AI assistants always connect to `/mcp` with a personal access token. To let them sign in with your OpenFarm account instead (required by Claude and ChatGPT on the web), set:

```dotenv
ORIGIN=https://farm.example.com
OAUTH_ENABLED=true
```

Then recreate the application. Startup stops with one of these errors when the address is not usable:

- `OAUTH_ENABLED requires ORIGIN (for example https://farm.example.com).`
- `OAUTH_ENABLED requires an HTTPS ORIGIN. HTTP is allowed only for localhost.`

The sign-in addresses are always built from `ORIGIN`, never from the request's host or forwarded headers. Route the whole hostname to OpenFarm, including `/mcp`, `/oauth/` and `/.well-known/`. Do not put Cloudflare Access or another login wall in front of `/.well-known/` or `/oauth/token`: assistants call them without a browser.

The request limits count the address that connects to OpenFarm. Behind a tunnel or proxy every assistant shares one count, so raise `OAuth__PermitLimit` if many people connect at once.

Changing `ORIGIN` keeps connected apps, but assistants that saved the old address must be pointed at the new one. Setting `OAUTH_ENABLED=false` stops signed-in apps at once; they work again when you turn it back on. Personal access tokens are not affected by either setting. Setup steps: [AI assistants](../administration/ai-assistants.md#prepare-the-server).

## Keep keys persistent

Preserve `/data/keys` with the database and part files. Losing Data Protection keys makes saved printer credentials unreadable. Replacing the signing key invalidates existing login tokens. See [Upgrades and backups](upgrades-backups.md) before moving an installation.
