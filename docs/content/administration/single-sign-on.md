---
title: Single sign-on with authentik
description: Configure OpenID Connect sign-in and central session logout in Settings.
---

OpenFarm supports single sign-on (SSO) through OpenID Connect (OIDC).
Authentik verifies the user's identity. OpenFarm controls the user's permissions.
A local account can keep its password and use an explicitly linked provider identity.

## Prepare the installation

Use one HTTPS hostname for OpenFarm, including `/api` and `/hubs`.
Set **Settings → Server → Public URL**, or supply the `ORIGIN` deployment override.
OpenFarm and the browser must be able to reach authentik.
Authentik must be able to reach OpenFarm's back-channel logout URL.

Back up the database and persistent data-protection keys before upgrading.
OpenFarm encrypts saved provider credentials with those keys.
Use the same persistent keys on all OpenFarm replicas.
Do not remove the keys when replacing a container.

## Configure authentik

1. Create an OAuth2/OpenID provider and application for OpenFarm.
2. Choose a confidential client and an asymmetric signing key, such as an RSA key using RS256.
3. Enable the **Authorization Code** grant type.
4. Register a **Strict** redirect URI: `https://farm.example.com/api/v1/auth/sso/callback`.
5. Configure `openid`, `profile`, and `email` scope mappings. Include their claims in the ID token.
6. Keep the per-application issuer mode: **Each provider has a different issuer, based on the application slug**.
7. Keep the subject mode stable after users enroll.
8. Apply the application-access policy and MFA policy that your users need.
9. Record the client ID, client credential, and exact issuer URL, including its trailing slash.

Select a signing key rather than leaving that field empty. Shared-secret HS256 signing does not supply the public signing keys used by this setup.

Keep the subject identifier stable. Changing it prevents existing identity links from matching.
OpenFarm requires the discovery authority and token issuer to match exactly.
Use the issuer base URL, such as `https://authentik.company/application/o/openfarm/`.
Do not append `/authorize` to the issuer URL.
Authentik's global issuer mode, **Same identifier is used for all providers**, is not supported.

OpenFarm exchanges authorization codes on the server and uses PKCE.
It uses Pushed Authorization Requests (PAR) when the provider advertises support.
It requests `openid`, `profile`, and `email`; it does not need `offline_access` or retain provider tokens.

## Enable optional OIDC

1. Sign in to OpenFarm as an administrator.
2. Open **Settings → Authentication**.
3. Turn on **OIDC authentication**.
4. Enter the **Issuer URL**, **Client ID**, and **Client credential**. Optional: enter a **Provider name** for the sign-in button. Blank shows **SSO**.
5. Keep **Internal authentication** on while enrolling existing accounts.
6. Select **Save**.
7. Link your administrator account as described below.

Changes apply immediately across OpenFarm replicas. No container restart is required.
New installations enable internal authentication and disable OIDC and JIT.
Enabling OIDC adds the provider's sign-in button. Keep internal authentication on to allow both sign-in methods.
The login page shows the provider button first. The username and password form opens with **Use username and password**.
OpenFarm rejects configurations that disable both methods.
HTTP is allowed only for localhost development.

Provider configuration and account provisioning appear while **OIDC authentication** is on.
Turning it off and on before saving preserves your draft.
Saving it off ends OIDC sessions and disables JIT provisioning.
Settings in disabled sections keep their saved values; unsaved edits to those sections are not applied.

The settings page shows the exact **Redirect URI** and **Back-channel logout URI** for your public address.
Saved credentials are never returned to the browser.
Leave a credential field blank to retain its saved value. Enter a new value to replace it.
Changing the issuer or client ID also requires entering the provider's client credential.

## Link an existing account

1. Sign in to OpenFarm with your local username and password.
2. Open **My profile → Security**.
3. Enter your current password under **Current password**.
4. Select **Link** with the provider name. With no provider name, select **Link SSO**.
5. Authenticate with the intended provider account.
6. Return to OpenFarm and check the linked status.

Linking keeps the account's groups, permissions, history, and details.
Matching email addresses do not link accounts automatically.
An external identity can belong to only one OpenFarm account.
Linking requests expire after five minutes and require the original browser session.
Linking requires fresh provider authentication and a signed `auth_time` claim.
OpenFarm allows one minute of clock skew. Keep both servers' clocks synchronized.

JIT account creation is optional.
See [account provisioning](account-provisioning.md).
Self-service unlinking is not available.

## Disable internal authentication

First link regular accounts and test provider sign-in in a separate browser session.
Keep a separate local administrator for recovery and test its password.

1. Open **Settings → Authentication**.
2. Turn off **Internal authentication**.
3. Select at least one local administrator under **Recovery access**.
4. Select **Save**.

Only selected recovery accounts can use local passwords when SSO is required.
The settings page lists enabled local administrators with a password.
To enforce SSO from a local session, select your current administrator as a recovery account first.
You can also make the change while signed in through the configured provider.

Regular users' existing password sessions stop working.
Password changes and administrator password resets are blocked for those accounts.
Account administration requires at least one enabled recovery administrator to remain.
The login page provides **Use recovery account**.

JIT can enroll new managed accounts while SSO is required.
Existing local accounts must use optional mode to link their identities.

## Sessions and revocation

An SSO session stays signed in while it is used. Each refresh extends it by three days, up to 14 days after sign-in.
A session unused for three days, or 14 days old, expires, and the user must sign in through the provider again.
These limits are fixed.

OpenFarm does not poll the provider. A user removed or disabled there keeps access until their session expires
or the provider sends [back-channel logout](#configure-back-channel-logout). Disable the OpenFarm account to revoke access at once.

Changing the provider or client credential ends existing SSO sessions.
Disabling SSO ends those sessions too. Changing the issuer requires an identity migration plan.

Signing out of OpenFarm ends the current OpenFarm browser session.
It does not sign the user out of authentik or revoke personal tokens and connected apps.
An active authentik session can let the user sign in again without entering a password.

### Configure back-channel logout

1. Open **Settings → Authentication → OpenID Connect**.
2. Copy **Back-channel logout URI**.
3. Open the OpenFarm OAuth2/OIDC provider in authentik.
4. Set **Logout URI** to the copied address.
5. Set **Logout Method** to **Back-channel**.
6. Save the provider and test termination of a user's authentik session.

Authentik sends a signed logout token to OpenFarm.
OpenFarm validates the issuer, audience, signature, logout event, and recent issuance time.
A token with `sid` ends matching provider sessions.
Providers that send session-specific logout tokens must include the same `sid` in ID tokens.
A token with only `sub` ends that user's SSO sessions for the configured provider.
Repeated notifications are safe, and an in-flight login cannot restore the terminated session.
Users need a new sign-in after their session ends.

Back-channel logout leaves local password sessions, personal tokens, and connected-app grants active.
Disable the OpenFarm account to revoke account access through all methods.
Provider-wide logout initiated from OpenFarm is not included.
Provider logout takes effect after authentik successfully delivers its logout notification.

## Troubleshoot provider setup

If linking returns to OpenFarm before authentik opens, check the saved **Issuer URL**.
OpenFarm loads the issuer's `.well-known/openid-configuration` document from its backend.
That address must return an OpenID configuration document, not a sign-in page or an authorization endpoint.
Check connectivity and TLS trust from the OpenFarm server or container, as well as proxy and firewall rules.
The OpenFarm server logs include the startup failure details; the browser shows a provider-configuration message.
OpenFarm sends an identifying `User-Agent: OpenFarm` header when retrieving discovery and signing keys.
Some edge protection rules reject requests that omit this header, even when a browser or curl can retrieve the document.

## Troubleshoot account linking

If linking fails after returning from the provider, OpenFarm shows a specific failure message.
The backend also logs `OIDC linking was rejected` with a fixed reason code and request trace.
These diagnostics do not include tokens, cookies, or credentials.

- For a browser proof or session failure, sign in to your local OpenFarm account again.
  Start linking in the same browser and use the same hostname throughout.
  On a local installation, do not switch between `localhost` and `127.0.0.1`.
- For an expired or replaced request, start a new attempt and complete it within five minutes.
  Complete only one linking attempt at a time.
- For a fresh authentication failure, sign out of the provider and start linking again.
  Administrators must check that the provider honors `prompt=login` and `max_age=0`.
  Check the clocks on both servers. OpenFarm allows one minute of clock difference.
  The backend logs the claim count and authentication age to help identify this failure.
- If the provider did not send a usable `auth_time` (reason `auth_time_missing`), trying again does not help.
  The claim is missing, not a single integer, or repeated.
  Configure the provider to include `auth_time` in the ID token.
- If the provider refused the sign-in (reason `provider_denied`), check that the provider account can use OpenFarm.
- If the issuer does not match (reason `issuer_mismatch`), enter the exact issuer in **Settings → Authentication**,
  including any trailing slash. The backend logs the issuer received and the issuer saved.
- For `provider_response`, the backend logs `OIDC link failed before account work` with the failure type and message.
  The log omits codes, tokens and cookies.
- For an identity conflict, ask an administrator to check the existing account association.
  OpenFarm does not automatically merge accounts.

## Recover access

If authentik is unavailable, select **Use recovery account** and use a designated local administrator.
If a configuration mistake prevents access, set this temporary deployment override and recreate the container:

```dotenv
Auth__RecoveryMode=true
```

Recovery mode temporarily disables SSO and provisioning and allows existing local password accounts to sign in.
Use a local administrator. Passwordless managed accounts cannot use local login.
The settings page is read-only while recovery mode is active.
Remove the override and recreate the container to restore the saved configuration.
Then use a tested recovery administrator to correct the settings.

## References

- [Authentik Portainer integration](https://integrations.goauthentik.io/hypervisors-orchestrators/portainer/)
- [Authentik Arcane integration](https://integrations.goauthentik.io/hypervisors-orchestrators/arcane/)

- [Authentik OAuth2/OIDC provider](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/)
- [Authentik back-channel logout](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/frontchannel_and_backchannel_logout/)
- [Immich integration](https://integrations.goauthentik.io/media/immich/) for strict redirects and back-channel logout.
- [Outline integration](https://integrations.goauthentik.io/documentation/outline/) for verified-email requirements.

Use OpenFarm's callback, stable subject mode, and provisioning settings from this guide.
Other applications' role mappings and logout endpoints are application-specific.
