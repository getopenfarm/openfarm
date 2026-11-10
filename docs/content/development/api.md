---
title: API access
description: Create a scoped personal access token and call the same REST API used by OpenFarm.
---

OpenFarm's REST API uses paths under `/api/v1` on your installation. Use a personal access token for scripts and integrations. Manage tokens from a browser where you are signed in.

## Create a token

1. Open **My profile → Access tokens → Add token**.
2. Enter a unique name and an expiry date.
3. Select at least one permission. Clear the date only if you need a token that does not expire.
4. For the printer-list example below, grant **`printers:list`**.
5. Create the token.
6. Copy the token credential. OpenFarm shows it only once.

Expiry is the end of the selected date in UTC. The default is six months. A user can hold up to 50 unrevoked tokens, including expired ones.

## Make a request

Set `OPENFARM_TOKEN` securely in your shell or secret manager, then send it in a header over HTTPS:

```bash
curl --fail-with-body \
  --header "Authorization: Bearer $OPENFARM_TOKEN" \
  https://farm.example.com/api/v1/printers
```

Do not put credentials in URLs, logs, or source files. Each request requires permission from both the token and its owner's account. If the owner loses a permission, existing tokens also lose that access.

## Rotate and revoke

**Regenerate** immediately replaces the credential and keeps its permissions and expiry date. Update the integration with the new credential. If a token has expired, create a new token.

**Revoke** and **Revoke all** take effect on subsequent requests. Administrators can revoke another user's tokens. They cannot create or regenerate credentials for that user.

The same token also works with the MCP server at `/mcp`, for AI assistants: see [AI assistants](../administration/ai-assistants.md).

Password changes and resets revoke personal tokens. Disabling the owner's account revokes tokens and sessions. Personal tokens cannot manage accounts, browser sessions, or other tokens. They also cannot connect to SignalR live events. These operations require a browser session.

## Read the contract

Use the repository's [OpenAPI document](https://github.com/getopenfarm/openfarm/blob/main/api/openapi.json) for exact routes, request fields, responses, and operation permissions. Generate a contract from a checkout with:

```bash
cd api/src/OpenFarm.Api
dotnet run -- export-openapi
```

Export mode does not initialize the database or start printer workers. The web client is generated from this contract; see [Local development](local-setup.md).

## Handle responses

- **401**: invalid, expired, or revoked credential or browser session.
- **403**: authenticated but missing the endpoint's required permission.
- **400 / 422**: read the validation error or problem response and correct the request.
- **409**: inspect current state before retrying a conflicting operation.

When submitting production demand, reuse a submission ID only for the same request. A queued job can start a physical print. Check the current configuration and review the preview before submitting work.
