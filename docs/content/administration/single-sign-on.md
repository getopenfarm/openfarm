---
title: Single sign-on with authentik
description: Connect existing OpenFarm accounts to authentik with OpenID Connect.
---

OpenFarm supports single sign-on (SSO) through OpenID Connect (OIDC).
Authentik verifies the user's identity. OpenFarm controls the user's permissions.
The same OpenFarm account can have a local password and a linked authentik identity.

## Prepare the installation

Use one HTTPS hostname for OpenFarm, including `/api` and `/hubs`.
Set the public URL under **Settings → Server**, or supply the `ORIGIN` deployment override.
See [installation configuration](../installation/configuration.md).
OpenFarm must be able to reach authentik's discovery, token, and signing-key endpoints.
The browser must also be able to reach authentik.

Back up the database and persistent keys before upgrading.
The upgrade adds a nullable column to browser sessions. Existing password sessions continue to work while OIDC is optional.

## Configure authentik

1. Create an OAuth2/OpenID provider in authentik.
2. Choose a confidential client and a signing key.
3. Register this exact redirect URI, replacing the hostname:

   ```text
   https://farm.example.com/api/v1/auth/sso/callback
   ```

4. Configure scope mappings for `openid`, `profile`, and `email`.
5. Create an authentik application and attach the provider.
6. Apply the required access policy and MFA policy in authentik.
7. Record the client ID, client credential, and issuer URL from the provider configuration.

Keep the provider's subject identifier stable. Changing it prevents existing identity links from matching.

## Enable optional OIDC

Add these values to the installation's deployment environment. The URLs and credentials below are placeholders.

```dotenv
ORIGIN=https://farm.example.com
Auth__Oidc__Enabled=true
Auth__Oidc__DisplayName=authentik
Auth__Oidc__Authority=https://auth.example.com/application/o/openfarm/
Auth__Oidc__ClientId=replace-with-client-id
Auth__Oidc__ClientSecret=replace-with-client-credential
Auth__Oidc__Required=false
Auth__Oidc__SessionHours=8
```

Use the exact issuer URL, including its trailing slash, for `Authority`.
Protect the environment file because it contains credentials.
Recreate the OpenFarm container after changing these settings.

OIDC is disabled by default. Enabling it adds **Sign in with authentik** and keeps local login available.
HTTP is permitted only for localhost development. Prefer the Vite proxy for local development so the browser uses one origin.

## Link an existing account

1. Sign in to OpenFarm with your local username and password.
2. Open **My profile → Security**.
3. Enter your current password under **Confirm local password**.
4. Select **Link authentik**.
5. Authenticate with the intended authentik account.
6. Return to OpenFarm and check the linked status.

You can now select **Sign in with authentik** on the login page.
Linking keeps your existing groups, permissions, history, and account details.
Matching email addresses do not link accounts automatically.
An authentik identity can belong to only one OpenFarm account.
Linking requests expire after five minutes and require the original browser session.

Automatic account creation through JIT and lifecycle/group synchronization through SCIM are optional.
See [account provisioning](account-provisioning.md) to enable them.
Self-service unlinking is not available.

## Require OIDC

First link all regular accounts and test OIDC sign-in in a separate browser session.
Keep a separate local administrator for recovery. Test its password before enforcing OIDC.
Use that account's OpenFarm user ID, rather than its username, in the configuration.
Administrators can read the ID from the account page URL under **Users & groups**.

```dotenv
Auth__Oidc__Required=true
Auth__Oidc__RecoveryUserIds__0=replace-with-recovery-administrator-uuid
```

Only explicitly listed recovery accounts can use password login when OIDC is required.
Regular users' existing password sessions stop working. Password changes and administrator password resets are blocked for those accounts.
The login page shows **Use recovery account** for the designated accounts.
Startup and account administration require at least one enabled recovery administrator with a password.

JIT or SCIM can enroll new managed accounts while OIDC is required.
Optional mode is still needed to link an existing local account through its local login.
Do not give normal users recovery exemptions to bypass enrollment.

## Sessions and revocation

SSO browser sessions last eight hours by default. `SessionHours` accepts values from 1 through 24.
Refreshing access does not extend the deadline. After expiry, sign in through authentik again.
Changing the OIDC authority or client ID invalidates existing SSO sessions.
Disabling OIDC also invalidates those sessions. Changing the authority requires new account links.

Signing out of OpenFarm ends the current OpenFarm browser session.
It does not sign you out of authentik or disconnect personal tokens and connected apps.
An active authentik session can let you sign in again without entering your password.

Disabling a user only in authentik does not immediately end their existing OpenFarm sessions.
To remove access immediately, disable the OpenFarm account as well.
That action revokes browser sessions, personal tokens, and connected-app access.
SCIM deactivation can revoke OpenFarm access centrally when [account provisioning](account-provisioning.md) is enabled.
OIDC back-channel logout and provider-wide logout are not included.

## Recover access

If authentik is unavailable, select **Use recovery account** and use the designated local administrator.
If the provider configuration is incorrect, set `Required=false` and `Enabled=false`, then recreate the container.
Existing local account passwords remain available in optional mode.
Never rely on the initial account seed to overwrite an existing password.

If sign-in fails, check the exact issuer, redirect URI, signing key, public URL, and authentik access policies.
Use the HTTPS OpenFarm hostname so the browser sends the required cookies.
