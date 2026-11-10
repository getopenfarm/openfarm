---
title: AI assistants
description: Connect Claude Code, Cursor, VS Code, Codex and other MCP clients to OpenFarm with an access token.
---

OpenFarm runs an [MCP](https://modelcontextprotocol.io) server at `/mcp` on your installation. An AI assistant connected to it can read your farm (printers, the queue, production runs, parts, assemblies, filament, spools, maintenance and farm stats) and make the queue and maintenance changes you allow. It acts as you, with only the access you give it.

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

The assistant sees only the tools that both the token and your account allow. If you lose a permission, the assistant loses it on its next request. Revoke the token to disconnect the assistant at once.

## What the assistant can change

With the matching permissions on its token, an assistant can also:

| Change | Permission |
|---|---|
| Reorder, pin and unpin queued jobs | Queue: reorder, pin |
| Requeue a failed or cancelled job | Queue: requeue |
| Dismiss a filament follow-up notification | Queue: update status |
| Mark maintenance done | Maintenance: complete |
| Pause a running print | Queue: remote control |
| Cancel a job or stop a running print | Queue: cancel |

Requeue, pause and cancel act on real printers: a requeued job can start on an idle printer without another step, and a stopped print cannot be resumed. The assistant is told to show you the job, the printer and its progress and to wait for your confirmation first. Most assistants also ask you to approve each change. Give these permissions only to a token you watch.

Cancel checks that the job is still where the assistant saw it. If a queued job was sent to a printer in the meantime, the cancel is refused and the assistant must read the job again and ask you.

An assistant can never mark a printer ready, skip objects, remove anything, or change settings, printer connections, users or credentials. Each change appears in the activity log with the token's name.

## What the assistant sends elsewhere

The assistant sends what it reads to its AI service. Give a token only the access the assistant needs. Names and notes in OpenFarm are shown to the assistant as data; it is told never to follow instructions found in them.

Claude and ChatGPT on the web connect from the internet and need sign-in, which is not available yet. Use an assistant that runs on your computer, such as Claude Code, Cursor or VS Code.

## Troubleshooting

- **401 Unauthorized**: the token is wrong, expired or revoked, or you sent a browser session instead of a token.
- **403 Forbidden (MCP origin check failed)**: a browser page called `/mcp` from another site. Use a desktop or CLI client.
- **A tool is missing**: the token or your account lacks its permission. Add the permission to a new token.
