# Accounts, sessions, and access

OpenFarm uses ASP.NET Identity accounts, 15-minute JWT access tokens, and database-backed browser sessions. A session expires 14 days after sign-in. Refreshing access does not extend that deadline. Access tokens stay in browser memory; the persistent refresh credential is an HttpOnly cookie. Refresh rotates the credential, tolerates concurrent retries for ten seconds, and revokes a session if an older credential is reused afterward.

## Users & Groups

Administrators create accounts with a username, email, temporary password, and groups. Users can change a temporary password in My Profile › Security. Passwords need at least 8 characters. Disabling an account immediately blocks its sessions. Changing your own password keeps the current session and revokes every other one; an administrator reset revokes every session. My Profile › Sessions lists active browser sessions and supports signing out individual devices or all devices. Administrators can list and revoke another user's sessions from the user sheet.

Users inherit the union of their groups' permissions. Extra permissions can add access for one person. The permission editor follows Akaun: None, View, Edit, Full, or Custom per area, with sensitive abilities always explicit. Administrators is a protected group with every permission; it cannot be edited or removed. At least one enabled administrator must remain. Other groups can be removed only when empty. Removing memberships never automatically promotes a user.

The initial groups are Administrators, Operators, and Viewers. Operators have routine business abilities without removal, credential management, remote printer control, settings administration, or audit access. Viewers receive business read abilities. The API remains authoritative even when a caller bypasses the UI. Live events are filtered against current sessions and permissions.

## Personal access tokens

My Profile › Access tokens lets a signed-in user create credentials for scripts and integrations.
Choose a unique name, an expiry date (six months by default), and at least one permission.
Clear the date for a credential that never expires. Expiry is the end of the selected day in UTC.
The creation page shows the credential once, with Copy and Done. It is never recoverable from the server.
The list shows its name, last four characters, access summary, expiry and last-used date.
Regenerate replaces the credential immediately while preserving its permissions and expiry;
an expired token must be replaced with a new token. Revoke and Revoke all take effect on the next request.
A user may hold 50 unrevoked tokens, including expired ones. Revocation releases the name and quota.

Send the credential in an HTTP header over HTTPS:

```sh
curl --header "Authorization: Bearer $OPENFARM_TOKEN" https://farm.example.com/api/v1/printers
```

Select `printers:list` to use that example. Invalid, expired or revoked credentials return 401;
a valid credential without the required permission returns 403. Personal access tokens work with
REST APIs and the MCP endpoint `/mcp` (see `dev-notes/MCP.md`); browser JWTs never work on `/mcp`.
SignalR live events require a browser session. Do not place personal credentials in URLs.
A write made through a token is stamped in the activity log as `details.via` (`Token`, token id, name).

The API intersects the token's selected abilities with its owner's **current** permissions on every
request. Removing group or individual access therefore also limits existing tokens. Tokens cannot
manage accounts, browser sessions, or other tokens; those operations require a signed-in browser.
Unlike Akaun's separate group delegation ceiling, OpenFarm currently uses the owner's current access
as its ceiling. Sensitive abilities such as remote printer control must be selected explicitly;
None/View/Edit/Full presets do not include them.

Users & Groups lets an administrator list and revoke another user's tokens, individually or all at once.
Administrators cannot create or regenerate another user's credential. Changing or resetting a password
revokes all personal tokens; changing your own password still preserves the current browser session.
Disabling an account revokes its tokens and sessions. Re-enabling it does not restore those credentials.

Credentials use an `ofp_` prefix plus 32 cryptographically random bytes encoded as hex. Only a SHA-256
hash and the last four characters are stored. Lifecycle actions are audited with non-secret metadata;
credentials and hashes are excluded. Last-used writes are limited to once per minute per token.
Creation and regeneration serialize on the owner row, and the database enforces unique unrevoked names.

Browser-only management endpoints:

- `GET /api/v1/auth/tokens/options`: catalog and abilities the owner may select.
- `GET/POST /api/v1/auth/tokens`: list metadata or create a credential.
- `POST /api/v1/auth/tokens/{id}/regenerate`: rotate and reveal the replacement once.
- `DELETE /api/v1/auth/tokens/{id}` and `POST /api/v1/auth/tokens/revoke-all`: revoke.
- `GET/DELETE /api/v1/admin/users/{id}/tokens`: administrator list/revoke all.
- `DELETE /api/v1/admin/users/{id}/tokens/{tokenId}`: administrator revoke one.

The additive PersonalAccessTokens migration runs at startup. Existing browser sessions continue to work.
Back up the database before deployment. No existing account receives a personal token automatically.

## Connected apps (OAuth)

With SignIn mode and an HTTPS public URL, MCP clients can sign in instead of using a token (rules and
protocol: `dev-notes/MCP.md`). A connected app is a `BrowserSessions` row with `ClientId` and `Scopes` set, so
it reuses refresh rotation, the 10-second retry, reuse revocation and every revocation trigger. Its access
token has issuer `ORIGIN` and audience `ORIGIN/mcp` and works only on `/mcp`; its refresh credential (`ofr_` +
64 hex) works only at `/oauth/token`. A browser session never acts as an app and an app never as a browser
session: My Profile › Sessions lists browsers only, and the users list counts browsers only.

Revocation: Profile › Connected apps (`DELETE /api/v1/auth/sessions/{id}`), the admin user page
(`DELETE /api/v1/admin/users/{id}/sessions/{sessionId}`), the admin revoke-all, a password change or reset,
disabling the account, deleting the user, reconnecting the same client, a replayed code, refresh reuse after
10 seconds, and `/oauth/revoke`. Sign out and your own "Revoke all sessions" keep apps connected.
Consent and app management happen only in a browser session.

- `GET /api/v1/auth/apps`: MCP URL, whether sign-in is on, and your connected apps.
- `GET /api/v1/admin/users/{id}/apps`: another user's connected apps.
- `GET/POST /api/v1/auth/oauth/consent`: the consent page's request details and decision.

The additive `ConnectedApps` migration adds the `OAuthClients` table and two nullable `BrowserSessions`
columns. `ServerSecuritySettings` subsequently adds installation configuration and app identity binding. MCP now defaults to Disabled.

## Upgrade

The additive migration assigns existing accounts to Administrators once, preserving their previous effective access. Review membership after upgrading. New accounts do not inherit administrator access. Existing JWTs require a fresh sign-in because they have no session identifier. Change the default administrator password after the first sign-in.

Migrations run at server startup. Back up the database before deployment and keep the persistent signing and Data Protection keys volume. Rollback requires returning both application and database schema together; do not run the old server against the new session implementation expecting current JWTs to work.

## Deployment

Same-origin hosting is the default. Configure the installation public URL under Settings → Server, or supply
ORIGIN as a deployment override. The URL is normalized and validated without trusting request Host or forwarded
headers. It supplies trusted browser origins, CORS, Secure refresh cookies and MCP/OAuth identity. UI changes apply
on the next request without restart; environment changes require container recreation. Use the HTTPS hostname
for sign-in after enabling secure cookies. Saved public URLs remain after an override is removed.

Installation settings live separately from FarmSettings. Only actual administrator browser sessions can read or
write `/api/v1/admin/settings/server` and `/api/v1/admin/settings/mcp`; writes use the signed-in session and require the latest Revision. MCP mode defaults to Disabled. MCP_ENABLED=false forces it off;
MCP_MODE overrides saved mode. Legacy OAUTH_ENABLED=true is imported once during ServerSettings bootstrap.
See `dev-notes/MCP.md` for snapshot consistency, deployment fingerprints and grant invalidation.

The public URL does not configure DNS, TLS or proxy routing. Route the whole hostname to OpenFarm, including
/api, /hubs, /mcp, /oauth and /.well-known. The built frontend uses relative API/hub URLs.

Without a public URL, HTTPS requests still set Secure refresh cookies automatically. Auth__SecureCookies=true
remains available. Keep HTTP development limited to trusted local networks. Explicit Cors__AllowedOrigins remain
additive; for cross-site cookies retain Auth__CrossSiteCookies=true and Auth__SecureCookies=true. Prefer a
same-origin reverse proxy because browsers may block third-party cookies.

Refresh and cookie-based logout require the antiforgery token obtained from GET /api/v1/auth/csrf in
X-CSRF-TOKEN. Browser authentication POSTs also validate Origin. APIs return 401 for invalid sessions and 403
for missing permissions; clients must not treat network failures or 403 as logout. Session and permission changes
are recorded without password/token contents.

An administrator can reset a forgotten password through Users & Groups. Email recovery and self-registration
are not included. Keep more than one administrator. If every administrator loses access, recover through the
installation's database/Identity procedure with a backup; seed settings never overwrite an existing password.

## Verification

Run backend tests and Release build, regenerate OpenAPI and the Orval client, then run `bun run test`, `bun run lint`, and `bun run build` from web/. User visual verification covers both themes, narrow screens, 200% zoom, focus, and overlay stacking; automated work does not start browser visual checks.
