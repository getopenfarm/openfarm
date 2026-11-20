---
title: Single sign-on reference
description: Rules, limits, and troubleshooting for OIDC sign-in and account provisioning.
---

This page explains how single sign-on (SSO) and account provisioning behave.
For setup, see [Single sign-on with authentik](single-sign-on.md) and [Account provisioning with authentik](account-provisioning.md).

## Provider requirements

- OpenFarm requests the `openid`, `profile`, and `email` scopes, plus the scopes listed under **Additional scopes**.
  It refuses `offline_access`, because it keeps no provider tokens.
- OpenFarm reads claims from the ID token only. It never calls the userinfo endpoint.
- The issuer in the token must match the **Issuer URL** exactly, including the trailing slash.
- Authentik's global issuer mode, **Same identifier is used for all providers**, is not supported.
- The provider must sign tokens with a certificate key (RSA or ECDSA), so that OpenFarm can check them with the provider's published keys.
  An empty **Signing Key** in authentik means shared-secret HS256 signing, which is not supported.
  Authentik creates `authentik Self-signed Certificate` at its first startup. It is valid for one year, but OpenFarm does not check certificate expiry.
- A changed subject identifier prevents existing identity links from matching. Changing the issuer requires an identity migration plan.

## Authentication settings

- Changes apply immediately on all OpenFarm replicas. No container restart is necessary.
- New installations have internal authentication on, and OIDC and JIT off.
- OpenFarm refuses settings that turn off both internal authentication and OIDC. HTTP issuer URLs are allowed only for localhost development.
- Saved credentials are never returned to the browser. Leave a credential field blank to keep its saved value.
  A new **Issuer URL** or **Client ID** also requires the client credential again.
- The provider and provisioning sections appear only while **OIDC authentication** is on. Turning it off and on before you save keeps your draft.
  Saving with OIDC off ends OIDC sessions and turns off JIT.
- With internal authentication off, only recovery accounts can use a password. Other password sessions stop working, and password changes and resets are blocked for those accounts.
- The login page shows the provider button first. **Use username and password** opens the password form.
- Use the same persistent data-protection keys on all replicas. Do not remove them when you replace a container.

## Account linking

- Linking keeps the account's groups, permissions, history, and details.
- Matching email addresses never link accounts automatically. OpenFarm never merges accounts.
- An external identity can belong to only one OpenFarm account.
- A linking request expires after five minutes, and must finish in the browser session that started it.
- Linking requires a fresh provider sign-in and a signed `auth_time` claim. OpenFarm allows one minute of clock difference between the servers.
- Self-service unlinking is not available.

## Account creation

- The ID token must contain `sub`, `preferred_username`, and a valid `email`. With **Require verified email** on, it must also have `email_verified=true`.
  The display name comes from the optional `name` claim.
- JIT never matches or merges accounts by email. A username or email that another account uses refuses the sign-in.
- JIT never re-enables a disabled account. It does not overwrite account details at later sign-ins.
- JIT accounts have no local password. Their name, email, and username are read-only in OpenFarm.
- The default group cannot be a protected group, such as **Administrators**, because every new account gets it.
- When internal authentication is off, JIT still creates new accounts. Existing local accounts must link while internal authentication is on.

## Group mapping

OpenFarm applies the mapping at every sign-in, to accounts it created through JIT:

- A user in a mapped group gets the OpenFarm group. A user in several mapped groups gets all of them.
- A user in no mapped group gets the default group.
- OpenFarm manages the mapped groups and the default group. Groups assigned by hand under **Users & Groups** stay.
- If you remove a mapping or point it at another group, OpenFarm removes the group it granted at the user's next sign-in.
- If you remove every mapping, the user loses the groups the mapping granted and gets the default group. Groups are then managed under **Users & Groups** again.
- Accounts that were linked by hand never change.
- OpenFarm reads one claim only, the one named in **Groups claim**. A group and an entitlement with the same name cannot both grant access.
- Only top-level claims are supported, not nested ones such as Keycloak's `realm_access.roles`.
  Identity claims such as `sub`, `email`, and `name` are refused, because a user can edit some of them at the provider.
- Some providers refuse an unknown scope. Add only scopes that the provider defines.
- OpenFarm never removes the last enabled administrator at sign-in. The sign-in fails instead, with `last_administrator_would_be_removed`.
- Each group change is in the activity log as `OidcGroupsSynced`.

## Sessions and sign-out

- An SSO session stays signed in while it is used. Each refresh extends it by three days, up to 14 days after sign-in. These limits are fixed.
- OpenFarm does not poll the provider. A change in authentik, such as a removed group or a disabled user, reaches OpenFarm at the next sign-in.
  To apply it sooner, revoke the user's sessions or disable the account under **Users & Groups**.
- A new provider or client credential ends existing SSO sessions. Turning off SSO ends them too.
- Signing out of OpenFarm ends the current browser session only. It does not sign the user out of authentik, so an active authentik session can sign the user in again without a password.

### Back-channel logout

- Authentik sends a signed logout token. OpenFarm checks its issuer, audience, signature, logout event, and issue time.
- A token with `sid` ends the matching sessions. A provider that sends `sid` in logout tokens must also send it in ID tokens.
  A token with only `sub` ends all of that user's SSO sessions.
- Repeated notifications are safe, and a sign-in in progress cannot restore the ended session.
- Back-channel logout does not end local password sessions, personal tokens, or connected apps.
- OpenFarm does not start a provider-wide logout.

## Manage access and offboarding

- Disable an account to revoke its browser sessions, connected apps, and personal tokens. A disabled account keeps its identity reserved.
- Re-enabling the account allows a new sign-in. Revoked credentials stay invalid.
- Removing an account removes its identity link. If JIT is on and authentik still allows the user, a later sign-in creates a new account.

## Troubleshooting

### Troubleshoot provider setup

| Symptom | Cause | Fix |
| --- | --- | --- |
| Sign-in or linking returns to OpenFarm before authentik opens. | OpenFarm cannot load `<Issuer URL>.well-known/openid-configuration`. | Check the **Issuer URL**. The address must return an OpenID configuration document, not a sign-in page. Check DNS, TLS trust, proxy, and firewall from the OpenFarm server. The server log has the details. |
| A browser or curl can load the discovery document, but OpenFarm cannot. | An edge protection rule blocks the request. | OpenFarm sends `User-Agent: OpenFarm`. Allow it. |
| `issuer_mismatch` | The token issuer differs from the saved **Issuer URL**. | Enter the exact issuer, including any trailing slash. The server log shows both values. |

### Troubleshoot account linking

When linking fails, OpenFarm shows a specific message. The server log has `OIDC linking was rejected` with a reason code. It never contains tokens, cookies, or credentials.

| Reason | Fix |
| --- | --- |
| Browser proof or session failure | Sign in to your local account again. Start linking in the same browser, on the same hostname. Do not switch between `localhost` and `127.0.0.1`. |
| Expired or replaced request | Start a new attempt and finish it within five minutes. Do one attempt at a time. |
| `fresh_authentication` | Sign out of authentik and start again. The provider must honor `prompt=login` and `max_age=0`. Check the server clocks. |
| `auth_time_missing` | Trying again does not help. Configure the provider to send one `auth_time` integer in the ID token. |
| `provider_denied` | Check that the provider account can use OpenFarm. |
| `provider_response` | The server log has `OIDC link failed before account work` with the failure type and message. |
| Identity conflict | The identity is linked to another account. Ask an administrator to check it. |

### Troubleshoot a refused sign-in

A refused sign-in always shows the same message on the login page.
The server log has one `OidcSignInRejected` warning that names the cause. It never contains claim values.

| Log reason | Cause | Fix |
| --- | --- | --- |
| `email_unverified` | The ID token has `email_verified=false`. | [Configure verified email](account-provisioning.md#configure-verified-email-in-authentik). |
| `username_claim_missing`, `email_claim_missing_or_invalid` | The ID token has no `preferred_username`, or no valid `email`. | Select the `profile` and `email` scope mappings on the provider, and keep **Include claims in id_token** on. |
| `claim_too_long` | A username, email, or name has more than 256 characters. | Shorten the value in authentik. |
| `account_conflict` | An OpenFarm account already uses this username, email, or identity. | Link the existing account in **My profile → Security**. |
| `Username, email or identity is invalid or already in use.` | The username has characters other than letters, digits, and `- . _ @ +`, or another account has the same email. | Change the authentik username or email, or link the existing account. |
| `The default group must be an existing, unprotected group.` | The default group does not exist or is protected. | Select an existing, unprotected **Default group**. |
| `Each mapped group must be an existing group.` | A mapped OpenFarm group was removed. | Select an existing group for each mapping row. |
| `last_administrator_would_be_removed` | The user would lose **Administrators**, and no other enabled administrator exists. | Make another account an administrator, or add the user to the authentik group again. |
| `not_linked_and_jit_disabled` | The identity is not linked and JIT is off. | Turn on JIT, or link the account in **My profile**. |
| `account_disabled_or_removed` | The account is disabled, or was removed during sign-in. | Re-enable the account under **Users & Groups**. |
| `provider_logout_receipt` | Authentik ended this session before sign-in finished. | Sign in again. |
| `provider_disabled_or_settings_changed`, `identity_conflict` | The settings changed, or two first sign-ins happened at the same time. | Try again. |
| `duplicate_claim_or_invalid_settings` | The ID token repeats a claim, or the saved settings are invalid. | Check the provider's scope mappings and the saved settings. |

### Troubleshoot group mapping

If the Groups claim has no values, the user gets the default group, and the server log has one entry. It never contains claim values.
An entry also appears for a user who has no groups or entitlements, so it is not always an error.

| Entry | Cause | Fix |
| --- | --- | --- |
| `OidcGroupsClaimMissing` | The provider did not send the claim. The scope is not requested, the scope mapping is not on the provider, **Include claims in id_token** is off, the mapping expression failed, or **Groups claim** has the wrong name. | The entry lists the claims present and the scopes requested. Add the scope under **Additional scopes**, select the mapping on the provider, and compare **Groups claim** with the claims present. Check authentik's events for a mapping error. |
| `OidcGroupsClaimEmpty` | The provider sent the claim, but it lists nothing for this user. With entitlements, neither the user nor any of the user's groups is bound to an entitlement. | Add the user to an authentik group. With entitlements, bind the user or a group on the entitlement itself, not on the application. |
| No entry, but the user has only the default group | The claim has values, and none equals a mapping row. | Compare the values with the mapping rows. Matching is exact and case-sensitive. A trailing space does not match. |

## Recover access

If authentik is unavailable, select **Local sign-in** on the login page (or open `/login?local=1`) and sign in as a selected recovery administrator.

If a settings mistake locks everyone out, set this deployment override and recreate the container:

```dotenv
Auth__RecoveryMode=true
```

Recovery mode turns off SSO and provisioning, and lets existing local password accounts sign in. Accounts without a password cannot use it.
The settings page is read-only in recovery mode.
Remove the override and recreate the container to restore the saved settings. Then sign in as a recovery administrator and correct them.

Account administration always keeps at least one enabled recovery administrator while SSO is required.
