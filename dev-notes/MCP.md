# MCP server

AI assistants reach OpenFarm through the Model Context Protocol at `POST /mcp`, served by the official C# SDK
(`ModelContextProtocol.AspNetCore`, pinned in `OpenFarm.Api.csproj`). Plan and rationale:
`dev-notes/plans/MCP_OAUTH.md`. User guide: `docs/content/administration/ai-assistants.md`.

## Transport and auth

- Stateless Streamable HTTP (`HttpServerSessionMode.Stateless`): POST only, no `Mcp-Session-Id`, no GET stream.
  Every request is authenticated and authorized from scratch, so a revoked token or a removed permission
  takes effect on the next call.
- Credentials: personal access tokens (`ofp_…`) through policy `Mcp`. A browser JWT gets 401. OAuth access
  tokens arrive in a later slice.
- 401 challenge: `WWW-Authenticate: Bearer realm="openfarm-mcp"`, plus `error="invalid_token"` when an
  `Authorization` header was sent (`PersonalAccessTokenHandler.HandleChallengeAsync`).
- Inline middleware in `Program.cs`: `Cache-Control: no-store`, frame and sniffing headers, 405 for non-POST,
  413 over 128 KiB, and 403 for any browser `Origin` other than `ORIGIN`.
- `/mcp`, `/oauth` and `/.well-known` are excluded from the SPA fallback and from OpenAPI.

## Tools

- `Mcp/McpTools.cs` is the one table: each `McpToolSpec` is an SDK tool plus the permissions it needs.
- `McpSetup.ConfigureSessionOptions` lists only the tools the caller may use now
  (`CredentialAccess.EffectiveAsync` = owner permissions ∩ token abilities).
- `McpCall.Filter` wraps every call: it rechecks access (FORBIDDEN), maps exceptions through
  `DomainExceptionHandler.ToProblem` to `{"error":{"code","message",…}}` tool errors (NOT_FOUND, CONFLICT,
  INVALID_INPUT, INTERNAL_ERROR), and logs one line per call without arguments or results.
- A tool returns the mirrored REST endpoint's response DTO, built by the same static factory the endpoint uses
  (for example `ListFleetResponse.From`). Structured content and the output schema come from that type.

Rules for a new tool:
1. Reuse the endpoint's Application handler and response factory. Never query EF Core from a tool.
2. Its permissions equal the endpoint's `[Permission]` + `AdditionalPermissions` (a test enforces this).
3. Reads use `McpTools.Read` (read-only annotations). Lists page with `McpTools.Page`/`PageSize` (≤ 100) and
   end their description with `McpTools.PagingNote`.
4. Leave out network addresses, serial numbers, file bytes and file URLs.
5. Never expose users, sessions, tokens, settings, printer connections, Mark ready or removals.

## Activity log

Rows written during a token request carry `details.via` (`Token`, token id, token name), stamped by
`ActivityChannelInterceptor` on SaveChanges, so REST and MCP writes are attributed the same way.
