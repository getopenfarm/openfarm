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
| `ORIGIN` | Optional deployment override for **Settings → Server → Public URL**. Locks the field while set. |
| `MCP_MODE` | Optional deployment override: `Disabled`, `AccessTokens`, or `SignIn`. Locks MCP mode in settings. |
| `MCP_ENABLED` | Optional deployment override. `true` enables MCP; `false` disables all MCP and OAuth access. Unset leaves control in Settings. |
| `OAUTH_ENABLED` | Legacy upgrade setting. An explicit `true` imports sign-in mode once when installation settings are first created. Requires HTTPS `ORIGIN`. |
| `OAuth__RegisterPermitLimit`, `OAuth__PermitLimit` | Sign-in requests allowed per minute from one address: app registrations (default `10`) and other sign-in requests (default `60`). |
| `Jwt__SigningKey` | Optional token signing key. If omitted, OpenFarm generates one and saves it under `/data/keys`. |
| `DataProtection__CertificatePath`, `DataProtection__CertificatePassword` | Optional PKCS#12 certificate protecting stored printer-credential encryption keys at rest. |
| `PUID`, `PGID`, `TZ` | Data ownership and log time zone. |
| `Slicing__MaxConcurrentSlicingOperations` | Maximum number of slicing operations that can run at the same time. Default: `2`. |

Use [deploy/.env.example](https://github.com/getopenfarm/openfarm/blob/main/deploy/.env.example) for the current supported examples. Farm currency and prices are configured in the application under [Settings](../administration/settings.md).

## HTTPS and reverse proxies

Set the **Public URL** under **Settings → Server**. For a deployment-controlled HTTPS hostname or Cloudflare Tunnel, you can instead set:

```dotenv
ORIGIN=https://farm.example.com
```

Route the **entire hostname**, including `/api` and `/hubs`, to `http://<server-ip>:6868`. The proxy must allow WebSocket connections for live updates. No separate frontend build or API hostname is needed.

`ORIGIN` must contain a scheme and hostname, with an optional port. Do not include a path other than `/`, a query, a fragment, or credentials. An invalid value stops startup.

Apply changed environment settings by recreating the application:

```bash
docker compose up -d --force-recreate openfarm
```

With an HTTPS origin, refresh cookies use the Secure attribute, even if the proxy connects to OpenFarm over HTTP. Sign in through that HTTPS hostname. Those cookies do not work for direct HTTP sign-in over the LAN. For trusted local HTTP hosting, leave the saved public URL blank and omit `ORIGIN`. Removing an override does not clear the saved URL.

## AI assistant sign-in

MCP is disabled by default. Administrators can enable **Access tokens only** or **Sign-in and access tokens** under **Settings → AI assistants**. The second mode requires an HTTPS public URL, except for localhost development. Changes in Settings apply without restarting.

For automated deployments, use:

```dotenv
ORIGIN=https://farm.example.com
MCP_MODE=SignIn
```

`ORIGIN` overrides the saved public URL. `MCP_MODE` overrides the saved mode. `MCP_ENABLED=true` enables access tokens when the saved mode is disabled; otherwise it keeps the enabled mode. Use `MCP_MODE=SignIn` to select OAuth. `MCP_ENABLED=false` disables every mode. An explicit availability or mode override makes the MCP control read-only. Leave both unset to manage MCP in Settings. Do not combine `MCP_ENABLED=true` with `MCP_MODE=Disabled`.

Environment changes require recreating the application. Changing deployment policy invalidates existing signed-in apps and pending connections on startup. Keep the deployment settings consistent when replacing the application instance.

The public URL supplies sign-in addresses and token identity. OpenFarm does not derive them from request hosts or forwarded headers. Route `/mcp`, `/oauth/` and `/.well-known/` to OpenFarm. Do not place a login wall in front of discovery or `/oauth/token`.

The request limits count the address that connects to OpenFarm. Behind a proxy, assistants can share one count. Adjust `OAuth__PermitLimit` if necessary.

### Upgrade from environment-only sign-in

When the new installation settings are first created, an explicit `OAUTH_ENABLED=true` and valid `ORIGIN` preserve sign-in and existing app grants. This import runs once. Later UI changes are not overwritten by the legacy flag. Remove `OAUTH_ENABLED` after upgrading; use `MCP_MODE` for continued deployment control.

Installations that previously used token-only MCP must enable MCP after upgrading. Personal access tokens retain their REST access. Changing the public URL or MCP mode in settings invalidates app grants and pending authorizations; users must reconnect their signed-in assistants.

Setup steps: [AI assistants](../administration/ai-assistants.md#prepare-the-server).

## Keep keys persistent

Preserve `/data/keys` with the database and part files. Losing Data Protection keys makes saved printer credentials unreadable. Replacing the signing key invalidates existing login tokens. See [Upgrades and backups](upgrades-backups.md) before moving an installation.
