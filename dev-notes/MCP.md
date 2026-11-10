# MCP server

AI assistants reach OpenFarm through the Model Context Protocol at `POST /mcp`, served by the official C# SDK
(`ModelContextProtocol.AspNetCore`, pinned in `OpenFarm.Api.csproj`). Plan and rationale:
`dev-notes/plans/MCP_OAUTH.md`. User guide: `docs/content/administration/ai-assistants.md`.

## Transport and auth

- Stateless Streamable HTTP (`HttpServerSessionMode.Stateless`): POST only, no `Mcp-Session-Id`, no GET stream.
  Every request is authenticated and authorized from scratch, so a revoked token or app, or a removed
  permission, takes effect on the next call.
- Credentials: policy `Mcp` uses the policy scheme `Mcp` (`Security/McpAuthentication.cs`): `Bearer ofp_…` goes
  to the personal access token handler, anything else to the `OAuth` JwtBearer, which accepts only app access
  tokens. The browser JwtBearer is never consulted, so a browser JWT gets 401.
- 401 challenge, written in one place (`McpAuthentication.WriteChallenge`, the `OAuth` scheme's `OnChallenge`,
  which the policy scheme forwards every challenge to): `Bearer realm="openfarm-mcp"`, then
  `, resource_metadata="ORIGIN/.well-known/oauth-protected-resource/mcp"` when OAuth is on, then
  `, error="invalid_token"` when a `Bearer` token was sent (any other scheme gets no error, RFC 6750 §3.1).
- Inline middleware in `Program.cs`: on `/mcp`, `/oauth` and `/.well-known`, `Cache-Control: no-store`,
  `Pragma: no-cache`, `Referrer-Policy: same-origin`, frame and sniffing headers; `/mcp` 405 for non-POST, 413
  over 128 KiB, and 403 for any browser `Origin` other than `ORIGIN`; `/oauth/*` 413 `{"error":"invalid_request"}`
  over 16 KiB; `/oauth/register|token|revoke` 405 `{"error":"invalid_request"}` with `Allow: POST` for any other
  method. The SPA's `.html` files get the frame headers and `Referrer-Policy: same-origin` too.
- `/mcp`, `/oauth` and `/.well-known` are excluded from the SPA fallback and from OpenAPI.

### Which credential reaches what

| Credential | `/api/v1/**` | `/mcp` | `/api/v1/auth/**`, `/admin/**` | Consent API | `/hubs/farm` |
|---|---|---|---|---|---|
| Browser JWT (`iss=aud=Jwt:Issuer`, browser `sid`) | yes | 401 | yes | only this | yes |
| PAT `ofp_` | yes | yes | 403 (path ban) | 403 | 401 |
| App access JWT (`iss=ORIGIN`, `aud=ORIGIN/mcp`, app `sid`) | 401 | yes | 401 | 401 | 401 |
| App refresh `ofr_` as a bearer | 401 | 401 | 401 | 401 | 401 |

Each "no" has two guards: the audiences differ per scheme, and `BrowserSessionService.IsActiveAsync` requires
`ClientId == null` while `ResolveAppAsync` requires the token's `client_id`.

## OAuth sign-in (off by default)

`OAUTH_ENABLED=true` + an HTTPS `ORIGIN` (HTTP only for localhost) turns on a hand-rolled OAuth 2.1 server for
the `/mcp` audience (`Infrastructure/Auth/OAuthSettings.cs` validates at startup). Off: every protocol path
answers 404 `{"error":"OAuth is disabled"}`, `/oauth/authorize` redirects to `/connect/error?code=disabled`, and
apps are kept but refused until it is on again. Issuer, resource and every metadata URL come only from `ORIGIN`.

- Protocol routes (`Api/OAuth/OAuthProtocolEndpoints.cs`, minimal API, excluded from OpenAPI): RFC 9728 PRM at
  `/.well-known/oauth-protected-resource[/mcp]`, RFC 8414 metadata, RFC 7591 DCR at `/oauth/register`,
  `/oauth/authorize`, `/oauth/token` (code + mandatory S256 PKCE, refresh), RFC 7009 `/oauth/revoke` (refresh
  and access tokens), RFC 9207 `iss` on every authorization response. Errors are `{"error":"<code>"}` only.
  Repeated parameters are refused; a parameter sent with no value counts as omitted (RFC 6749 §3.1).
- Rate limits: ASP.NET `RateLimiter` keyed on `Connection.RemoteIpAddress` (never `X-Forwarded-For`),
  `OAuth:RegisterPermitLimit` (10/min) and `OAuth:PermitLimit` (60/min). Behind a tunnel one shared bucket.
- Pure rules live in `OAuthRules` (redirect classes and matching, PKCE, client names, state, parameters; TDD).
  State and flow live in `OAuthService`. Expiries use the injected `TimeProvider`.
- Authorize validates and 302s to the SPA consent page `/connect?request=<payload>`: a DataProtection
  time-limited payload (10 min, purpose `OpenFarm.OAuthRequest.v1`) with a random `rid`. Every failure goes to
  `/connect/error?code=…` on OpenFarm, never to the client's redirect URI. `resource` may be omitted; another
  value is `invalid_target`. Requested scopes are ignored: the user chooses on the consent page.
- Consent (`GET`/`POST /api/v1/auth/oauth/consent`, browser session only) re-checks client and redirect on
  every call. Approve creates the pending app with `Id = Guid(rid)` and the redirect URI it consented to, so a
  request is approved at most once (primary key), and returns a 120 s code payload (purpose
  `OpenFarm.OAuthCode.v1`). Deny inserts the same row already revoked, so a denied request is used up too.
- Exchange locks the pending app and checks PKCE first. A code whose app already has a credential is a replay:
  with the right verifier it revokes the app, the first exchange's tokens included (RFC 6749 §4.1.2), so two
  concurrent exchanges of one code leave no working app; with a wrong or missing verifier it is refused and
  changes nothing (the code alone travels in a URL). A first exchange with a failed PKCE check revokes the
  pending app (the code is burned). Success adds the first `ofr_` credential, revokes older exchanged apps of the
  same user and client (reconnect replaces; a parallel pending one is left alone) and audits
  `ConnectedAppAdded`. Refresh is `BrowserSessionService.RefreshAsync(credential, clientId)`: rotation, the
  10 s same-successor retry and reuse revocation are the browser session's own.
- A widening `scope` on refresh is `invalid_scope`; a narrower one is accepted and the full grant is returned
  (no per-token narrowing).

### A connected app is a browser session with `ClientId`

`BrowserSessions.ClientId` (FK → `OAuthClients`, cascade) and `Scopes` (`CHECK` both null or both set). One
migration, `ConnectedApps`. Lifetime: 30 days from consent, absolute. Pending apps (no credential) are hidden
everywhere and deleted after 10 minutes; clients nobody connected are deleted after 24 hours
(`OAuthService.CleanupAsync`, run by `SessionCleanupService`); at 1000 clients registration reclaims the oldest
client with no app that is older than a consent request (10 minutes), else answers 503. `BrowserSessions.RedirectUri`
keeps the redirect the user consented to: the "Returns to" of the Connected apps list and the audit rows.

Every `BrowserSessions` query states its kind: `ListSessionsEndpoint` and the users list count browsers only;
`IsActiveAsync` browsers only; `ResolveAppAsync` apps only; `RefreshAsync` matches `ClientId` (and the
credential format: 64 hex vs `ofr_` + 64 hex); `RevokeCredentialAsync` (sign out) ignores apps; your own
revoke-all passes `browsersOnly`; the admin revoke-all, password change, reset and disable end apps too.
Adding a query on `BrowserSessions` means choosing its kind.

### Scopes

`Application/Auth/OAuthScopes.cs` is a frozen catalog of 10 scopes (`<area>.<tier>`, tiers Read, Change and
Printer control). `OAuthScopesTests` snapshots every expansion: never widen a scope, since every app already
granted it would gain the ability; add a new scope instead. Each permission belongs to exactly one scope
(tested), so for an app `CredentialAccess.EffectiveAsync` = owner permissions ∩ expand(granted scopes) gates
tools on scope and permission at once. Scopes are read from the app session on every request, never from the
token. Consent pre-ticks only allowed Read scopes; Change and Printer control are never pre-ticked.

### Redirect URIs (deviation from MCP 2025-11-25)

Web `https:` any host; Loopback `http:` on `localhost`, `127.0.0.1` or `[::1]`, any port when matching
(RFC 8252 §7.3); Native: exactly `cursor`, `vscode` and `vscode-insiders`. Everything else is refused.
**Deviation:** MCP Communication Security says "All redirect URIs MUST be either localhost or use HTTPS." The
three native schemes break that rule because Cursor and VS Code register them (decision confirmed by the user,
2026-11-10). Changing the list is one line in `OAuthRules.NativeSchemes` plus a test. The consent page marks
Loopback and Native names as unverified.

### Proxies

Forward `/mcp`, `/oauth/` and `/.well-known/` unbuffered with `Authorization`, `Accept`, `Content-Type`,
`Mcp-Protocol-Version`, `Mcp-Method` and `Mcp-Name`. Keep Cloudflare Access off discovery and `/oauth/token`.

## Tools

- `Mcp/McpTools.cs` is the one table: each `McpToolSpec` is an SDK tool plus the permissions it needs.
- `McpSetup.ConfigureSessionOptions` lists only the tools the caller may use now
  (`CredentialAccess.EffectiveAsync` = owner permissions ∩ token abilities, or ∩ expand(scopes) for an app).
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
6. Optional tool arguments need a default (`string? x = null`, `CancellationToken ct = default`): without one
   the SDK marks the argument required and the call fails with "Invalid request".
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

## Known trade-offs

- Apps keep the browser session's 10-second refresh grace (same successor on a retried refresh): a refresh whose
  response is lost in a tunnel would otherwise disconnect the app (plan D8).
- OAuth rate limits count the socket address, so behind a tunnel every client shares one bucket: this fails
  safe; a trusted-proxy setting is later work.
- `BrowserSessionService.RevokeAsync` reads the apps to audit before it revokes them: in that narrow race only an
  audit row can be missed; access is still revoked.

## Activity log

Rows written during a token or app request carry `details.via` (`Token`/token id/token name, or `App`/app
session id/app name), stamped by `ActivityChannelInterceptor` on SaveChanges, so REST and MCP writes are
attributed the same way. `whoami` reports the same kind and label, plus the granted scopes for an app.

App lifecycle rows (`EntityType = ConnectedApp`, `EntityId` = app session id): `ConnectedAppAdded`,
`ConnectedAppRevoked` (`Reason`: `user`, `administrator`, `replaced`, `code-reuse`, `app`) with a
`ReportedState` JSON of client id, name, returns-to label, scopes and expiry, and
`ConnectedAppCredentialReuse`. Never logged or audited: tokens, codes, verifiers, secrets, request payloads,
`Authorization` headers; `client_name` is never logged (DCR logs client id, auth method and redirect kinds).
