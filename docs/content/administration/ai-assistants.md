---
title: AI assistants
description: Connect Claude, ChatGPT, Claude Code, Cursor, VS Code and other MCP clients to OpenFarm by signing in or with an access token.
---

OpenFarm runs an [MCP](https://modelcontextprotocol.io) server at `/mcp` on your installation. An AI assistant connected to it can read your farm (printers, the queue, production runs, parts, assemblies, filament stock, fee profiles, maintenance and farm stats) and make the changes you allow. It acts as you, with only the access you give it.

There are two ways to connect:

| Way | How | For |
|---|---|---|
| Sign in | The assistant opens OpenFarm, you sign in and choose what it may do. | Claude, ChatGPT, Claude Code, Cursor, VS Code. Needs [sign-in turned on](#prepare-the-server). |
| Access token | You create a token and paste it into the assistant's settings. | Any MCP client that can send a header, including on a LAN-only installation. |

## Prepare the server {#prepare-the-server}

MCP is disabled by default. An administrator must enable it before any assistant can connect.

1. Open **Settings → AI assistants**.
2. Select **Access tokens only** for a compatible local client. For browser sign-in, first set the **Public URL** under **Settings → Server**, then select **Sign-in and access tokens**.
3. Select **Save**. Changes apply without restarting OpenFarm.
4. Connect from your assistant. Follow the sign-in or access-token steps below.

Sign-in requires an HTTPS public URL. HTTP is allowed only for localhost development. Setting the public URL does not configure DNS, HTTPS, a tunnel, or a reverse proxy.

For remote access, route the whole hostname to OpenFarm, including `/mcp`, `/oauth/` and `/.well-known/`. Do not put another login wall in front of discovery or `/oauth/token`. See [Configuration](../installation/configuration.md#https-and-reverse-proxies).

Deployment settings can lock these controls. The page names the setting that controls the field. See [deployment overrides](../installation/configuration.md#ai-assistant-sign-in).

To check an enabled server, run:

```bash
curl -i -X POST https://farm.example.com/mcp \
  -H 'Content-Type: application/json' -d '{}'
```

Without a credential, the server returns `401`. With sign-in enabled, its `WWW-Authenticate` header includes `resource_metadata`. A disabled server returns `404`.

Claude and ChatGPT on the web connect from the internet: the address must be reachable from there. An installation reachable only on your LAN can use Claude Code, Cursor or VS Code with an access token instead.

## Sign in from your assistant

Use your installation address with `/mcp` at the end. For example, if the public URL is `https://farm.example.com`, use `https://farm.example.com/mcp`. Replace the example URL in each setup below.

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
5. Configure your assistant with the server URL and token. Use an example below.

Use your installation address with `/mcp` at the end. Replace `https://farm.example.com/mcp` in the examples. A local client can use a LAN address, such as `http://openfarm.local:6868/mcp`.

Store the credential securely. You can use a local environment variable such as `OPENFARM_TOKEN` to keep it out of a shared configuration file. This is a client credential, separate from the server settings.

### Claude Code

```bash
claude mcp add --transport http openfarm https://farm.example.com/mcp \
  --header "Authorization: Bearer $OPENFARM_TOKEN"
```

### Cursor

Set `OPENFARM_TOKEN` in the environment used to start Cursor. Add this configuration to `~/.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "openfarm": {
      "url": "https://farm.example.com/mcp",
      "headers": { "Authorization": "Bearer ${env:OPENFARM_TOKEN}" }
    }
  }
}
```

### VS Code

Add this configuration to `.vscode/mcp.json`. VS Code asks for the token and stores it securely.

```json
{
  "inputs": [
    {
      "type": "promptString",
      "id": "openfarm-token",
      "description": "OpenFarm access token",
      "password": true
    }
  ],
  "servers": {
    "openfarm": {
      "type": "http",
      "url": "https://farm.example.com/mcp",
      "headers": { "Authorization": "Bearer ${input:openfarm-token}" }
    }
  }
}
```

### Codex

Set `OPENFARM_TOKEN` in the environment used to start Codex. Add this configuration to `~/.codex/config.toml`:

```toml
[mcp_servers.openfarm]
url = "https://farm.example.com/mcp"
bearer_token_env_var = "OPENFARM_TOKEN"
```

### Other clients

Other clients need these settings:

| Setting | Value |
|---|---|
| Transport | Streamable HTTP |
| Server URL | `https://farm.example.com/mcp` |
| Header | `Authorization: Bearer <token>` |

The assistant sees only the tools that both its access and your account allow. If you lose a permission, the assistant loses it on its next request.

## What you can ask

- "What needs my attention?" — printers awaiting clear, offline or alerting, jobs that cannot be sent and why, failed jobs, late runs, low filament and maintenance due.
- "Queue 20 keychains and 5 desk sets." or "Make a production run for Friday's market." — the assistant shows the plan (plates, print hours, filament, shortages) and queues it only after you agree.
- "I cleared the plates on P1 and P3." — marks those printers ready.
- "Why is the bracket job not printing?" — the checks each printer fails: nozzle, material, colour, plate or pin.
- "What filament should I order for the next six weeks?" — stock, use over the last 30 days, queued work and spools to buy per colour.
- "I received 10 spools of PLA Basic Black, 1 kg, 22 each." — adds them to stock.
- "Put the gridfinity parts in Storage/Gridfinity and tag them." — folders, names, tags and assemblies.
- "Add a Shopee fee profile: 18% commission and 0.50 per order." and "What does the desk set sell for on Shopee?"
- "Lubricate the rods every 200 print hours on all X1C printers."

## What the assistant can change

With the matching access, an assistant can also:

| Change | Sign-in choice | Token permission |
|---|---|---|
| Reorder, pin and unpin queued jobs | Reorder, pin and requeue jobs | Print jobs: Reorder, Pin |
| Requeue a failed or cancelled job | same | Print jobs: Requeue |
| Queue parts and assemblies, alone or as a production run | Plan and queue parts and assemblies | Production runs: Add |
| Organize library folders, rename and tag parts, add and change assemblies | Organize folders, rename and tag parts, and add or change assemblies | Library: Organize; Parts: Change; Assemblies: Add, Change |
| Add new spools to stock | Add new spools to stock | Spools: Add |
| Add and change fee profiles | Add and change fee profiles | Settings: Change fees |
| Add maintenance tasks, assign them to printers, mark them done | Add and assign maintenance tasks, and mark maintenance done | Maintenance: Change, Mark done |
| Mark printers ready | Mark printers ready after their plates are cleared | Printers: Mark ready |
| Pause a running print | Pause running prints | Print jobs: Remote control |
| Cancel a job or stop a running print | Cancel jobs and stop running prints | Print jobs: Cancel |

Queueing, requeueing, marking ready, pausing and cancelling act on real printers: an idle printer can start the work at once, and a stopped print cannot be resumed.

- **Queueing**: the assistant first shows you the plan and queues exactly that plan only after you agree. Asking twice never queues it twice.
- **Mark ready**: the assistant marks a printer ready only when you tell it that printer's plate is clear. If another print ended on that printer after the assistant looked, it refuses and asks you again.
- **Requeue, pause and cancel**: the assistant shows you the job, the printer and its progress and waits for your confirmation. Cancel also checks that the job is still where the assistant saw it.

Most assistants also ask you to approve each change. Allow printer control only for an assistant you watch.

An assistant can never remove anything, resume prints, skip objects, or change settings other than fee profiles, printer connections, users or credentials. Each change appears in the activity log under your name, with the app or token name.

## What the assistant sends elsewhere

The assistant sends what it reads to its AI service. Give it only the access it needs. Names and notes in OpenFarm are shown to the assistant as data; it is told never to follow instructions found in them.

## Disconnect

An administrator can select **Disabled** under **Settings → AI assistants** to block all MCP access. Changing the MCP mode or public URL disconnects signed-in apps and cancels pending connection requests. Re-enabling MCP does not restore those app connections. Personal access tokens keep their REST permissions and can use MCP again when token access is enabled. Work already accepted by a printer is not undone.

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
- **MCP is disabled**: ask an administrator to enable it under **Settings → AI assistants**.
- **Sign-in is off**: use an access token, or ask an administrator to set an HTTPS public URL and select **Sign-in and access tokens**.
- **Connection request expired**: the Connect page was open for more than 10 minutes, or was already used. Start again from the assistant.
- **Discovery returns HTML**: your proxy sends `/.well-known/` or `/oauth/` to another site or to a cached app page. Route the whole hostname to OpenFarm.
- **403 Forbidden (MCP origin check failed)**: a browser page called `/mcp` from another site. Use the assistant's own connector, desktop app or CLI.
- **405 on GET /mcp**: normal. MCP uses POST only.
- **A tool is missing**: the assistant's access or your account lacks its permission. Connect again and allow it, or add the permission to a new token.

Use the POST check in [Prepare the server](#prepare-the-server). A plain GET request returns `405` when MCP is enabled.
