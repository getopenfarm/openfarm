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

## Writes

- `McpTools.Write` + `request.WriteAsync(dto, run, ct)`: builds the mirrored endpoint's request DTO, runs its
  `IValidator<T>` (INVALID_INPUT with `issues`), runs the same handler, then `ActivityRecorder.RecordAsync` —
  the code the FastEndpoints post-processor uses — so rows and live events match REST.
- Printer commands (`pause_print_job`, `cancel_print_job`) return `{ job, confirmed, note }`: confirmed is false
  until telemetry reports the change, as REST's 202.
- `cancel_print_job` requires `expectedPhase` (`PrintJobPhases.Of`: Queued, Starting, Printing, Paused). A
  dispatched job is still `Status = Queued`, so status alone cannot tell "remove from queue" from "stop the
  printer". The handler checks it after loading the job (`PrintJobPhaseMismatchException` → 409 with
  `currentPhase`); a dispatch racing the save fails the row version (409). REST passes no expectation.
- Requeue, pause and cancel are `openWorld`, and their descriptions and the instructions require presenting
  the job and obtaining reviewed confirmation (tested). Mark ready is never a tool: it is the human "bed is
  clear" gate.

## Activity log

Rows written during a token request carry `details.via` (`Token`, token id, token name), stamped by
`ActivityChannelInterceptor` on SaveChanges, so REST and MCP writes are attributed the same way.
