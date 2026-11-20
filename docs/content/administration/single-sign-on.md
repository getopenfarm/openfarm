---
title: Single sign-on with authentik
description: Configure OpenID Connect sign-in with authentik in Settings.
---

OpenFarm supports single sign-on (SSO) through OpenID Connect (OIDC).
Authentik verifies the user's identity. OpenFarm controls the user's permissions.

This guide lists only the settings that differ from authentik's defaults.
For the rules behind each setting, see the [SSO reference](sso-reference.md).

## Preparation

The steps use these placeholders:

- `openfarm.company` is the hostname of your OpenFarm installation.
- `authentik.company` is the hostname of your authentik installation.
- `<application_slug>` is the slug of the OpenFarm application in authentik.

Before you start:

- Serve OpenFarm on one HTTPS hostname, including `/api` and `/hubs`.
- Set **Settings → Server → Public URL**, or the `ORIGIN` deployment override.
- Make sure that OpenFarm and the browser can reach authentik, and that authentik can reach OpenFarm.
- Back up the database and the persistent data-protection keys. OpenFarm encrypts the saved client credential with those keys.

## authentik configuration

### Create an application and provider

1. Log in to authentik as an administrator and open the Admin interface.
2. Go to **Applications** > **Applications** and click **New Application**.
   - **Application**: enter a name, for example `OpenFarm`. Note the **Slug**.
   - **Choose a Provider**: select **OAuth2/OpenID Connect**.
   - **Configure the Provider**:
     - **Client type**: `Confidential`. Note the **Client ID** and **Client Secret**.
     - Redirect URI: add `https://openfarm.company/api/v1/auth/sso/callback` with matching mode `Strict`.
     - **Signing Key**: select an RSA key. Do not leave it empty.
     - **Logout URI**: `https://openfarm.company/api/v1/auth/sso/backchannel-logout`
     - **Logout Method**: **Back-channel**
   - **Configure Bindings**: bind the users or groups that may sign in to OpenFarm.
3. Review the summary and create the application.

Keep the other provider defaults:

- The `openid`, `profile`, and `email` scope mappings stay selected, and **Include claims in id_token** stays on. OpenFarm reads claims from the ID token only.
- The issuer mode stays **Each provider has a different issuer, based on the application slug**.

:::caution Do not change the subject mode
After users sign in, keep **Subject mode** unchanged. A new subject breaks every existing account link.
:::

## OpenFarm configuration

### Connect to authentik

1. Sign in to OpenFarm as an administrator.
2. Open **Settings → Authentication**.
3. Turn on **OIDC authentication**.
4. Enter these values:
   - **Issuer URL**: `https://authentik.company/application/o/<application_slug>/`. Keep the trailing slash. Do not add `/authorize`.
   - **Client ID**: the client ID from authentik.
   - **Client credential**: the client secret from authentik.
   - **Provider name** (optional): the label of the sign-in button. Blank shows **SSO**.
5. Keep **Internal authentication** on.
6. Select **Save**.

The page shows the exact **Redirect URI** and **Back-channel logout URI** for your public URL. Compare them with the values in authentik.

### Link your administrator account

1. Open **My profile → Security**.
2. Enter your password under **Current password**.
3. Select **Link** with the provider name. With no provider name, select **Link SSO**.
4. Sign in to authentik with your account.
5. Return to OpenFarm and check that the account shows as linked.

Each existing user links their own account the same way. OpenFarm never links accounts by email address.

### Require SSO (optional)

:::caution Keep a recovery administrator
Before you turn off internal authentication, link the regular accounts and test SSO in a separate browser.
Keep one local administrator with a tested password. It is your way in if authentik fails. See [Recover access](sso-reference.md#recover-access).
:::

1. Open **Settings → Authentication**.
2. Turn off **Internal authentication**.
3. Under **Recovery access**, select at least one local administrator. If you signed in with a password, select your own account.
4. Select **Save**.

Only the selected recovery accounts can then sign in with a password. Other users' password sessions stop working.

### Create accounts at first sign-in (optional)

To let new users sign in without an existing account, see [Account provisioning with authentik](account-provisioning.md).

## Configuration verification

1. Open OpenFarm in a private browser window and select the provider button on the login page.
2. Sign in to authentik. OpenFarm opens signed in.
3. In authentik, end that user's session. OpenFarm signs the user out at the next request.

If a step fails, see [Troubleshooting](sso-reference.md#troubleshooting).

## Resources

- [SSO reference](sso-reference.md)
- [Authentik OAuth2/OIDC provider](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/)
- [Authentik back-channel logout](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/frontchannel_and_backchannel_logout/)
