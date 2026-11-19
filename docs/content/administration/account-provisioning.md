---
title: Account provisioning with authentik
description: Create passwordless accounts at first sign-in with OIDC.
---

OpenFarm can create accounts through OIDC just-in-time provisioning (JIT).
JIT is disabled by default. Configure [OIDC sign-in](single-sign-on.md) first.
The **Account provisioning** section appears while **OIDC authentication** is on.

JIT creates an account when a new user signs in. It does not match or merge accounts by email.
Leave JIT disabled if administrators must create accounts before users can sign in.
Existing local accounts must use explicit OIDC linking.

Apply application-access policies in authentik before enabling JIT.
New accounts receive the configured default OpenFarm group. The default is **Viewers**.
Use an existing empty group if new accounts must have no operational permissions until an administrator grants access.
Protected groups, including **Administrators**, cannot be provisioning targets.

## Enable JIT

1. Open **Settings → Authentication** as an administrator.
2. Turn on **Create accounts on first sign-in (JIT)**.
3. Keep **Require verified email** on beneath the JIT switch.
4. Select the **Default group** for new accounts.
5. Select **Save**.

Changes apply immediately. No container restart is required.

The OIDC ID token must contain `sub`, `preferred_username`, and a valid `email`.
The default policy also requires `email_verified=true`.
The display name comes from the optional `name` claim.
Configure authentik's `profile` and `email` scope mappings to supply these claims in the ID token.
Since authentik 2025.10, the default email mapping returns `email_verified=false`.
JIT account creation fails until the provider supplies a verified address.
Existing linked accounts can still sign in.

OpenFarm rejects username and email collisions. An existing local account must use explicit linking instead.
JIT never re-enables a disabled account. Existing account details are not overwritten on subsequent logins.
Administrators manage group access in OpenFarm.

### Configure verified email in authentik

First configure an email-verification process.
Follow authentik's [Verify email addresses guide](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/verify-email-address/).
If you store the confirmed address in the `email_verified_address` user attribute, use this custom scope mapping:

```python
email = request.user.email
verified_address = request.user.attributes.get("email_verified_address")
return {
    "email": email,
    "email_verified": bool(email and verified_address == email),
}
```

1. Open **Customization → Property Mappings** and create a **Scope Mapping**.
2. Set **Scope name** to `email` and enter the expression above.
3. Open the OpenFarm OAuth2/OIDC provider.
4. Replace the default `email` mapping with the custom mapping. Do not select both mappings.
5. Confirm that the ID token contains the expected email and `email_verified=true` for a verified test user.

Set `email_verified_address` only after the user confirms that address.
Clear it when the address changes, including changes from administrators or synchronized sources.
Do not return `True` unconditionally unless another trusted process verifies every current address.

### Troubleshoot a failed sign-in

A refused sign-in always shows the same message on the login page.
The server log has one `OidcSignInRejected` warning that names the cause. It never contains claim values.

| Log reason | Cause | Fix |
| --- | --- | --- |
| `email_unverified` | The ID token has `email_verified=false`. | [Configure verified email](#configure-verified-email-in-authentik). |
| `username_claim_missing`, `email_claim_missing_or_invalid` | The ID token has no `preferred_username`, or no valid `email`. | Select the `profile` and `email` scope mappings on the authentik provider. OpenFarm reads the ID token only, not the userinfo endpoint. |
| `claim_too_long` | A username, email, or name has more than 256 characters. | Shorten the value in authentik. |
| `account_conflict` | An OpenFarm account already uses this username, email, or identity. | Link the existing account in My profile › Security. JIT never merges accounts. |
| `Username, email or identity is invalid or already in use.` | The username has characters other than letters, digits, and `- . _ @ +`, or another account has the same email. | Change the authentik username or email, or link the existing account. |
| `Provisioning can use only existing, unprotected OpenFarm groups.` | The default group does not exist or is protected. | Select an existing, unprotected default group in Settings → Authentication. |
| `not_linked_and_jit_disabled` | The identity is not linked and JIT is off. | Enable JIT, or link the account in My profile. |
| `account_disabled_or_removed` | The account is disabled, or was removed during sign-in. JIT does not re-enable it. | Re-enable the account under **Users & Groups**. |
| `provider_logout_receipt` | The provider ended this session before sign-in finished. | Sign in again. |
| `provider_disabled_or_settings_changed`, `identity_conflict` | Authentication settings changed, or two first sign-ins raced. | Try again. |
| `duplicate_claim_or_invalid_settings` | The ID token repeats a claim, or the saved settings are invalid. | Check the provider's claim mappings and the saved settings. |

## Manage access and offboarding

JIT accounts have no local password. Local password reset is blocked.
Their name, email, and username are read-only in OpenFarm.
Administrators manage groups and extra permissions under **Users & Groups**.
Provider group changes do not synchronize OpenFarm permissions.
Keep the provider's subject mode stable after users enroll.

Disable an account in OpenFarm to revoke its browser sessions, connected apps, and personal tokens.
Re-enabling the account permits a fresh login. Revoked credentials remain invalid.
Provider disablement is not polled. Back-channel logout ends OIDC browser sessions but does not revoke personal tokens or connected apps.

Removing an account in OpenFarm removes its identity link. If JIT stays enabled and the provider still permits access, a later sign-in can create a new account.
Disable the account to keep its identity reserved during offboarding.

## Verify before rollout

Test a new JIT login and check its default group.
Confirm that existing users can link their accounts without creating duplicates.
Disable a user and check that their browser session and personal token stop working.
Keep a tested local recovery administrator before requiring OIDC.
