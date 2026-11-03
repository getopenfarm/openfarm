---
title: API access
description: Create a scoped personal access token and call the same REST API used by OpenFarm.
---

OpenFarm's REST endpoints live under `/api/v1` on your installation. Use a personal access token for scripts and integrations; manage tokens from a signed-in browser.

## Create a token

1. Open **My profile → Access tokens → Add token**.
2. Enter a unique name, expiry date, and at least one permission. Clear the date only if you need a non-expiring credential.
3. For the printer-list example below, grant **`printers:list`**.
4. Create the token and copy its credential. It is shown only once.

Expiry is the end of the selected date in UTC. The default is six months. A user can hold up to 50 unrevoked tokens, including expired ones.

## Make a request

Set `OPENFARM_TOKEN` securely in your shell or secret manager, then send it in a header over HTTPS:

```bash
curl --fail-with-body \
  --header "Authorization: Bearer $OPENFARM_TOKEN" \
  https://farm.example.com/api/v1/printers
```

Do not put credentials in URLs, logs, or source files. The token's selected permissions are intersected with its owner's current permissions on every request. Losing account access also limits existing tokens.

## Rotate and revoke

**Regenerate** immediately replaces the credential while keeping its permissions/expiry. Update the integration with the new one. Expired tokens need a new token. **Revoke** and **Revoke all** take effect on subsequent requests; administrators can revoke another user's tokens but cannot create/regenerate credentials for them.

Password changes/resets revoke personal tokens. Disabling the owner revokes tokens and sessions. Token credentials cannot manage accounts, browser sessions, or other tokens, and cannot connect to SignalR live events; those require browser sessions.

## Read the contract

Use the repository's [OpenAPI document](https://github.com/getopenfarm/openfarm/blob/main/api/openapi.json) for exact routes, request fields, responses, and operation permissions. Generate a contract from a checkout with:

```bash
cd api/src/OpenFarm.Api
dotnet run -- export-openapi
```

Export mode does not initialize the database or start printer workers. The web client is generated from this contract; see [Local development](local-setup.md).

## Handle responses

- **401**: invalid, expired, or revoked credential/session.
- **403**: authenticated but missing the endpoint's required permission.
- **400 / 422**: inspect the validation/problem response and fix the request.
- **409**: inspect current state before retrying a conflicting operation.

For production-demand submission, reuse its submission ID only for the same request. Queueing work is a physical operation; check current configuration and review the preview before creating demand.
