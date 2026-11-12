# MCP server

AI assistants reach OpenFarm through the Model Context Protocol at `POST /mcp`, served by the official C# SDK
(`ModelContextProtocol.AspNetCore`, pinned in `OpenFarm.Api.csproj`). Plan and rationale:
`dev-notes/plans/MCP_OAUTH.md`. User guide: `docs/content/administration/ai-assistants.md`.

## Activation and installation settings

`ServerSecuritySettings` adds a singleton `ServerSettings` row and `BrowserSessions.OAuthConfigurationIdentity`.
MCP defaults to Disabled; AccessTokens and SignIn are explicit modes. Administrators configure the public URL
under Settings → Server and the mode under Settings → AI assistants. The endpoints are
`GET/PUT /api/v1/admin/settings/server` and `/api/v1/admin/settings/mcp`: actual Administrators membership,
a browser session, current-password verification (Identity lockout), optimistic revision checks and a transaction.
Audits contain before/after settings, never passwords. Generic farm-settings access cannot change installation security.

`ServerConfigurationOverrides` validates deployment policy: MCP_ENABLED=false disables access; true enables AccessTokens when the selected mode is Disabled.
MCP_MODE otherwise wins over saved mode. Explicit true with MCP_MODE=Disabled is rejected.
An explicit availability override locks the MCP mode control, as does MCP_MODE. ORIGIN wins over saved URL. Overridden UI fields are read-only. OAUTH_ENABLED is a
one-time bootstrap import only: explicit true with ORIGIN preserves existing OAuth grants on upgrade.
Token-only installations now require activation. Initial seeding serializes on an advisory lock.

`ServerConfiguration` reads one authoritative database snapshot at the start of each API/protocol request.
PublicOrigin, CORS, auth origin checks, cookies, JWT issuer/audience validators, challenges, discovery and OAuth
services use that snapshot. No request-host/forwarded-header inference; no global mutable token-validation options.
Disabled mode gates /mcp, /oauth and /.well-known before authentication/registration/body processing (404).
Consent APIs additionally require SignIn. Profile list/revoke operations remain available when disabled.
Static files and liveness do not read installation settings.

A settings change rotates Revision and revokes app sessions in the same transaction. OAuth grants bind to a
hash of Revision and effective public URL; requests and codes bind through DataProtection purposes v2.
Pending requests, codes and refresh/access credentials cannot survive a change or become valid when switching back.
A persisted deployment-policy fingerprint rotates the revision and revokes grants when environment overrides
change at startup. All replicas must share the same deployment policy. PATs are never revoked by these changes;
MCP gates access while REST remains available. Accepted operations already in flight are not undone.

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
- Both protocol eras work: 2025 `initialize` and 2026-07-28 `server/discover` (per-request `_meta`, `Mcp-Method`
  header). On 2026-07-28 the SDK answers an unknown method with HTTP 404, which MCP Inspector 2.x treats as a broken
  connection and then drops the `tools/list` it sent alongside. So `McpSetup` answers `resources/list`,
  `resources/templates/list` and `prompts/list` with empty lists (tested). The startup log states the effective public URL, MCP mode and force-off policy.

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

SignIn mode with an HTTPS public URL (HTTP only for localhost) enables the OAuth 2.1 server for `/mcp`.
In AccessTokens mode, protocol handlers return 404 and authorize redirects to the disabled error page.
In Disabled mode, the master gate returns 404 for all protocol routes. Issuer, resource and metadata URLs
come only from the request's validated installation configuration.

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
  time-limited payload (10 min, configuration-bound purpose `OpenFarm.OAuthRequest.v2`) with a random `rid`. Every failure goes to
  `/connect/error?code=…` on OpenFarm, never to the client's redirect URI. `resource` may be omitted; another
  value is `invalid_target`. Requested scopes are ignored: the user chooses on the consent page.
- Consent (`GET`/`POST /api/v1/auth/oauth/consent`, browser session only) re-checks client and redirect on
  every call. Approve creates the pending app with `Id = Guid(rid)` and the redirect URI it consented to, so a
  request is approved at most once (primary key), and returns a 120 s code payload (configuration-bound purpose
  `OpenFarm.OAuthCode.v2`). Deny inserts the same row already revoked, so a denied request is used up too.
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

`Application/Auth/OAuthScopes.cs` is a frozen catalog of 16 scopes (`<area>.<tier>`, tiers Read, Change and
Printer control). `OAuthScopesTests` snapshots every expansion: never widen a released scope, since every app
already granted it would gain the ability; add a new scope instead (the 2026-11 tool rewrite reshaped them before
the first release). Fee profiles are the only setting a scope may reach, and `printers.ready` (Mark ready) is
Printer control, never pre-ticked. Each permission belongs to exactly one scope
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

Tools are shaped around what a person asks an assistant to do, not around REST endpoints: one call per goal,
names next to ids, batches where a person thinks in batches. Design sources: Anthropic's "Writing effective tools
for agents", the MCP 2025-06-18 tools spec, and the Block, Vercel, WorkOS and Phil Schmid MCP guides.

- `Mcp/McpTools.cs` is the one table: each `McpToolSpec` is an SDK tool plus the permissions it needs.
- `McpSetup.ConfigureSessionOptions` lists only the tools the caller may use now
  (`CredentialAccess.EffectiveAsync` = owner permissions ∩ token abilities, or ∩ expand(scopes) for an app).
- `McpCall.Filter` wraps every call: it rechecks access (FORBIDDEN), maps exceptions through
  `DomainExceptionHandler.ToProblem` to `{"error":{"code","message",…}}` tool errors (NOT_FOUND, CONFLICT,
  INVALID_INPUT, FORBIDDEN, INTERNAL_ERROR), and logs one line per call without arguments or results. The message
  is the problem title plus the domain exception's message (what REST returns as `detail`), so the agent sees
  what was wrong; NOT_FOUND names the tool that finds valid ids.

| Area | Read | Change |
|---|---|---|
| Overview | `get_farm_overview` (no permission; each section checks its own and lists the rest in `hidden`) | |
| Printers | `list_printers`, `get_printer` | `mark_printers_ready` |
| Queue | `list_print_jobs`, `get_print_job` (with why a queued job waits) | `reorder_print_job`, `pin_print_job` (no printer unpins), `requeue_print_job`, `pause_print_job`, `cancel_print_job` |
| Production | `plan_production`, `list_production_runs`, `get_production_run` | `queue_production` |
| Library | `search_library`, `get_part`, `get_assembly` (`expand` for the full tree) | `create_library_folder`, `rename_library_folder`, `move_library_items`, `update_parts`, `save_assembly` |
| Pricing | `list_fee_profiles`, `quote_assembly` | `save_fee_profile` |
| Maintenance | `list_maintenance` | `save_maintenance_task`, `assign_maintenance`, `complete_maintenance` |
| Filament | `list_filaments` (stock + forecast + suggested purchase), `list_spools`, `get_farm_stats` | `register_spools` |

Rules for a new tool:
1. A tool may call several Application handlers or services; never EF Core directly. Its permissions are the
   union of the REST endpoints whose work it does (`McpReadToolsTests.Mirrors`, tested), so MCP never reaches more
   than REST. A tool mirroring a `library:*` endpoint also checks the library's read permission per call
   (`request.RequireAsync`), as `PermissionAuthorizationHandler` does by route.
2. Return a view record shaped for agents, never a bare list: MCP structured content must be an object (tested).
   Put names next to ids (printer, part, run, colour), units in field names (`Grams`, `Minutes`, `…Utc`), and
   document fields with `<param>` comments. Reuse a REST DTO only when it already does this.
   `McpSchemas.Portable` (applied by `McpTools.Read`/`Write`) inlines `$ref`s and rewrites nullable
   `"type": [X, "null"]` as `anyOf`, so clients that read one type per schema (Gemini's OpenAPI subset; MCP
   Inspector's portability check) accept every tool (tested). Results leave null fields out, and clients reject a
   whole call whose result misses a required field, so an output schema requires only fields .NET never omits:
   numbers, booleans, ids and dates (tested on every schema).
3. Reads use `McpTools.Read`. Lists page with `McpTools.Page`/`PageSize` (clamped to 1–100) and return
   `page`, `pageSize`, `totalCount` and `hasMore`; end the description with `McpTools.PagingNote`.
4. Descriptions say when to use the tool and which tool gives its ids. Batch arguments are arrays of records with
   `[property: Description]` on each field.
5. Leave out network addresses, serial numbers, file bytes and file URLs.
6. Optional tool arguments need a default (`string? x = null`, `CancellationToken ct = default`): without one
   the SDK marks the argument required and the call fails with "Invalid request".
   `McpCall` drops blank arguments (`""`, whitespace, `null`, also inside objects and lists) before a tool runs,
   since form clients send an empty field that way: a blank optional argument gets its default, a blank required
   one fails as missing.
8. Add every new tool to `McpToolResultsTests.EveryToolsResultMatchesItsOutputSchema`: it calls each tool on real
   data and checks the result against the tool's own output schema, and fails while a tool is not called.
7. Person only: every removal, resuming prints, skipping objects, and settings other than fee profiles, users,
   sessions, tokens and printer connections.

## Writes

- `McpTools.Write` + `request.WriteAsync(dto, run, ct)`: builds the mirrored endpoint's request DTO, runs its
  `IValidator<T>` (INVALID_INPUT with `issues`), runs the same handler, then `ActivityRecorder.RecordAsync` —
  the code the FastEndpoints post-processor uses — so rows and live events match REST. Folder and fee-profile
  requests became `IActivityLoggable` for this, which also logs their REST changes.
- Approval (`McpInstructions.Text`): an explicit request authorizes the routine change it names (library,
  fee profiles, maintenance setup, spool registration, reorder, pin). Requeue, pause and cancel need reviewed
  confirmation of that exact job (tested).
- **Production is previewed, then committed.** `plan_production` is read-only and returns a `planToken`: a
  30-minute `ITimeLimitedDataProtector` payload of the lines, each line's `planVersion` and submission id, the
  destination and the account. `queue_production(planToken)` submits exactly that, so what the user approved is
  what is queued; the same token never queues twice (per-line `SubmissionId`s; a new run uses one). Destination
  `queue` and `run` submit each line through `ProductionDemandService` and report per-line outcomes, so a
  line refused for a changed part does not hide the ones already queued; `newRun` is one atomic
  `CreateProductionRunCommand`. Finite quantities only; backfill stays in the UI.
- **Mark ready** is a tool, gated by the user's word: only for printers the user says in this conversation have
  a clear plate. `mark_printers_ready` takes each printer's `awaitingClearSinceUtc` from `list_printers`;
  `MarkPrinterReadyCommand.ExpectedSinceUtc` refuses a printer whose activity changed since (another print
  ended, so the plate the user cleared may not be this one) with `ChangedSinceRead`. Each printer is handled on
  its own and gets its own activity row.
- Printer commands (`pause_print_job`, `cancel_print_job`) return `{ job, confirmed, note }`: confirmed is false
  until telemetry reports the change, as REST's 202.
- `cancel_print_job` requires `expectedPhase` (`PrintJobPhases.Of`: Queued, Starting, Printing, Paused). A
  dispatched job is still `Status = Queued`, so status alone cannot tell "remove from queue" from "stop the
  printer". The handler checks it after loading the job (`PrintJobPhaseMismatchException` → 409 with
  `currentPhase`); a dispatch racing the save fails the row version (409). REST passes no expectation.

## Filament forecast

`GetFilamentForecastQuery` joins each colour's stock (30-day use), the grams unfinished finite jobs still need
(`GetFilamentShortagesQueryHandler.QueuedDemandAsync`) and the newest spool bought, and `FilamentForecasts.Compute`
decides purchases per material and colour (material exactly, hex ignoring case, as job matching compares them):

- A job line tied to a catalog colour can only print from that colour: its shortfall is bought as that colour.
- What every brand has left after its own jobs forms one pool for lines that name only the material and colour and
  for the next `coverDays` of use (past use counts as interchangeable between brands).
- The pool's shortfall is suggested once, on the brand bought last (most stock when none was bought), in that
  brand's last spool size (1 kg when none).

So two brands of PLA #000000 with 5 kg on one and 0 g on the other suggest nothing for a 1 kg job that names only the
colour, and two empty brands suggest one spool, not two. With one brand per colour this is the plain
`ceil(max(0, coverDays × used30d/30 + queued − onHand) / spool size)`. `list_filaments` shows `queuedGrams` (tied to
the colour) apart from `sharedQueuedGrams` (the same figure on every brand, counted once); `get_farm_overview`'s
short list and `plan_production`'s `shortGrams` use the same rule. `RunsOutOn` is the colour's pool date across brands,
today when the colour's own jobs are already short, and empty beyond ten years. Limits: a linear 30-day pace, and
backfill demand is not counted. The REST `GET /filaments/shortages` still flags every matching brand, by design
("over-reported but never missed").

## Evaluating tools

Change tool names and descriptions against tasks, not taste. Run `./dev.sh up`, create a token under Profile →
Connected apps, connect Claude Code with
`claude mcp add --transport http openfarm http://localhost:6868/mcp --header "Authorization: Bearer ofp_…"`, and
run each task in a fresh session. Record tool calls, errors and tokens; read the transcripts for retries, wrong
ids and unasked questions.

1. "What needs my attention this morning?"
2. "Queue 3 Lamp kits." (expects plan, summary, approval, queue)
3. "I cleared the plates on P1 and P3, mark them ready."
4. "Why is the bracket job not printing?"
5. "What filament should I order to cover the next six weeks?"
6. "I received 10 spools of Bambu PLA Basic Black, 1 kg, 22 each, from the Bambu store."
7. "Move every gridfinity part into Storage/Gridfinity and tag them gridfinity."
8. "Make an assembly Desk set: 2 pen holders and 1 tray, priced at 40% margin."
9. "Add a Shopee fee profile: 18% commission, 2% payment fee, 0.50 per order."
10. "Lubricate the rods every 200 print hours on all X1C printers; it was done last Monday."
11. "Make a production run for the market on Friday: 20 keychains and 5 Desk sets."
12. "What does the Desk set sell for on Shopee, and how much can I discount it?"

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
attributed the same way. `get_farm_overview` reports the same kind and label as `connection`.

App lifecycle rows (`EntityType = ConnectedApp`, `EntityId` = app session id): `ConnectedAppAdded`,
`ConnectedAppRevoked` (`Reason`: `user`, `administrator`, `replaced`, `code-reuse`, `app`) with a
`ReportedState` JSON of client id, name, returns-to label, scopes and expiry, and
`ConnectedAppCredentialReuse`. Never logged or audited: tokens, codes, verifiers, secrets, request payloads,
`Authorization` headers; `client_name` is never logged (DCR logs client id, auth method and redirect kinds).
