# Accounts, sessions, and access

OpenFarm uses ASP.NET Identity accounts, 15-minute JWT access tokens, and database-backed browser sessions. A session expires 14 days after sign-in. Refreshing access does not extend that deadline. Access tokens stay in browser memory; the persistent refresh credential is an HttpOnly cookie. Refresh rotates the credential, tolerates concurrent retries for ten seconds, and revokes a session if an older credential is reused afterward.

## Users & Groups

Administrators create accounts with a username, email, temporary password, and groups. Users can change a temporary password in My Profile › Security. Passwords need at least 8 characters. Disabling an account immediately blocks its sessions. Changing your own password keeps the current session and revokes every other one; an administrator reset revokes every session. My Profile › Sessions lists active browser sessions and supports signing out individual devices or all devices. Administrators can list and revoke another user's sessions from the user sheet.

Users inherit the union of their groups' permissions. Extra permissions can add access for one person. The permission editor follows Akaun: None, View, Edit, Full, or Custom per area, with sensitive abilities always explicit. Administrators is a protected group with every permission; it cannot be edited or removed. At least one enabled administrator must remain. Other groups can be removed only when empty. Removing memberships never automatically promotes a user.

The initial groups are Administrators, Operators, and Viewers. Operators have routine business abilities without removal, credential management, remote printer control, settings administration, or audit access. Viewers receive business read abilities. The API remains authoritative even when a caller bypasses the UI. Live events are filtered against current sessions and permissions.

## Upgrade

The additive migration assigns existing accounts to Administrators once, preserving their previous effective access. Review membership after upgrading. New accounts do not inherit administrator access. Existing JWTs require a fresh sign-in because they have no session identifier. Change the default administrator password after the first sign-in.

Migrations run at server startup. Back up the database before deployment and keep the persistent signing and Data Protection keys volume. Rollback requires returning both application and database schema together; do not run the old server against the new session implementation expecting current JWTs to work.

## Deployment

Same-origin hosting is the default and Vite proxies /api and /hubs in development. HTTPS sets Secure cookies automatically. When HTTPS terminates at a reverse proxy, set `Auth__SecureCookies=true` unless trusted forwarded-header handling already makes Request.IsHttps correct. Keep HTTP development limited to trusted local networks.

For a frontend on another origin, set the exact `Cors__AllowedOrigins__0` (and additional numbered origins). Credentialed requests and antiforgery headers are required. If frontend and API are on different *sites*, also set `Auth__CrossSiteCookies=true` and `Auth__SecureCookies=true`; both must use HTTPS. Browsers that block third-party cookies may still prevent this setup; prefer a same-origin reverse proxy.

Refresh and cookie-based logout require the antiforgery token obtained from GET /api/v1/auth/csrf in the X-CSRF-TOKEN header. Browser authentication POSTs also validate Origin. APIs return 401 for invalid sessions and 403 for missing permissions; clients must not treat network failures or 403 as logout. Session and permission changes are recorded in the activity log without password/token contents.

An administrator can reset a forgotten password through Users & Groups. Email recovery and self-registration are not included. Keep more than one trusted administrator. If all administrators lose their passwords, use the installation's database/Identity recovery procedure with a backup; changing seed settings does not overwrite an existing account password.

## Verification

Run backend tests and Release build, regenerate OpenAPI and the Orval client, then run `bun run test`, `bun run lint`, and `bun run build` from web/. User visual verification covers both themes, narrow screens, 200% zoom, focus, and overlay stacking; automated work does not start browser visual checks.
