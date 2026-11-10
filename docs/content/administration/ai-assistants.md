---
title: AI assistants
description: Connect Claude, ChatGPT, Claude Code, Cursor, VS Code and other MCP clients to OpenFarm by signing in or with an access token.
---

OpenFarm runs an [MCP](https://modelcontextprotocol.io) server at `/mcp` on your installation. An AI assistant connected to it can read your farm (printers, the queue, production runs, parts, assemblies, filament, spools, maintenance and farm stats) and make the queue and maintenance changes you allow. It acts as you, with only the access you give it.

There are two ways to connect:

| Way | How | For |
|---|---|---|
| Sign in | The assistant opens OpenFarm, you sign in and choose what it may do. | Claude, ChatGPT, Claude Code, Cursor, VS Code. Needs [sign-in turned on](#prepare-the-server). |
| Access token | You create a token and paste it into the assistant's settings. | Any MCP client that can send a header, including on a LAN-only installation. |

## Prepare the server {#prepare-the-server}

Sign-in is off until an administrator turns it on. It needs a public HTTPS address:

1. Give OpenFarm its public address and turn sign-in on in `.env`:

   ```dotenv
   ORIGIN=https://farm.example.com
   OAUTH_ENABLED=true
   ```

2. Recreate the application: `docker compose up -d --force-recreate openfarm`.
3. Route the whole hostname to OpenFarm, including `/mcp`, `/oauth/` and `/.well-known/`, as for `/api` and `/hubs` (see [Configuration](../installation/configuration.md#https-and-reverse-proxies)). Do not put Cloudflare Access or another login wall in front of `/.well-known/` or `/oauth/token`.
4. Check it: `curl -i https://farm.example.com/mcp` answers `401` with a `WWW-Authenticate` header that contains `resource_metadata`.

Claude and ChatGPT on the web connect from the internet: the address must be reachable from there. An installation reachable only on your LAN can use Claude Code, Cursor or VS Code with an access token instead.

## Sign in from your assistant

My profile → Connected apps → **Connect an AI assistant** shows the server URL and the setup for each assistant. In short:

| Assistant | Setup |
|---|---|
| Claude | Settings → Connectors → **Add custom connector**. Name: OpenFarm. URL: `https://farm.example.com/mcp`. |
| ChatGPT | Settings → Apps & Connectors → Advanced settings → turn on **Developer mode**, then create a connector with the URL and OAuth authentication. |
| Claude Code | `claude mcp add --transport http openfarm https://farm.example.com/mcp`, then run `/mcp` in Claude Code and choose **Authenticate**. |
| Cursor | Add `{"mcpServers":{"openfarm":{"url":"https://farm.example.com/mcp"}}}` to `~/.cursor/mcp.json`. |
| VS Code | Add `{"servers":{"openfarm":{"type":"http","url":"https://farm.example.com/mcp"}}}` to `.vscode/mcp.json`. |

When the assistant connects, OpenFarm opens a **Connect** page, titled with the app's name, in your browser:

1. Sign in to OpenFarm if asked. Check the account shown under the title.
2. Check **Returns to**: it names the website or the app on your computer that receives the answer. The app name is supplied by the app itself and is not verified.
3. Choose what the app may do. Reading is selected for you; changes and printer control never are.
4. Choose **Allow selected access**. If you did not start this from your assistant just now, choose **Cancel**.

A connection lasts 30 days. Then connect again from the assistant.

## Connect with an access token

1. Open **My profile → Access tokens → Add token**.
2. Name the token after the assistant, for example `Claude Code`.
3. Select the read permissions the assistant needs, for example **Printers: View** and **Queue: View**.
4. Create the token and copy the credential. OpenFarm shows it only once.
5. Open **My profile → Connected apps → Connect an AI assistant**, choose your assistant and copy its setup.

Store the credential in an environment variable such as `OPENFARM_TOKEN`, not in a file you share. For Claude Code:

```bash
claude mcp add --transport http openfarm https://farm.example.com/mcp \
  --header "Authorization: Bearer $OPENFARM_TOKEN"
```

Other clients need these settings:

| Setting | Value |
|---|---|
| Transport | Streamable HTTP |
| Server URL | `https://farm.example.com/mcp` |
| Header | `Authorization: Bearer <token>` |

The assistant sees only the tools that both its access and your account allow. If you lose a permission, the assistant loses it on its next request.

## What the assistant can change

With the matching access, an assistant can also:

| Change | Sign-in choice | Token permission |
|---|---|---|
| Reorder, pin and unpin queued jobs | Reorder, pin and requeue jobs, and dismiss notifications | Queue: reorder, pin |
| Requeue a failed or cancelled job | same | Queue: requeue |
| Dismiss a filament follow-up notification | same | Queue: update status |
| Mark maintenance done | Mark maintenance done | Maintenance: complete |
| Pause a running print | Pause running prints | Queue: remote control |
| Cancel a job or stop a running print | Cancel jobs and stop running prints | Queue: cancel |

Requeue, pause and cancel act on real printers: a requeued job can start on an idle printer without another step, and a stopped print cannot be resumed. The assistant is told to show you the job, the printer and its progress and to wait for your confirmation first. Most assistants also ask you to approve each change. Allow these only for an assistant you watch.

Cancel checks that the job is still where the assistant saw it. If a queued job was sent to a printer in the meantime, the cancel is refused and the assistant must read the job again and ask you.

An assistant can never mark a printer ready, skip objects, remove anything, or change settings, printer connections, users or credentials. Each change appears in the activity log under your name, with the app or token name.

## What the assistant sends elsewhere

The assistant sends what it reads to its AI service. Give it only the access it needs. Names and notes in OpenFarm are shown to the assistant as data; it is told never to follow instructions found in them.

## Disconnect

- **A signed-in app**: My profile → Connected apps → **Revoke access**. An administrator can do the same on the user's page.
- **A token**: My profile → Access tokens → **Revoke**.
- Changing your password, an administrator's password reset or disabling the account ends every connected app and token. An administrator's **Revoke all** on a user's sessions also ends their apps.
- **Sign out** and your own **Revoke all sessions** do not disconnect apps or tokens.

## Limits

- A signed-in connection lasts 30 days from when you allowed it.
- Lists return at most 100 items a page.
- The assistant does not get live updates; it reads again when you ask.
- Sign-in needs HTTPS (HTTP only for `localhost`).

## Troubleshooting

- **401 Unauthorized**: the token or sign-in is wrong, expired or revoked, or you sent a browser session instead. With sign-in on, the `WWW-Authenticate` header names `resource_metadata`, which the assistant uses to sign in.
- **The assistant asks for an OAuth login, or says the server needs authentication it cannot do**: sign-in is off. [Prepare the server](#prepare-the-server), or use an access token.
- **Sign-in is off**: shown on the Connect page when `OAUTH_ENABLED` is not `true`. Set it and an HTTPS `ORIGIN`, then restart OpenFarm.
- **Connection request expired**: the Connect page was open for more than 10 minutes, or was already used. Start again from the assistant.
- **Discovery returns HTML**: your proxy sends `/.well-known/` or `/oauth/` to another site or to a cached app page. Route the whole hostname to OpenFarm.
- **403 Forbidden (MCP origin check failed)**: a browser page called `/mcp` from another site. Use the assistant's own connector, desktop app or CLI.
- **405 on GET /mcp**: normal. MCP uses POST only.
- **A tool is missing**: the assistant's access or your account lacks its permission. Connect again and allow it, or add the permission to a new token.

Check the server from a terminal with `curl -i https://farm.example.com/mcp`.
